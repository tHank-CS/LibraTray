using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LibraTray.Core.Protocol;

namespace LibraTray.MockDevice;

internal sealed class MockDeviceServer
{
    private static readonly TimeSpan RestartDowntime = TimeSpan.FromMilliseconds(200);

    private readonly MockOptions _options;
    private readonly MockState _state;
    private bool _restartDowntimePending;
    private bool _restartTriggered;

    public MockDeviceServer(MockOptions options)
    {
        _options = options;
        _state = new MockState(options.Model, options.Name);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var tcpListener = new TcpListener(
            _options.ListenAddress,
            _options.TcpPort);
        using var serverCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        UdpClient? udpClient = null;

        try
        {
            tcpListener.Start();
            udpClient = new UdpClient(
                new IPEndPoint(_options.ListenAddress, _options.DiscoveryPort));

            WriteStatus(
                $"模式={_options.Mode} TCP={_options.ListenAddress}:{_options.TcpPort}"
                + $" UDP discovery={_options.ListenAddress}:{_options.DiscoveryPort}");

            Task tcpTask = RunTcpLoopAsync(tcpListener, serverCancellation.Token);
            Task udpTask = RunUdpLoopAsync(udpClient, serverCancellation.Token);
            Task firstCompleted = await Task
                .WhenAny(tcpTask, udpTask)
                .ConfigureAwait(false);

            if (firstCompleted.IsFaulted)
            {
                serverCancellation.Cancel();
            }

            await Task.WhenAll(tcpTask, udpTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (serverCancellation.IsCancellationRequested)
        {
            // Expected for Ctrl+C, duration expiry, or peer-loop shutdown.
        }
        finally
        {
            serverCancellation.Cancel();
            udpClient?.Dispose();
            tcpListener.Stop();
        }
    }

    private async Task RunTcpLoopAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using TcpClient client = await listener
                .AcceptTcpClientAsync(cancellationToken)
                .ConfigureAwait(false);
            client.NoDelay = true;
            WriteStatus($"TCP 客户端已连接：{client.Client.RemoteEndPoint}");

            try
            {
                await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                WriteStatus($"TCP 客户端 I/O 结束：{exception.Message}");
            }
            catch (SocketException exception)
            {
                WriteStatus($"TCP 客户端网络结束：{exception.SocketErrorCode}");
            }
            catch (YeelightProtocolException exception)
            {
                WriteStatus($"TCP 客户端协议错误，已断开该客户端：{exception.Message}");
            }

            WriteStatus("TCP 客户端已断开。");

            if (_restartDowntimePending)
            {
                _restartDowntimePending = false;
                client.Dispose();
                listener.Stop();
                WriteStatus(
                    "restart 模式：TCP 监听固定暂停 200 ms，"
                    + "用于确定性验证断线后的重新连接。");
                await Task.Delay(RestartDowntime, cancellationToken).ConfigureAwait(false);
                listener.Start();
                WriteStatus("restart 模式：TCP 监听已恢复，后续请求按 normal 响应。");
            }
        }
    }

    private async Task HandleClientAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        NetworkStream stream = client.GetStream();
        var framer = new CrlfMessageFramer();
        byte[] readBuffer = new byte[8 * 1024];

        if (_options.Mode == MockMode.Props)
        {
            await WriteWholeFrameAsync(
                stream,
                MockProtocol.CreateProps(CreatePartialProps()),
                cancellationToken).ConfigureAwait(false);
        }
        else if (_options.Mode == MockMode.BadProps)
        {
            WriteStatus(
                "BAD PROPS：主动通知将故意与 query 真实状态冲突，"
                + "用于验证客户端一致性处理。");
            await WriteWholeFrameAsync(
                stream,
                MockProtocol.CreateProps(CreateIncorrectProps()),
                cancellationToken).ConfigureAwait(false);
        }

        while (true)
        {
            int bytesRead = await stream
                .ReadAsync(readBuffer, cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return;
            }

            IReadOnlyList<byte[]> frames = framer.Append(
                readBuffer.AsSpan(0, bytesRead));
            foreach (byte[] frame in frames)
            {
                if (frame.Length == 0)
                {
                    continue;
                }

                WriteStatus($"RX RAW: {Encoding.UTF8.GetString(frame)}");
                bool keepConnection = await HandleFrameAsync(
                    stream,
                    frame,
                    cancellationToken).ConfigureAwait(false);
                if (!keepConnection)
                {
                    return;
                }
            }
        }
    }

    private async Task<bool> HandleFrameAsync(
        NetworkStream stream,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        if (_options.Mode == MockMode.Disconnect)
        {
            WriteStatus("disconnect 模式：收到首帧后主动断开，不发送响应。");
            return false;
        }

        if (_options.Mode == MockMode.Restart && !_restartTriggered)
        {
            _restartTriggered = true;
            _restartDowntimePending = true;
            _state.Reset();
            WriteStatus(
                "restart 模式：首次完整请求触发一次模拟重启，"
                + "状态已重置且当前连接不响应并断开。");
            return false;
        }

        if (_options.Mode == MockMode.NoResponse)
        {
            WriteStatus("no-response 模式：已消费请求但保持静默。");
            return true;
        }

        bool parsed = MockRequest.TryParse(frame, out MockRequest? request, out string error);
        int responseId = request?.Id ?? 1;
        byte[] response;

        if (!parsed || request is null)
        {
            response = MockProtocol.CreateError(responseId, -5000, error);
        }
        else if (_options.Mode == MockMode.Error)
        {
            response = MockProtocol.CreateError(
                request.Id,
                -5000,
                "mock error mode");
        }
        else
        {
            MockReply reply = _state.Execute(request);
            response = reply.IsSuccess
                ? MockProtocol.CreateSuccess(request.Id, reply.Results)
                : MockProtocol.CreateError(
                    request.Id,
                    reply.ErrorCode,
                    reply.ErrorMessage);
        }

        if (_options.Mode == MockMode.Delay)
        {
            WriteStatus(
                $"delay 模式：延迟 {_options.Delay.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)} ms。");
            await Task.Delay(_options.Delay, cancellationToken).ConfigureAwait(false);
        }

        await WriteResponseByModeAsync(
            stream,
            response,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task WriteResponseByModeAsync(
        NetworkStream stream,
        byte[] response,
        CancellationToken cancellationToken)
    {
        switch (_options.Mode)
        {
            case MockMode.Split:
                await WriteSplitFrameAsync(
                    stream,
                    response,
                    cancellationToken).ConfigureAwait(false);
                break;

            case MockMode.Coalesce:
                byte[] props = MockProtocol.CreateProps(_state.Snapshot());
                byte[] combined = new byte[response.Length + props.Length];
                response.CopyTo(combined, 0);
                props.CopyTo(combined, response.Length);
                WriteStatus(
                    $"TX COALESCED RAW: {Encoding.UTF8.GetString(combined).TrimEnd()}");
                await stream
                    .WriteAsync(combined, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case MockMode.Props:
                await WriteWholeFrameAsync(
                    stream,
                    response,
                    cancellationToken).ConfigureAwait(false);
                await WriteWholeFrameAsync(
                    stream,
                    MockProtocol.CreateProps(CreatePartialProps()),
                    cancellationToken).ConfigureAwait(false);
                break;

            case MockMode.BadProps:
                await WriteWholeFrameAsync(
                    stream,
                    response,
                    cancellationToken).ConfigureAwait(false);
                WriteStatus(
                    "BAD PROPS：发送与真实 query 状态故意冲突的主动通知。");
                await WriteWholeFrameAsync(
                    stream,
                    MockProtocol.CreateProps(CreateIncorrectProps()),
                    cancellationToken).ConfigureAwait(false);
                break;

            default:
                await WriteWholeFrameAsync(
                    stream,
                    response,
                    cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private static async Task WriteWholeFrameAsync(
        NetworkStream stream,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        WriteStatus($"TX RAW: {Encoding.UTF8.GetString(frame).TrimEnd()}");
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteSplitFrameAsync(
        NetworkStream stream,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        int firstLength = Math.Max(1, frame.Length / 3);
        int secondLength = Math.Max(1, (frame.Length - firstLength) / 2);
        int thirdLength = frame.Length - firstLength - secondLength;
        int[] lengths = [firstLength, secondLength, thirdLength];
        int offset = 0;

        for (int index = 0; index < lengths.Length; index++)
        {
            int length = lengths[index];
            if (length <= 0)
            {
                continue;
            }

            WriteStatus(
                $"TX SPLIT {index + 1}/{lengths.Length}：{length} bytes");
            await stream
                .WriteAsync(frame.AsMemory(offset, length), cancellationToken)
                .ConfigureAwait(false);
            offset += length;

            if (offset < frame.Length)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(30),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RunUdpLoopAsync(
        UdpClient client,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            UdpReceiveResult datagram = await client
                .ReceiveAsync(cancellationToken)
                .ConfigureAwait(false);
            string request = Encoding.ASCII.GetString(datagram.Buffer);

            if (!request.StartsWith("M-SEARCH * HTTP/1.1", StringComparison.OrdinalIgnoreCase)
                || !request.Contains("ST: wifi_bulb", StringComparison.OrdinalIgnoreCase))
            {
                WriteStatus(
                    $"忽略来自 {datagram.RemoteEndPoint} 的非 Yeelight discovery 报文。");
                continue;
            }

            byte[] response = Encoding.UTF8.GetBytes(CreateDiscoveryResponse());
            await client
                .SendAsync(response, datagram.RemoteEndPoint, cancellationToken)
                .ConfigureAwait(false);
            WriteStatus($"已响应 discovery：{datagram.RemoteEndPoint}");
        }
    }

    private string CreateDiscoveryResponse()
    {
        IReadOnlyDictionary<string, string> state = _state.Snapshot();
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 200 OK\r\n");
        builder.Append("Cache-Control: max-age=3600\r\n");
        builder.Append(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Date: {DateTimeOffset.UtcNow:R}\r\n"));
        builder.Append("Ext:\r\n");
        builder.Append(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Location: yeelight://{_options.ListenAddress}:{_options.TcpPort}\r\n"));
        builder.Append("id: 0x0000000000000015\r\n");
        builder.Append(
            string.Create(
                CultureInfo.InvariantCulture,
                $"model: {_options.Model}\r\n"));
        builder.Append("fw_ver: 100\r\n");
        builder.Append(
            string.Create(
                CultureInfo.InvariantCulture,
                $"support: {MockState.SupportedMethods}\r\n"));
        AppendStateHeader(builder, state, "power");
        AppendStateHeader(builder, state, "bright");
        AppendStateHeader(builder, state, "ct");
        AppendStateHeader(builder, state, "rgb");
        AppendStateHeader(builder, state, "bg_power");
        AppendStateHeader(builder, state, "bg_bright");
        AppendStateHeader(builder, state, "bg_ct");
        AppendStateHeader(builder, state, "bg_rgb");
        AppendStateHeader(builder, state, "bg_hue");
        AppendStateHeader(builder, state, "bg_sat");
        AppendStateHeader(builder, state, "bg_lmode");
        builder.Append(
            string.Create(
                CultureInfo.InvariantCulture,
                $"name: {_options.Name}\r\n"));
        builder.Append("\r\n");
        return builder.ToString();
    }

    private Dictionary<string, string> CreateIncorrectProps()
    {
        var properties = new Dictionary<string, string>(
            _state.Snapshot(),
            StringComparer.Ordinal);
        properties["power"] = properties["power"] == "on" ? "off" : "on";
        properties["bright"] = properties["bright"] == "1" ? "99" : "1";
        properties["bg_power"] = properties["bg_power"] == "on" ? "off" : "on";
        properties["bg_bright"] = properties["bg_bright"] == "1" ? "99" : "1";
        return properties;
    }

    private Dictionary<string, string> CreatePartialProps()
    {
        IReadOnlyDictionary<string, string> state = _state.Snapshot();
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["power"] = state["power"],
            ["bg_power"] = state["bg_power"],
        };
    }

    private static void AppendStateHeader(
        StringBuilder builder,
        IReadOnlyDictionary<string, string> state,
        string name)
    {
        builder.Append(name);
        builder.Append(": ");
        builder.Append(state[name]);
        builder.Append("\r\n");
    }

    private static void WriteStatus(string message)
    {
        var safeMessage = new StringBuilder(message.Length);
        foreach (char character in message)
        {
            if (char.IsControl(character)
                && character is not ('\r' or '\n' or '\t'))
            {
                safeMessage.Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"\\u{(int)character:X4}"));
            }
            else
            {
                safeMessage.Append(character);
            }
        }

        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"[{DateTimeOffset.Now:O}] {safeMessage}"));
    }
}
