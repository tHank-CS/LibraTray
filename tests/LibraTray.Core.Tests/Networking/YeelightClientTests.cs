using System.Collections.Concurrent;
using System.Text;
using LibraTray.Core.Networking;
using LibraTray.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Networking;

[TestClass]
public sealed class YeelightClientTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task SendCommandAsyncCorrelatesOutOfOrderResponsesAroundNotification()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var client = new YeelightClient();

        var notificationCompletion =
            new TaskCompletionSource<YeelightPropsNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        client.NotificationReceived += (_, eventArgs) =>
        {
            if (eventArgs.Notification is YeelightPropsNotification props)
            {
                notificationCompletion.TrySetResult(props);
            }
        };

        await client.ConnectAsync(server.EndPoint, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);

        Task<YeelightSuccessResponse> firstCommand = client.SendCommandAsync(
            "first_command",
            timeout: TestTimeout,
            cancellationToken: testCancellation.Token);
        Task<YeelightSuccessResponse> secondCommand = client.SendCommandAsync(
            "second_command",
            timeout: TestTimeout,
            cancellationToken: testCancellation.Token);

        ReceivedRequest firstRequest =
            await connection.ReadRequestAsync(testCancellation.Token);
        ReceivedRequest secondRequest =
            await connection.ReadRequestAsync(testCancellation.Token);

        Assert.AreEqual("first_command", firstRequest.Method);
        Assert.AreEqual("second_command", secondRequest.Method);

        string stickyFrames =
            """{"method":"props","params":{"power":"on","future":1}}"""
            + "\r\n"
            + $$"""{"id":{{secondRequest.Id}},"result":["second"],"future":true}"""
            + "\r\n";

        await connection.WriteTextAsync(
            stickyFrames,
            testCancellation.Token);

        byte[] splitResponse = Encoding.UTF8.GetBytes(
            $$"""{"id":{{firstRequest.Id}},"result":["first"]}""" + "\r\n");
        await connection.WriteBytesAsync(
            splitResponse.AsMemory(0, 7),
            testCancellation.Token);
        await connection.WriteBytesAsync(
            splitResponse.AsMemory(7),
            testCancellation.Token);

        YeelightSuccessResponse second = await secondCommand;
        YeelightSuccessResponse first = await firstCommand;
        YeelightPropsNotification notification =
            await notificationCompletion.Task.WaitAsync(testCancellation.Token);

        Assert.AreEqual(firstRequest.Id, first.Id);
        Assert.AreEqual("first", first.Results[0].GetString());
        Assert.AreEqual(secondRequest.Id, second.Id);
        Assert.AreEqual("second", second.Results[0].GetString());
        Assert.AreEqual("on", notification.Properties["power"].GetString());
        Assert.IsTrue(client.IsConnected);
    }

    [TestMethod]
    public async Task SendCommandAsyncErrorResponseThrowsTypedCommandException()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var client = new YeelightClient();

        await client.ConnectAsync(server.EndPoint, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);

        Task<YeelightSuccessResponse> command = client.SendCommandAsync(
            "failing_command",
            timeout: TestTimeout,
            cancellationToken: testCancellation.Token);
        ReceivedRequest request =
            await connection.ReadRequestAsync(testCancellation.Token);

        await connection.WriteTextAsync(
            $$$"""{"id":{{{request.Id}}},"error":{"code":-1,"message":"rejected","future":true}}""" + "\r\n",
            testCancellation.Token);

        YeelightCommandException exception =
            await Assert.ThrowsExactlyAsync<YeelightCommandException>(
                async () => await command);

        Assert.AreEqual(request.Id, exception.RequestId);
        Assert.AreEqual(-1, exception.ErrorCode);
        Assert.AreEqual("rejected", exception.DeviceMessage);
    }

    [TestMethod]
    public async Task SendCommandAsyncTimesOutAndLateResponseIsObservable()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var client = new YeelightClient();

        var unmatchedCompletion =
            new TaskCompletionSource<YeelightResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        client.UnmatchedResponseReceived += (_, eventArgs) =>
            unmatchedCompletion.TrySetResult(eventArgs.Response);

        await client.ConnectAsync(server.EndPoint, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);

        Task<YeelightSuccessResponse> command = client.SendCommandAsync(
            "silent_command",
            timeout: TimeSpan.FromMilliseconds(200),
            cancellationToken: testCancellation.Token);
        ReceivedRequest request =
            await connection.ReadRequestAsync(testCancellation.Token);

        await Assert.ThrowsExactlyAsync<TimeoutException>(
            async () => await command);

        await connection.WriteTextAsync(
            $$"""{"id":{{request.Id}},"result":["late"]}""" + "\r\n",
            testCancellation.Token);

        YeelightResponse unmatched =
            await unmatchedCompletion.Task.WaitAsync(testCancellation.Token);
        Assert.AreEqual(request.Id, unmatched.Id);
        Assert.IsTrue(client.IsConnected);
    }

    [TestMethod]
    public async Task SendCommandAsyncCallerCancellationCancelsOnlyThatRequest()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        using var commandCancellation = new CancellationTokenSource();
        await using var server = new LoopbackYeelightServer();
        await using var client = new YeelightClient();

        await client.ConnectAsync(server.EndPoint, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);

        Task<YeelightSuccessResponse> command = client.SendCommandAsync(
            "cancel_command",
            timeout: TestTimeout,
            cancellationToken: commandCancellation.Token);
        _ = await connection.ReadRequestAsync(testCancellation.Token);

        commandCancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await command);
        Assert.IsTrue(client.IsConnected);
    }

    [TestMethod]
    public async Task RemoteDisconnectFailsPendingRequestAndReconnectCanSendAgain()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var client = new YeelightClient();

        await client.ConnectAsync(server.EndPoint, testCancellation.Token);
        LoopbackYeelightConnection firstConnection =
            await server.AcceptAsync(testCancellation.Token);

        Task<YeelightSuccessResponse> interruptedCommand =
            client.SendCommandAsync(
                "interrupted_command",
                timeout: TestTimeout,
                cancellationToken: testCancellation.Token);
        _ = await firstConnection.ReadRequestAsync(testCancellation.Token);
        await firstConnection.DisposeAsync();

        await Assert.ThrowsExactlyAsync<YeelightDisconnectedException>(
            async () => await interruptedCommand);
        await WaitUntilAsync(
            () => !client.IsConnected,
            testCancellation.Token);

        Task<LoopbackYeelightConnection> acceptAgain =
            server.AcceptAsync(testCancellation.Token);
        await client.ReconnectAsync(testCancellation.Token);
        await using LoopbackYeelightConnection secondConnection =
            await acceptAgain;

        Task<YeelightSuccessResponse> recoveredCommand =
            client.SendCommandAsync(
                "recovered_command",
                timeout: TestTimeout,
                cancellationToken: testCancellation.Token);
        ReceivedRequest recoveredRequest =
            await secondConnection.ReadRequestAsync(testCancellation.Token);

        await secondConnection.WriteTextAsync(
            $$"""{"id":{{recoveredRequest.Id}},"result":["ok"]}""" + "\r\n",
            testCancellation.Token);

        YeelightSuccessResponse response = await recoveredCommand;
        Assert.AreEqual("ok", response.Results[0].GetString());
        Assert.IsTrue(client.IsConnected);
    }

    [TestMethod]
    public async Task SubscriberExceptionsAreReportedWithoutBreakingTransport()
    {
        using var testCancellation = new CancellationTokenSource(TestTimeout);
        await using var server = new LoopbackYeelightServer();
        await using var client = new YeelightClient();

        var subscriberErrors = new ConcurrentQueue<YeelightSubscriberErrorEventArgs>();
        var errorsObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        client.NotificationReceived += static (_, _) =>
            throw new InvalidOperationException("Notification observer failed.");
        client.ProtocolErrorReceived += static (_, _) =>
            throw new InvalidOperationException("Protocol-error observer failed.");
        client.SubscriberErrorReceived += (_, eventArgs) =>
        {
            subscriberErrors.Enqueue(eventArgs);
            if (subscriberErrors.Count >= 2)
            {
                errorsObserved.TrySetResult();
            }
        };

        await client.ConnectAsync(server.EndPoint, testCancellation.Token);
        await using LoopbackYeelightConnection connection =
            await server.AcceptAsync(testCancellation.Token);

        Task<YeelightSuccessResponse> command = client.SendCommandAsync(
            "survives_observers",
            timeout: TestTimeout,
            cancellationToken: testCancellation.Token);
        ReceivedRequest request =
            await connection.ReadRequestAsync(testCancellation.Token);

        string frames =
            "{invalid json}\r\n"
            + """{"method":"props","params":{"power":"on"}}"""
            + "\r\n"
            + $$"""{"id":{{request.Id}},"result":["still-connected"]}"""
            + "\r\n";
        await connection.WriteTextAsync(frames, testCancellation.Token);

        YeelightSuccessResponse response = await command;
        await errorsObserved.Task.WaitAsync(testCancellation.Token);

        Assert.AreEqual("still-connected", response.Results[0].GetString());
        Assert.IsTrue(client.IsConnected);
        CollectionAssert.AreEquivalent(
            new[]
            {
                nameof(client.ProtocolErrorReceived),
                nameof(client.NotificationReceived),
            },
            subscriberErrors.Select(error => error.EventName).ToArray());
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }
}
