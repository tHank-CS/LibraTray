using System.Net.Sockets;
using LibraTray.Core.Protocol;

namespace LibraTray.MockDevice;

internal static class MockApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.WriteLine("Yeelight Libra Pro Mock Device");

        MockOptions options;
        try
        {
            options = MockOptions.Parse(args);
            ValidateOptions(options);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"参数错误：{exception.Message}");
            PrintHelp();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        using var userCancellation = new CancellationTokenSource();
        using var durationCancellation = options.Duration is null
            ? null
            : new CancellationTokenSource(options.Duration.Value);
        using var operationCancellation = durationCancellation is null
            ? CancellationTokenSource.CreateLinkedTokenSource(userCancellation.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(
                userCancellation.Token,
                durationCancellation.Token);

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            userCancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var server = new MockDeviceServer(options);
            await server.RunAsync(operationCancellation.Token).ConfigureAwait(false);

            if (userCancellation.IsCancellationRequested)
            {
                Console.Error.WriteLine("模拟设备已由 Ctrl+C 安全停止。");
                return 130;
            }

            if (durationCancellation?.IsCancellationRequested == true)
            {
                Console.WriteLine("模拟时长已到，服务已安全停止。");
            }

            return 0;
        }
        catch (SocketException exception)
        {
            Console.Error.WriteLine(
                $"网络错误 ({exception.SocketErrorCode})：{exception.Message}");
            return 3;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"I/O 错误：{exception.Message}");
            return 3;
        }
        catch (YeelightProtocolException exception)
        {
            Console.Error.WriteLine($"协议错误：{exception.Message}");
            return 3;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void ValidateOptions(MockOptions options)
    {
        _ = options.Mode;
        _ = options.ListenAddress;
        _ = options.TcpPort;
        _ = options.DiscoveryPort;
        _ = options.Delay;
        _ = options.Duration;
        _ = options.Model;
        _ = options.Name;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """

            用法：
              LibraTray.MockDevice [选项]

            选项：
              --mode MODE              默认 normal
              --listen-address IPv4    默认 127.0.0.1；仅允许 127.0.0.0/8 loopback
              --tcp-port N             默认 55443
              --discovery-port N       默认 1982
              --delay-ms N             delay 模式延迟，默认 1500
              --duration-seconds N     0 表示运行到 Ctrl+C，默认 0
              --model VALUE            discovery 内部型号，默认 lamp15
              --name VALUE             discovery 上报名，默认 Mock Libra Pro
              --help                   显示帮助

            MODE：
              normal       正常响应
              delay        延迟后正常响应
              error        对每个请求返回协议错误
              no-response  消费请求但不响应，连接保持打开
              split        把一个 CRLF 帧拆为三次 TCP 写入
              coalesce     把响应与 props 两帧合并为一次 TCP 写入
              props        连接后及每次响应后发送真实状态的部分 props 通知
              disconnect   收到首个完整请求后主动断线
              restart      首个完整请求触发一次断线、状态重置及固定 200 ms
                           TCP 不可用窗口；随后重连按 normal 响应
              bad-props    query 返回真实状态，但主动 props 故意给出冲突值
              incorrect-props
                           bad-props 的同义名称
              cold-start-silent
                           初始双通道全关；普通背景开灯返回 ok 但状态不变，
                           直到 bg_set_scene 重新初始化背景输出

            支持的通用命令：
              get_prop, set_power, set_bright, set_ct_abx, set_rgb,
              bg_set_power, bg_set_bright, bg_set_rgb, bg_set_scene

            restart 是确定性的协议重连测试接缝，不会重启进程或操作系统。
            """);
    }
}
