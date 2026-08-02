using System.Diagnostics;
using System.Net;
using System.Text;
using LibraTray.Core.Devices.LibraPro;
using LibraTray.Core.Networking;
using LibraTray.Core.Protocol;
using LibraTray.Core.Tests.Networking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Devices.LibraPro;

[TestClass]
public sealed class LibraProDeviceSessionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task ConnectAndVerifiedCommandPublishCompleteState()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var session = CreateSession();
        YeelightDiscoveredDevice device = CreateDevice(server.EndPoint);

        Task connectTask = session.ConnectAsync(device, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);
        ReceivedRequest initialQuery =
            await connection.ReadRequestAsync(testCancellation.Token);
        Assert.AreEqual("get_prop", initialQuery.Method);
        await WriteStateAsync(
            connection,
            initialQuery.Id,
            mainBrightness: 50,
            testCancellation.Token);
        await connectTask;

        Assert.AreEqual(LibraProSessionStatus.Connected, session.Status);
        Assert.AreEqual("Yeelight Libra Pro", session.Identity?.DisplayName);
        Assert.AreEqual("0x0000000000000001", session.ConnectionInfo?.DeviceId);
        Assert.AreEqual("38", session.ConnectionInfo?.FirmwareVersion);
        Assert.AreEqual(server.EndPoint, session.ConnectionInfo?.ControlEndPoint);
        CollectionAssert.Contains(
            session.ConnectionInfo?.Capabilities.ToArray(),
            "bg_set_scene");
        Assert.AreEqual(50, session.CurrentState?.MainBrightness);

        Task<LibraProState> command =
            session.SetMainBrightnessAsync(42, testCancellation.Token);
        ReceivedRequest write =
            await connection.ReadRequestAsync(testCancellation.Token);
        Assert.AreEqual("set_bright", write.Method);
        await connection.WriteTextAsync(
            $$"""{"id":{{write.Id}},"result":["ok"]}""" + "\r\n",
            testCancellation.Token);
        ReceivedRequest verification =
            await connection.ReadRequestAsync(testCancellation.Token);
        Assert.AreEqual("get_prop", verification.Method);
        await WriteStateAsync(
            connection,
            verification.Id,
            mainBrightness: 42,
            testCancellation.Token);

        LibraProState state = await command;
        Assert.AreEqual(42, state.MainBrightness);
        Assert.AreEqual(42, session.CurrentState?.MainBrightness);
    }

    [TestMethod]
    public async Task ConnectRejectsUnknownDiscoveryIdentityBeforeTcp()
    {
        await using var session = CreateSession();
        YeelightDiscoveredDevice device = CreateDevice(
            new IPEndPoint(IPAddress.Loopback, 55_443),
            model: "stripa");

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => session.ConnectAsync(device));
        Assert.AreEqual(LibraProSessionStatus.Faulted, session.Status);
    }

    [TestMethod]
    public async Task TransientNotificationQueryFailureRetriesAndKeepsConnectionUsable()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var session = CreateSession();
        YeelightDiscoveredDevice device = CreateDevice(server.EndPoint);

        Task connectTask = session.ConnectAsync(device, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);
        ReceivedRequest initialQuery =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteStateAsync(
            connection,
            initialQuery.Id,
            mainBrightness: 50,
            testCancellation.Token);
        await connectTask;

        var retryObserved =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReconciledState =
            new TaskCompletionSource<LibraProState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        session.StatusChanged += (_, eventArgs) =>
        {
            if (eventArgs.Status == LibraProSessionStatus.Retrying
                && eventArgs.RetryAttempt == 1)
            {
                retryObserved.TrySetResult();
            }
        };
        session.StateChanged += (_, eventArgs) =>
        {
            if (eventArgs.State.MainBrightness == 60)
            {
                firstReconciledState.TrySetResult(eventArgs.State);
            }
        };

        await connection.WriteTextAsync(
            """{"method":"props","params":{"bright":60}}""" + "\r\n",
            testCancellation.Token);
        ReceivedRequest failedQuery =
            await connection.ReadRequestAsync(testCancellation.Token);
        await connection.WriteTextAsync(
            $"{{\"id\":{failedQuery.Id},"
            + "\"error\":{\"code\":-1,\"message\":\"busy\"}}\r\n",
            testCancellation.Token);
        await retryObserved.Task.WaitAsync(testCancellation.Token);
        ReceivedRequest retriedQuery =
            await connection.ReadRequestAsync(testCancellation.Token);
        Assert.AreEqual("get_prop", retriedQuery.Method);
        await WriteStateAsync(
            connection,
            retriedQuery.Id,
            mainBrightness: 60,
            testCancellation.Token);
        await firstReconciledState.Task.WaitAsync(testCancellation.Token);

        Assert.AreEqual(LibraProSessionStatus.Connected, session.Status);

        var reconciledState =
            new TaskCompletionSource<LibraProState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += (_, eventArgs) =>
        {
            if (eventArgs.State.MainBrightness == 61)
            {
                reconciledState.TrySetResult(eventArgs.State);
            }
        };
        await connection.WriteTextAsync(
            """{"method":"props","params":{"bright":61}}""" + "\r\n",
            testCancellation.Token);
        ReceivedRequest successfulQuery =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteStateAsync(
            connection,
            successfulQuery.Id,
            mainBrightness: 61,
            testCancellation.Token);

        LibraProState state =
            await reconciledState.Task.WaitAsync(testCancellation.Token);
        Assert.AreEqual(61, state.MainBrightness);
        Assert.AreEqual(LibraProSessionStatus.Connected, session.Status);
    }

    [TestMethod]
    public async Task CommandSynchronizationFailureResendsSameOperation()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var session = CreateSession();
        YeelightDiscoveredDevice device = CreateDevice(server.EndPoint);
        Task connectTask = session.ConnectAsync(device, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);
        ReceivedRequest initialQuery =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteStateAsync(
            connection,
            initialQuery.Id,
            mainBrightness: 50,
            testCancellation.Token);
        await connectTask;

        Task<LibraProState> command =
            session.SetMainBrightnessAsync(42, testCancellation.Token);
        ReceivedRequest firstWrite =
            await connection.ReadRequestAsync(testCancellation.Token);
        Assert.AreEqual("set_bright", firstWrite.Method);
        await WriteOkAsync(connection, firstWrite.Id, testCancellation.Token);
        ReceivedRequest failedVerification =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteBusyAsync(
            connection,
            failedVerification.Id,
            testCancellation.Token);

        ReceivedRequest retriedWrite =
            await connection.ReadRequestAsync(testCancellation.Token);
        Assert.AreEqual("set_bright", retriedWrite.Method);
        await WriteOkAsync(connection, retriedWrite.Id, testCancellation.Token);
        ReceivedRequest successfulVerification =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteStateAsync(
            connection,
            successfulVerification.Id,
            mainBrightness: 42,
            testCancellation.Token);

        LibraProState state = await command;
        Assert.AreEqual(42, state.MainBrightness);
        Assert.AreEqual(LibraProSessionStatus.Connected, session.Status);
    }

    [TestMethod]
    public async Task CommandStateMismatchResendsAndResetsRetryStatus()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var session = CreateSession();
        var observedStatuses = new List<LibraProSessionStatus>();
        session.StatusChanged += (_, eventArgs) =>
            observedStatuses.Add(eventArgs.Status);
        YeelightDiscoveredDevice device = CreateDevice(server.EndPoint);
        Task connectTask = session.ConnectAsync(device, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);
        ReceivedRequest initialQuery =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteStateAsync(
            connection,
            initialQuery.Id,
            mainBrightness: 50,
            testCancellation.Token);
        await connectTask;

        Task<LibraProState> command =
            session.SetMainBrightnessAsync(42, testCancellation.Token);
        ReceivedRequest firstWrite =
            await connection.ReadRequestAsync(testCancellation.Token);
        Assert.AreEqual("set_bright", firstWrite.Method);
        await WriteOkAsync(connection, firstWrite.Id, testCancellation.Token);
        ReceivedRequest mismatchedVerification =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteStateAsync(
            connection,
            mismatchedVerification.Id,
            mainBrightness: 50,
            testCancellation.Token);

        ReceivedRequest retriedWrite =
            await connection.ReadRequestAsync(testCancellation.Token);
        Assert.AreEqual("set_bright", retriedWrite.Method);
        await WriteOkAsync(connection, retriedWrite.Id, testCancellation.Token);
        ReceivedRequest successfulVerification =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteStateAsync(
            connection,
            successfulVerification.Id,
            mainBrightness: 42,
            testCancellation.Token);

        LibraProState state = await command;
        Assert.AreEqual(42, state.MainBrightness);
        Assert.AreEqual(42, session.CurrentState?.MainBrightness);
        CollectionAssert.Contains(
            observedStatuses,
            LibraProSessionStatus.Retrying);
        Assert.AreEqual(LibraProSessionStatus.Connected, session.Status);
    }

    [TestMethod]
    public async Task CommandReportsFailureOnlyAfterRetryBudgetIsExhausted()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var session = CreateSession();
        YeelightDiscoveredDevice device = CreateDevice(server.EndPoint);
        Task connectTask = session.ConnectAsync(device, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);
        ReceivedRequest initialQuery =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteStateAsync(
            connection,
            initialQuery.Id,
            mainBrightness: 50,
            testCancellation.Token);
        await connectTask;

        Task<LibraProState> command =
            session.SetMainBrightnessAsync(42, testCancellation.Token);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ReceivedRequest write =
                await connection.ReadRequestAsync(testCancellation.Token);
            Assert.AreEqual("set_bright", write.Method);
            await WriteOkAsync(connection, write.Id, testCancellation.Token);
            ReceivedRequest verification =
                await connection.ReadRequestAsync(testCancellation.Token);
            await WriteBusyAsync(
                connection,
                verification.Id,
                testCancellation.Token);
        }

        await Assert.ThrowsExactlyAsync<YeelightCommandException>(
            () => command);
        Assert.AreEqual(LibraProSessionStatus.Connected, session.Status);
    }

    [TestMethod]
    public async Task SessionSpacesEveryRequestBelowPublishedConnectionQuota()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var session = new LibraProDeviceSession(
            new LibraProDeviceSessionOptions
            {
                MinimumCommandInterval = TimeSpan.FromMilliseconds(120),
            });
        YeelightDiscoveredDevice device = CreateDevice(server.EndPoint);
        Task connectTask = session.ConnectAsync(device, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);
        ReceivedRequest initialQuery =
            await connection.ReadRequestAsync(testCancellation.Token);
        long initialReceived = Stopwatch.GetTimestamp();
        await WriteStateAsync(
            connection,
            initialQuery.Id,
            mainBrightness: 50,
            testCancellation.Token);
        await connectTask;

        Task<LibraProState> command =
            session.SetMainBrightnessAsync(42, testCancellation.Token);
        ReceivedRequest write =
            await connection.ReadRequestAsync(testCancellation.Token);
        long writeReceived = Stopwatch.GetTimestamp();
        Assert.IsGreaterThanOrEqualTo(
            TimeSpan.FromMilliseconds(80),
            Stopwatch.GetElapsedTime(initialReceived, writeReceived));
        await WriteOkAsync(connection, write.Id, testCancellation.Token);
        ReceivedRequest verification =
            await connection.ReadRequestAsync(testCancellation.Token);
        long verificationReceived = Stopwatch.GetTimestamp();
        Assert.IsGreaterThanOrEqualTo(
            TimeSpan.FromMilliseconds(80),
            Stopwatch.GetElapsedTime(writeReceived, verificationReceived));
        await WriteStateAsync(
            connection,
            verification.Id,
            mainBrightness: 42,
            testCancellation.Token);
        _ = await command;
    }

    [TestMethod]
    public async Task SessionDefersRequestsThatReachRollingWindowQuota()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var session = new LibraProDeviceSession(
            new LibraProDeviceSessionOptions
            {
                MinimumCommandInterval = TimeSpan.Zero,
                MaximumCommandsPerWindow = 3,
                CommandQuotaWindow = TimeSpan.FromMilliseconds(300),
            });
        YeelightDiscoveredDevice device = CreateDevice(server.EndPoint);
        Task connectTask = session.ConnectAsync(device, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);
        ReceivedRequest initialQuery =
            await connection.ReadRequestAsync(testCancellation.Token);
        long initialReceived = Stopwatch.GetTimestamp();
        await WriteStateAsync(
            connection,
            initialQuery.Id,
            mainBrightness: 50,
            testCancellation.Token);
        await connectTask;

        Task<LibraProState> firstCommand =
            session.SetMainBrightnessAsync(42, testCancellation.Token);
        ReceivedRequest firstWrite =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteOkAsync(connection, firstWrite.Id, testCancellation.Token);
        ReceivedRequest firstVerification =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteStateAsync(
            connection,
            firstVerification.Id,
            mainBrightness: 42,
            testCancellation.Token);
        _ = await firstCommand;

        Task<LibraProState> secondCommand =
            session.SetMainBrightnessAsync(43, testCancellation.Token);
        ReceivedRequest secondWrite =
            await connection.ReadRequestAsync(testCancellation.Token);
        Assert.IsGreaterThanOrEqualTo(
            TimeSpan.FromMilliseconds(200),
            Stopwatch.GetElapsedTime(initialReceived));
        await WriteOkAsync(connection, secondWrite.Id, testCancellation.Token);
        ReceivedRequest secondVerification =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteStateAsync(
            connection,
            secondVerification.Id,
            mainBrightness: 43,
            testCancellation.Token);
        _ = await secondCommand;
    }

    [TestMethod]
    public async Task DisposeCancelsAndWaitsForAnInFlightCommand()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        var session = CreateSession();
        YeelightDiscoveredDevice device = CreateDevice(server.EndPoint);
        Task connectTask = session.ConnectAsync(device, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);
        ReceivedRequest initialQuery =
            await connection.ReadRequestAsync(testCancellation.Token);
        await WriteStateAsync(
            connection,
            initialQuery.Id,
            mainBrightness: 50,
            testCancellation.Token);
        await connectTask;

        Task<LibraProState> command = session.SetMainBrightnessAsync(42);
        ReceivedRequest write =
            await connection.ReadRequestAsync(testCancellation.Token);
        Assert.AreEqual("set_bright", write.Method);

        Task dispose = session.DisposeAsync().AsTask();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => command);
        await dispose.WaitAsync(testCancellation.Token);
        Assert.AreEqual(LibraProSessionStatus.Disconnected, session.Status);
    }

    private static YeelightDiscoveredDevice CreateDevice(
        IPEndPoint endpoint,
        string model = "lamp15")
    {
        string response =
            "HTTP/1.1 200 OK\r\n"
            + $"Location: yeelight://{endpoint.Address}:{endpoint.Port}\r\n"
            + "id: 0x0000000000000001\r\n"
            + $"model: {model}\r\n"
            + "fw_ver: 38\r\n"
            + "support: get_prop set_power set_bright set_ct_abx "
            + "bg_set_power bg_set_bright bg_set_ct_abx bg_set_rgb bg_set_scene\r\n"
            + "\r\n";
        YeelightDiscoveryResponse parsed = YeelightDiscovery.ParseResponse(
            Encoding.UTF8.GetBytes(response));
        return new YeelightDiscoveredDevice(
            new IPEndPoint(endpoint.Address, YeelightDiscovery.MulticastPort),
            parsed);
    }

    private static LibraProDeviceSession CreateSession() =>
        new(
            new LibraProDeviceSessionOptions
            {
                MinimumCommandInterval = TimeSpan.Zero,
            });

    private static Task WriteStateAsync(
        LoopbackYeelightConnection connection,
        int requestId,
        int mainBrightness,
        CancellationToken cancellationToken)
    {
        string response =
            $$"""{"id":{{requestId}},"result":["off","off","off","{{mainBrightness}}","4000","50","4000","13395711","359","100","1"]}"""
            + "\r\n";
        return connection.WriteTextAsync(response, cancellationToken);
    }

    private static Task WriteOkAsync(
        LoopbackYeelightConnection connection,
        int requestId,
        CancellationToken cancellationToken) =>
        connection.WriteTextAsync(
            $$"""{"id":{{requestId}},"result":["ok"]}""" + "\r\n",
            cancellationToken);

    private static Task WriteBusyAsync(
        LoopbackYeelightConnection connection,
        int requestId,
        CancellationToken cancellationToken) =>
        connection.WriteTextAsync(
            $"{{\"id\":{requestId},"
            + "\"error\":{\"code\":-1,\"message\":\"busy\"}}\r\n",
            cancellationToken);
}
