using System.Net;
using System.Net.Sockets;
using System.Text;
using LibraTray.Core.Networking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Probe.Tests.Networking;

[TestClass]
[DoNotParallelize]
public sealed class ProbeTcpSessionPrivacyTests
{
    private const string Secret = "fixture-token-that-must-never-reach-output";
    private static readonly SemaphoreSlim ConsoleCaptureLock = new(1, 1);

    [TestMethod]
    public async Task CredentialResultsNeverReachConsoleOrJsonl()
    {
        ProbeCapture[] captures =
        [
            await CaptureCredentialQueryAsync(
                $$"""{"id":1,"result":["{{Secret}}"],"echo":"{{Secret}}"}"""
                + "\r\n",
                "auth.token"),
            await CaptureCredentialQueryAsync(
                $$"""{"id":999,"result":["{{Secret}}"]}""" + "\r\n",
                "wifi_password_hash"),
            await CaptureCredentialQueryAsync(
                $$"""{"id":1,"result":["{{Secret}}"]""" + "\r\n"),
            await CaptureCredentialQueryAsync(
                "{\"id\":1,\"error\":{\"code\":-1,\"message\":"
                + "\"{\\\"auth.token\\\":\\\""
                + Secret
                + "\\\"}\"}}"
                + "\r\n"),
        ];

        Assert.IsTrue(captures[0].CommandSucceeded);
        Assert.IsFalse(captures[1].CommandSucceeded);
        Assert.IsFalse(captures[2].CommandSucceeded);
        Assert.IsFalse(captures[3].CommandSucceeded);

        foreach (ProbeCapture capture in captures)
        {
            Assert.IsFalse(
                capture.ConsoleOutput.Contains(Secret, StringComparison.Ordinal),
                "A credential result reached the probe console.");
            Assert.IsFalse(
                capture.Jsonl.Contains(Secret, StringComparison.Ordinal),
                "A credential result reached the probe JSONL log.");
            StringAssert.Contains(
                capture.ConsoleOutput + capture.Jsonl,
                "[REDACTED");
        }
    }

    [TestMethod]
    public async Task DefaultRawResponseRedactionHandlesEscapedSensitiveKeys()
    {
        const string SensitiveSsid = "fixture-raw-response-escaped-ssid";
        string frames =
            """{"method":"props","params":{"\u0073sid":"fixture-raw-response-escaped-ssid","power":"on"}}"""
            + "\r\n"
            + """{"id":1,"result":["on"]}"""
            + "\r\n";

        ProbeCapture capture = await CaptureCredentialQueryAsync(
            frames,
            credentialProperty: "power",
            redact: true);

        Assert.IsTrue(capture.CommandSucceeded);
        Assert.IsFalse(
            capture.Jsonl.Contains(SensitiveSsid, StringComparison.Ordinal),
            "An escaped SSID reached the probe JSONL log.");
        StringAssert.Contains(capture.Jsonl, "[REDACTED");
    }

    [TestMethod]
    public async Task MalformedEscapedCredentialWithoutPendingRequestIsAlwaysSuppressed()
    {
        const string EscapedSecret = "fixture-escaped-malformed-token-secret";
        string responseFrame =
            "{\"to\\u006ben\":\"fixture-escaped-malformed-token-secret\""
            + "\r\n";

        await ConsoleCaptureLock.WaitAsync();

        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"LibraTray-Probe-Malformed-{Guid.NewGuid():N}.jsonl");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        var consoleWriter = new StringWriter();
        TextWriter originalOut = Console.Out;
        using var fixtureCancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));

        try
        {
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task server = ServeUnsolicitedFrameAsync(
                listener,
                responseFrame,
                fixtureCancellation.Token);

            Console.SetOut(consoleWriter);
            await using (DiagnosticLogger logger = DiagnosticLogger.Create(
                logPath,
                redact: false))
            {
                var output = new ProbeOutput(logger);
                await using var session = new ProbeTcpSession(output);
                await session.ConnectAsync(
                    IPAddress.Loopback.ToString(),
                    port,
                    TimeSpan.FromSeconds(2),
                    fixtureCancellation.Token);
                await session.ListenAsync(
                    TimeSpan.FromMilliseconds(250),
                    fixtureCancellation.Token);
            }

            await server;
            string combined = consoleWriter
                + await File.ReadAllTextAsync(
                    logPath,
                    fixtureCancellation.Token);

            Assert.IsFalse(
                combined.Contains(EscapedSecret, StringComparison.Ordinal),
                "A credential from a malformed unsolicited frame reached output.");
            StringAssert.Contains(combined, "[REDACTED-MALFORMED-FRAME");
        }
        finally
        {
            Console.SetOut(originalOut);
            listener.Stop();
            File.Delete(logPath);
            ConsoleCaptureLock.Release();
        }
    }

    [TestMethod]
    public async Task DiscoveryDatagramsNeverExposeCredentialsInRawMode()
    {
        const string DatagramSecret = "fixture-truncated-discovery-token-secret";
        const string InvalidHeaderSecret = "fixture-invalid-header-token-secret";
        const string LocationSecret = "fixture-location-token-secret";
        const string AuthorizationSecret = "fixture-authorization-header-secret";
        const string EmbeddedNameSecret = "fixture-name-assignment-token-secret";
        const string BodySecret = "fixture-discovery-body-token-secret";
        const string StatusSecret = "fixture-status-line-token-secret";
        byte[][] responses =
        [
            Encoding.UTF8.GetBytes(
                "{\"token\":\"fixture-truncated-discovery-token-secret"),
            Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\n"
                + "Location: yeelight://127.0.0.1:55443\r\n"
                + "x token: fixture-invalid-header-token-secret\r\n"
                + "\r\n"),
            Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\n"
                + "Location: yeelight://token:fixture-location-token-secret"
                + "@127.0.0.1:55443\r\n"
                + "\r\n"),
            Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\n"
                + "Location: yeelight://127.0.0.1:55443\r\n"
                + "id: fixture-valid-credential-record\r\n"
                + "Authorization: Bearer fixture-authorization-header-secret\r\n"
                + "name: access_token=fixture-name-assignment-token-secret\r\n"
                + "\r\n"),
            Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\n"
                + "Location: yeelight://127.0.0.1:55443\r\n"
                + "id: fixture-duplicate-endpoint-record\r\n"
                + "\r\n"),
            Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\n"
                + "Location: yeelight://127.0.0.1:55443\r\n"
                + "id: fixture-body-credential-record\r\n"
                + "\r\n"
                + "access_token=fixture-discovery-body-token-secret"),
            Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 token: fixture-status-line-token-secret\r\n"
                + "Location: yeelight://127.0.0.1:55443\r\n"
                + "\r\n"),
        ];

        await ConsoleCaptureLock.WaitAsync();

        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"LibraTray-Probe-Discovery-{Guid.NewGuid():N}.jsonl");
        using var server = new UdpClient(
            new IPEndPoint(IPAddress.Loopback, 0));
        var consoleWriter = new StringWriter();
        TextWriter originalOut = Console.Out;
        using var fixtureCancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));

        try
        {
            int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
            Task responder = ServeDiscoveryDatagramsAsync(
                server,
                responses,
                fixtureCancellation.Token);

            Console.SetOut(consoleWriter);
            IReadOnlyList<DiscoveryRecord> records;
            await using (DiagnosticLogger logger = DiagnosticLogger.Create(
                logPath,
                redact: false))
            {
                var output = new ProbeOutput(logger);
                records = await ProbeDiscovery.DiscoverAsync(
                    IPAddress.Loopback.ToString(),
                    port,
                    TimeSpan.FromMilliseconds(250),
                    output,
                    fixtureCancellation.Token);
            }

            await responder;
            string combined = consoleWriter
                + await File.ReadAllTextAsync(
                    logPath,
                    fixtureCancellation.Token);

            Assert.HasCount(1, records);
            Assert.IsFalse(
                combined.Contains(DatagramSecret, StringComparison.Ordinal),
                "A credential from a malformed discovery datagram reached output.");
            Assert.IsFalse(
                combined.Contains(InvalidHeaderSecret, StringComparison.Ordinal),
                "A credential from an invalid discovery header reached output.");
            Assert.IsFalse(
                combined.Contains(LocationSecret, StringComparison.Ordinal),
                "A credential from discovery Location metadata reached output.");
            Assert.IsFalse(
                combined.Contains(AuthorizationSecret, StringComparison.Ordinal),
                "A discovery Authorization header reached output.");
            Assert.IsFalse(
                combined.Contains(EmbeddedNameSecret, StringComparison.Ordinal),
                "A credential embedded in a discovery name reached output.");
            Assert.IsFalse(
                combined.Contains(BodySecret, StringComparison.Ordinal),
                "A credential in a discovery body reached output.");
            Assert.IsFalse(
                combined.Contains(StatusSecret, StringComparison.Ordinal),
                "A credential in an invalid discovery status line reached output.");
            StringAssert.Contains(combined, "[REDACTED-MALFORMED-DATAGRAM");
        }
        finally
        {
            Console.SetOut(originalOut);
            File.Delete(logPath);
            ConsoleCaptureLock.Release();
        }
    }

    private static async Task<ProbeCapture> CaptureCredentialQueryAsync(
        string responseFrame,
        string credentialProperty = "access_token",
        bool redact = false)
    {
        await ConsoleCaptureLock.WaitAsync();

        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"LibraTray-Probe-Credential-{Guid.NewGuid():N}.jsonl");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        var consoleWriter = new StringWriter();
        TextWriter originalOut = Console.Out;
        using var fixtureCancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));

        try
        {
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task server = ServeSingleResponseAsync(
                listener,
                responseFrame,
                fixtureCancellation.Token);

            Console.SetOut(consoleWriter);
            bool commandSucceeded;
            await using (DiagnosticLogger logger = DiagnosticLogger.Create(
                logPath,
                redact))
            {
                var output = new ProbeOutput(logger);
                await using var session = new ProbeTcpSession(output);
                await session.ConnectAsync(
                    IPAddress.Loopback.ToString(),
                    port,
                    TimeSpan.FromSeconds(2),
                    fixtureCancellation.Token);

                try
                {
                    await session.SendCommandAsync(
                        "get_prop",
                        [credentialProperty],
                        TimeSpan.FromSeconds(2),
                        fixtureCancellation.Token);
                    commandSucceeded = true;
                }
                catch (Exception exception) when (
                    exception is TimeoutException
                        or YeelightDisconnectedException
                        or ProbeCommandRejectedException)
                {
                    commandSucceeded = false;
                }
            }

            await server;
            string jsonl = await File.ReadAllTextAsync(
                logPath,
                fixtureCancellation.Token);
            return new ProbeCapture(
                consoleWriter.ToString(),
                jsonl,
                commandSucceeded);
        }
        finally
        {
            Console.SetOut(originalOut);
            listener.Stop();
            File.Delete(logPath);
            ConsoleCaptureLock.Release();
        }
    }

    private static async Task ServeSingleResponseAsync(
        TcpListener listener,
        string responseFrame,
        CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(
            cancellationToken);
        NetworkStream stream = client.GetStream();
        byte[] requestBuffer = new byte[4 * 1024];
        int bytesRead = await stream.ReadAsync(
            requestBuffer,
            cancellationToken);
        Assert.IsGreaterThan(0, bytesRead);

        byte[] response = Encoding.UTF8.GetBytes(responseFrame);
        await stream.WriteAsync(response, cancellationToken);
    }

    private static async Task ServeUnsolicitedFrameAsync(
        TcpListener listener,
        string responseFrame,
        CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(
            cancellationToken);
        NetworkStream stream = client.GetStream();
        byte[] response = Encoding.UTF8.GetBytes(responseFrame);
        await stream.WriteAsync(response, cancellationToken);
        await Task.Delay(500, cancellationToken);
    }

    private static async Task ServeDiscoveryDatagramsAsync(
        UdpClient server,
        IReadOnlyList<byte[]> responses,
        CancellationToken cancellationToken)
    {
        UdpReceiveResult request = await server.ReceiveAsync(cancellationToken);
        foreach (byte[] response in responses)
        {
            await server.SendAsync(
                response,
                request.RemoteEndPoint,
                cancellationToken);
        }
    }

    private sealed record ProbeCapture(
        string ConsoleOutput,
        string Jsonl,
        bool CommandSucceeded);
}
