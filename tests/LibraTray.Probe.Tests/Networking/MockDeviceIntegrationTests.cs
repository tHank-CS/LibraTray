using System.Net;
using System.Net.Sockets;
using LibraTray.Core.Devices.LibraPro;
using LibraTray.Core.Networking;
using LibraTray.Core.Protocol;
using LibraTray.MockDevice;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Probe.Tests.Networking;

[TestClass]
public sealed class MockDeviceIntegrationTests
{
    [TestMethod]
    [DataRow("normal")]
    [DataRow("delay")]
    [DataRow("split")]
    [DataRow("coalesce")]
    [DataRow("props")]
    [DataRow("bad-props")]
    public async Task ReadModesReturnACompleteMatchedResponse(string mode)
    {
        await using MockServerFixture fixture = MockServerFixture.Start(mode);

        YeelightSuccessResponse response = await SendGetPowerAsync(
            fixture.TcpPort,
            TimeSpan.FromSeconds(2));

        Assert.HasCount(1, response.Results);
        Assert.AreEqual("on", response.Results[0].GetString());
    }

    [TestMethod]
    public async Task ErrorNoResponseAndDisconnectModesSurfaceExpectedFailures()
    {
        await using (MockServerFixture fixture = MockServerFixture.Start("error"))
        {
            await Assert.ThrowsExactlyAsync<ProbeCommandRejectedException>(
                () => SendGetPowerAsync(
                    fixture.TcpPort,
                    TimeSpan.FromSeconds(1)));
        }

        await using (MockServerFixture fixture = MockServerFixture.Start("no-response"))
        {
            await Assert.ThrowsExactlyAsync<TimeoutException>(
                () => SendGetPowerAsync(
                    fixture.TcpPort,
                    TimeSpan.FromMilliseconds(150)));
        }

        await using (MockServerFixture fixture = MockServerFixture.Start("disconnect"))
        {
            await Assert.ThrowsExactlyAsync<YeelightDisconnectedException>(
                () => SendGetPowerAsync(
                    fixture.TcpPort,
                    TimeSpan.FromSeconds(1)));
        }
    }

    [TestMethod]
    public async Task RestartModeDisconnectsOnceThenAcceptsAReconnect()
    {
        await using MockServerFixture fixture = MockServerFixture.Start("restart");

        await Assert.ThrowsExactlyAsync<YeelightDisconnectedException>(
            () => SendGetPowerAsync(
                fixture.TcpPort,
                TimeSpan.FromSeconds(1)));

        YeelightSuccessResponse response = await SendGetPowerAfterRestartAsync(
            fixture.TcpPort);

        Assert.AreEqual("on", response.Results[0].GetString());
    }

    [TestMethod]
    public async Task OversizedClientFrameDisconnectsOnlyThatClient()
    {
        await using MockServerFixture fixture = MockServerFixture.Start("normal");
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, fixture.TcpPort);
            NetworkStream stream = client.GetStream();
            byte[] oversized = new byte[CrlfMessageFramer.DefaultMaximumFrameBytes + 1];
            Array.Fill(oversized, (byte)'x');
            await stream.WriteAsync(oversized);

            byte[] response = new byte[1];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            int bytesRead = await stream.ReadAsync(response, timeout.Token);
            Assert.AreEqual(0, bytesRead);
        }

        YeelightSuccessResponse validResponse = await SendGetPowerAsync(
            fixture.TcpPort,
            TimeSpan.FromSeconds(1));
        Assert.AreEqual("on", validResponse.Results[0].GetString());
    }

    [TestMethod]
    public async Task ConfirmedSafeWriteTraversesDiscoveryPreReadWriteAndPostRead()
    {
        await using MockServerFixture fixture = MockServerFixture.Start("normal");
        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"LibraTray-Probe-SafeWrite-{Guid.NewGuid():N}.jsonl");

        try
        {
            int exitCode = await ProbeApplication.RunAsync(
            [
                "safe-write",
                "--host",
                IPAddress.Loopback.ToString(),
                "--port",
                fixture.TcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--target",
                IPAddress.Loopback.ToString(),
                "--local-address",
                IPAddress.Loopback.ToString(),
                "--discovery-port",
                fixture.DiscoveryPort.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                "--method",
                "set_bright",
                "--value",
                "42",
                "--confirm-write",
                "--timeout-seconds",
                "2",
                "--log-path",
                logPath,
            ]);

            Assert.AreEqual(0, exitCode);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public async Task ConfirmedLamp15MainPowerWritePreservesBackgroundChannel()
    {
        await using MockServerFixture fixture = MockServerFixture.Start("normal");
        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"LibraTray-Probe-Lamp15-Power-{Guid.NewGuid():N}.jsonl");

        try
        {
            int exitCode = await ProbeApplication.RunAsync(
            [
                "safe-write",
                "--host",
                IPAddress.Loopback.ToString(),
                "--port",
                fixture.TcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--target",
                IPAddress.Loopback.ToString(),
                "--local-address",
                IPAddress.Loopback.ToString(),
                "--discovery-port",
                fixture.DiscoveryPort.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                "--method",
                "set_power",
                "--value",
                "off",
                "--confirm-write",
                "--timeout-seconds",
                "2",
                "--log-path",
                logPath,
            ]);

            Assert.AreEqual(0, exitCode);
            YeelightSuccessResponse state = await SendGetPropertiesAsync(
                fixture.TcpPort,
                ["power", "main_power", "bg_power"],
                TimeSpan.FromSeconds(1));
            Assert.AreEqual("on", state.Results[0].GetString());
            Assert.AreEqual("off", state.Results[1].GetString());
            Assert.AreEqual("on", state.Results[2].GetString());
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public async Task ConfirmedLamp15BackgroundColorSceneRestoresReadableState()
    {
        await using MockServerFixture fixture = MockServerFixture.Start("normal");
        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"LibraTray-Probe-Lamp15-BackgroundScene-{Guid.NewGuid():N}.jsonl");

        try
        {
            int exitCode = await ProbeApplication.RunAsync(
            [
                "safe-write",
                "--host",
                IPAddress.Loopback.ToString(),
                "--port",
                fixture.TcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--target",
                IPAddress.Loopback.ToString(),
                "--local-address",
                IPAddress.Loopback.ToString(),
                "--discovery-port",
                fixture.DiscoveryPort.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                "--method",
                "bg_set_scene",
                "--value",
                "13395711,50",
                "--confirm-write",
                "--timeout-seconds",
                "2",
                "--log-path",
                logPath,
            ]);

            Assert.AreEqual(0, exitCode);
            YeelightSuccessResponse state = await SendGetPropertiesAsync(
                fixture.TcpPort,
                [
                    "power",
                    "main_power",
                    "bg_power",
                    "bright",
                    "ct",
                    "bg_bright",
                    "bg_rgb",
                    "bg_lmode",
                ],
                TimeSpan.FromSeconds(1));
            Assert.AreEqual("on", state.Results[0].GetString());
            Assert.AreEqual("on", state.Results[1].GetString());
            Assert.AreEqual("on", state.Results[2].GetString());
            Assert.AreEqual("50", state.Results[3].GetString());
            Assert.AreEqual("4000", state.Results[4].GetString());
            Assert.AreEqual("50", state.Results[5].GetString());
            Assert.AreEqual("13395711", state.Results[6].GetString());
            Assert.AreEqual("1", state.Results[7].GetString());
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [TestMethod]
    public async Task AdapterRecoversColdStartSilentBackgroundOverRealTcp()
    {
        await using MockServerFixture fixture =
            MockServerFixture.Start("cold-start-silent");
        await using var client = new YeelightClient(TimeSpan.FromSeconds(2));
        await client.ConnectAsync(
            IPAddress.Loopback.ToString(),
            fixture.TcpPort);
        var transport = new YeelightCommandTransport(client);
        using var adapter = new LibraProAdapter(
            transport,
            "lamp15",
            MockState.SupportedMethods.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries));
        adapter.BeginConnectionEpoch(1);

        LibraProCommandResult result = await adapter.SetBackgroundPowerAsync(
            enabled: true,
            new LibraProBackgroundSnapshot(50, 13_395_711),
            TimeSpan.FromSeconds(2));

        Assert.IsTrue(result.RecoveryAttempted);
        Assert.IsFalse(result.State.MainPower);
        Assert.IsTrue(result.State.BackgroundPower);
        Assert.AreEqual(50, result.State.BackgroundBrightness);
        Assert.AreEqual(13_395_711, result.State.BackgroundRgb);
    }

    private static Task<YeelightSuccessResponse> SendGetPowerAsync(
        int port,
        TimeSpan timeout) =>
        SendGetPropertiesAsync(port, ["power"], timeout);

    private static async Task<YeelightSuccessResponse> SendGetPropertiesAsync(
        int port,
        IReadOnlyList<object?> properties,
        TimeSpan timeout)
    {
        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"LibraTray-Probe-MockIntegration-{Guid.NewGuid():N}.jsonl");

        try
        {
            await using DiagnosticLogger logger = DiagnosticLogger.Create(
                logPath,
                redact: true);
            var output = new ProbeOutput(logger);
            await using var session = new ProbeTcpSession(output);
            await session.ConnectAsync(
                IPAddress.Loopback.ToString(),
                port,
                timeout,
                CancellationToken.None);
            return await session.SendCommandAsync(
                "get_prop",
                properties,
                timeout,
                CancellationToken.None);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    private static async Task<YeelightSuccessResponse> SendGetPowerAfterRestartAsync(
        int port)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        Exception? lastFailure = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                return await SendGetPowerAsync(port, TimeSpan.FromSeconds(1));
            }
            catch (Exception exception) when (
                exception is SocketException or YeelightDisconnectedException)
            {
                lastFailure = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }

        throw new AssertFailedException(
            $"Mock restart endpoint did not recover before the deadline: {lastFailure}");
    }

    private sealed class MockServerFixture : IAsyncDisposable
    {
        private static readonly Lock PortAllocationLock = new();

        private readonly CancellationTokenSource _cancellation;
        private readonly Task _serverTask;

        private MockServerFixture(
            int tcpPort,
            int discoveryPort,
            CancellationTokenSource cancellation,
            Task serverTask)
        {
            TcpPort = tcpPort;
            DiscoveryPort = discoveryPort;
            _cancellation = cancellation;
            _serverTask = serverTask;
        }

        public int TcpPort { get; }

        public int DiscoveryPort { get; }

        public static MockServerFixture Start(string mode)
        {
            lock (PortAllocationLock)
            {
                int tcpPort = GetUnusedTcpPort();
                int discoveryPort = GetUnusedUdpPort();
                MockOptions options = MockOptions.Parse(
                [
                    "--mode",
                    mode,
                    "--tcp-port",
                    tcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--discovery-port",
                    discoveryPort.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    "--delay-ms",
                    "25",
                ]);
                var cancellation = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));
                var server = new MockDeviceServer(options);
                Task serverTask = server.RunAsync(cancellation.Token);
                if (serverTask.IsFaulted)
                {
                    cancellation.Dispose();
                    serverTask.GetAwaiter().GetResult();
                }

                return new MockServerFixture(
                    tcpPort,
                    discoveryPort,
                    cancellation,
                    serverTask);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _cancellation.CancelAsync();
                await _serverTask;
            }
            finally
            {
                _cancellation.Dispose();
            }
        }

        private static int GetUnusedTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static int GetUnusedUdpPort()
        {
            using var client = new UdpClient(
                new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
        }
    }
}
