using System.Globalization;
using System.Net.Sockets;
using System.Text;
using LibraTray.Core.Networking;
using LibraTray.Core.Protocol;

namespace LibraTray.Probe;

internal sealed class ProbeTcpSession : IAsyncDisposable
{
    private readonly ProbeOutput _output;
    private readonly TcpClient _client = new()
    {
        NoDelay = true,
    };
    private readonly CrlfMessageFramer _framer = new();
    private readonly byte[] _readBuffer = new byte[8 * 1024];
    private readonly Dictionary<int, IReadOnlyList<int>> _sensitiveResultIndexes = [];
    private readonly Dictionary<int, IReadOnlyList<int>> _credentialResultIndexes = [];

    private NetworkStream? _stream;
    private int _nextRequestId;

    public ProbeTcpSession(ProbeOutput output)
    {
        _output = output;
    }

    public async Task ConnectAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _output.RegisterConnectionTarget(host);

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);

        try
        {
            System.Net.IPAddress address = await ProbeDiscovery.ResolveIpv4Async(
                host,
                operationCancellation.Token).ConfigureAwait(false);
            await _client
                .ConnectAsync(address, port, operationCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"连接 {host}:{port} 超时。");
        }

        _stream = _client.GetStream();
        await _output.InfoAsync(
            $"已连接 TCP {host}:{port}",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<YeelightSuccessResponse> SendCommandAsync(
        string method,
        IEnumerable<object?>? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream
            ?? throw new InvalidOperationException("TCP 会话尚未连接。");
        int requestId = Interlocked.Increment(ref _nextRequestId);
        object?[] parameterValues = parameters?.ToArray() ?? [];
        byte[] frame = YeelightRequestSerializer.Serialize(
            new YeelightRequest(requestId, method, parameterValues));
        string json = Encoding.UTF8.GetString(frame.AsSpan(0, frame.Length - 2));

        if (string.Equals(method, "get_prop", StringComparison.Ordinal))
        {
            int[] sensitiveIndexes = parameterValues
                .Select((value, index) => new { value, index })
                .Where(item =>
                    item.value is string name
                    && DiagnosticRedactor.IsSensitivePropertyName(name))
                .Select(item => item.index)
                .ToArray();
            if (sensitiveIndexes.Length > 0)
            {
                _sensitiveResultIndexes[requestId] = sensitiveIndexes;
            }

            int[] credentialIndexes = parameterValues
                .Select((value, index) => new { value, index })
                .Where(item =>
                    item.value is string name
                    && DiagnosticRedactor.IsCredentialPropertyName(name))
                .Select(item => item.index)
                .ToArray();
            if (credentialIndexes.Length > 0)
            {
                _credentialResultIndexes[requestId] = credentialIndexes;
            }
        }

        await _output.SentJsonAsync(json, cancellationToken).ConfigureAwait(false);

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);

        try
        {
            await stream
                .WriteAsync(frame, operationCancellation.Token)
                .ConfigureAwait(false);

            while (true)
            {
                IReadOnlyList<YeelightMessage> messages = await ReadMessagesAsync(
                    operationCancellation.Token).ConfigureAwait(false);
                YeelightResponse? matchingResponse = messages
                    .OfType<YeelightResponse>()
                    .FirstOrDefault(response => response.Id == requestId);

                switch (matchingResponse)
                {
                    case YeelightSuccessResponse success:
                        return success;

                    case YeelightErrorResponse error:
                        throw new ProbeCommandRejectedException(
                            error.Id,
                            error.Code,
                            error.Message);
                }
            }
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"命令 {method} 在 {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} 秒内无响应。");
        }
        finally
        {
            _sensitiveResultIndexes.Remove(requestId);
            _credentialResultIndexes.Remove(requestId);
        }
    }

    public async Task ListenAsync(
        TimeSpan? duration,
        CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("TCP 会话尚未连接。");
        }

        using var durationCancellation = duration is null
            ? null
            : new CancellationTokenSource(duration.Value);
        using var operationCancellation = durationCancellation is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                durationCancellation.Token);

        try
        {
            while (true)
            {
                await ReadMessagesAsync(operationCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (
            durationCancellation?.IsCancellationRequested == true
            && !cancellationToken.IsCancellationRequested)
        {
            await _output.InfoAsync(
                "监听时间已到，安全断开。",
                cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private async Task<IReadOnlyList<YeelightMessage>> ReadMessagesAsync(
        CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream
            ?? throw new InvalidOperationException("TCP 会话尚未连接。");

        int bytesRead = await stream
            .ReadAsync(_readBuffer, cancellationToken)
            .ConfigureAwait(false);
        if (bytesRead == 0)
        {
            throw new YeelightDisconnectedException("设备关闭了 TCP 连接。");
        }

        IReadOnlyList<byte[]> frames = _framer.Append(
            _readBuffer.AsSpan(0, bytesRead));
        var messages = new List<YeelightMessage>(frames.Count);

        foreach (byte[] frame in frames)
        {
            if (frame.Length == 0)
            {
                continue;
            }

            string raw = Encoding.UTF8.GetString(frame);
            IReadOnlyList<int>? pendingCredentialIndexes =
                GetPendingIndexes(_credentialResultIndexes);
            string logRaw = pendingCredentialIndexes is not null
                ? DiagnosticRedactor.RedactSensitiveResultValues(
                    raw,
                    pendingCredentialIndexes)
                : raw;
            IReadOnlyList<int>? pendingSensitiveIndexes =
                GetPendingIndexes(_sensitiveResultIndexes);
            if (_output.RedactionEnabled && pendingSensitiveIndexes is not null)
            {
                logRaw = DiagnosticRedactor.RedactSensitiveResultValues(
                    logRaw,
                    pendingSensitiveIndexes);
            }

            try
            {
                YeelightMessage message = YeelightMessageParser.Parse(frame);
                RegisterSensitiveResponseValues(message);

                await _output.RawResponseAsync(
                    "TCP",
                    logRaw,
                    logRaw,
                    cancellationToken).ConfigureAwait(false);
                messages.Add(message);
                await _output.ProtocolMessageAsync(
                    message,
                    pendingCredentialIndexes,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (YeelightProtocolException exception)
            {
                string malformedLog = string.Create(
                    CultureInfo.InvariantCulture,
                    $"[REDACTED-MALFORMED-FRAME bytes={frame.Length}]");
                await _output.RawResponseAsync(
                    "TCP",
                    malformedLog,
                    malformedLog,
                    cancellationToken).ConfigureAwait(false);
                await _output.WarningAsync(
                    $"收到无法解析的协议帧：{exception.Message}",
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return messages;
    }

    private static int[]? GetPendingIndexes(
        IReadOnlyDictionary<int, IReadOnlyList<int>> indexMap)
    {
        int[] indexes = indexMap.Values
            .SelectMany(static value => value)
            .Distinct()
            .Order()
            .ToArray();
        return indexes.Length == 0 ? null : indexes;
    }

    private void RegisterSensitiveResponseValues(YeelightMessage message)
    {
        if (message is not YeelightSuccessResponse success
            || success.Id > int.MaxValue
            || !_sensitiveResultIndexes.TryGetValue(
                (int)success.Id,
                out IReadOnlyList<int>? indexes))
        {
            return;
        }

        foreach (int index in indexes)
        {
            if (index >= success.Results.Count)
            {
                continue;
            }

            System.Text.Json.JsonElement result = success.Results[index];
            _output.RegisterSensitiveValue(ProbeOutput.FormatJsonElement(result));
        }

        if (!_credentialResultIndexes.TryGetValue(
                (int)success.Id,
                out IReadOnlyList<int>? credentialIndexes))
        {
            return;
        }

        foreach (int index in credentialIndexes)
        {
            if (index >= success.Results.Count)
            {
                continue;
            }

            System.Text.Json.JsonElement result = success.Results[index];
            _output.RegisterSensitiveValue(
                ProbeOutput.FormatJsonElement(result),
                replacement: "[REDACTED-SECRET]",
                alwaysRedact: true);
        }
    }
}

internal sealed class ProbeCommandRejectedException : Exception
{
    public ProbeCommandRejectedException(long requestId, int errorCode, string deviceMessage)
        : base(
            $"设备拒绝命令 {requestId.ToString(CultureInfo.InvariantCulture)}："
            + $"{errorCode.ToString(CultureInfo.InvariantCulture)} {deviceMessage}")
    {
        RequestId = requestId;
        ErrorCode = errorCode;
        DeviceMessage = deviceMessage;
    }

    public long RequestId { get; }

    public int ErrorCode { get; }

    public string DeviceMessage { get; }
}
