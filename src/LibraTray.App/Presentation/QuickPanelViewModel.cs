using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using LibraTray.Core.Devices.LibraPro;
using LibraTray.Core.Identity;

namespace LibraTray.App.Presentation;

internal sealed class QuickPanelViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly LibraProDeviceSession _session;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly CancellationToken _lifetimeToken;

    private string _connectionStatus = "正在等待局域网设备";
    private string? _errorMessage;
    private bool _isDeviceOnline;
    private bool _isBusy;
    private bool _mainPower;
    private bool _backgroundPower;
    private int _mainBrightness = 50;
    private int _mainColorTemperature = 4_000;
    private int _backgroundBrightness = 50;
    private int _backgroundRgb = 13_395_711;
    private bool _disposed;

    public QuickPanelViewModel(
        LibraProDeviceSession session,
        Dispatcher dispatcher,
        CancellationToken lifetimeToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _session = session;
        _dispatcher = dispatcher;
        _lifetimeToken = lifetimeToken;
        DeviceName = ProductIdentityCatalog.LibraProFriendlyProductName;
        _session.StatusChanged += OnSessionStatusChanged;
        _session.StateChanged += OnSessionStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DeviceName { get; }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetField(ref _connectionStatus, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public bool IsDeviceOnline
    {
        get => _isDeviceOnline;
        private set
        {
            if (SetField(ref _isDeviceOnline, value))
            {
                OnPropertyChanged(nameof(CanControl));
                OnPropertyChanged(nameof(CanRetry));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanControl));
                OnPropertyChanged(nameof(CanRetry));
            }
        }
    }

    public bool CanControl => IsDeviceOnline && !IsBusy;

    public bool CanRetry => !IsDeviceOnline && !IsBusy;

    public bool MainPower
    {
        get => _mainPower;
        private set => SetField(ref _mainPower, value);
    }

    public bool BackgroundPower
    {
        get => _backgroundPower;
        private set => SetField(ref _backgroundPower, value);
    }

    public int MainBrightness
    {
        get => _mainBrightness;
        private set => SetField(ref _mainBrightness, value);
    }

    public int MainColorTemperature
    {
        get => _mainColorTemperature;
        private set => SetField(ref _mainColorTemperature, value);
    }

    public int BackgroundBrightness
    {
        get => _backgroundBrightness;
        private set => SetField(ref _backgroundBrightness, value);
    }

    public int BackgroundRgb
    {
        get => _backgroundRgb;
        private set => SetField(ref _backgroundRgb, value);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            () => _session.DiscoverAndConnectAsync(
                cancellationToken: ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public Task SetMainPowerAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            () => _session.SetMainPowerAsync(
                enabled,
                ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public Task SetBackgroundPowerAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            () => _session.SetBackgroundPowerAsync(
                enabled,
                ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public Task SetMainBrightnessAsync(
        int brightness,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            () => _session.SetMainBrightnessAsync(
                brightness,
                ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public Task SetMainColorTemperatureAsync(
        int colorTemperature,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            () => _session.SetMainColorTemperatureAsync(
                colorTemperature,
                ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public Task SetBackgroundBrightnessAsync(
        int brightness,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            () => _session.SetBackgroundBrightnessAsync(
                brightness,
                ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public Task SetBackgroundRgbAsync(
        int rgb,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            () => _session.SetBackgroundRgbAsync(
                rgb,
                ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.StatusChanged -= OnSessionStatusChanged;
        _session.StateChanged -= OnSessionStateChanged;
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationLock.WaitAsync(cancellationToken);

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            if (IsDeviceOnline)
            {
                ConnectionStatus = "正在应用设置";
            }

            await operation();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = ToUserMessage(exception);
        }
        finally
        {
            IsBusy = false;
            if (_session.Status == LibraProSessionStatus.Connected)
            {
                ConnectionStatus = "已连接 · 本地控制";
            }

            _operationLock.Release();
        }
    }

    private void OnSessionStatusChanged(
        object? sender,
        LibraProSessionStatusChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = _dispatcher.BeginInvoke(() =>
        {
            IsDeviceOnline =
                eventArgs.Status
                    is LibraProSessionStatus.Connected
                    or LibraProSessionStatus.Retrying;
            ConnectionStatus = eventArgs.Status switch
            {
                LibraProSessionStatus.Discovering => "正在搜索局域网设备",
                LibraProSessionStatus.Connecting => "正在建立本地连接",
                LibraProSessionStatus.Connected => "已连接 · 本地控制",
                LibraProSessionStatus.Retrying =>
                    $"设备响应较慢 · 正在重试 "
                    + $"{eventArgs.RetryAttempt}/{eventArgs.RetryLimit}",
                LibraProSessionStatus.Faulted => "连接或控制失败",
                _ => "未连接",
            };
            if (eventArgs.Status == LibraProSessionStatus.Retrying)
            {
                ErrorMessage = null;
            }
            else if (eventArgs.Status == LibraProSessionStatus.Connected)
            {
                ErrorMessage = eventArgs.Exception is null
                    ? null
                    : "本次状态同步失败，但设备连接仍然可用。";
            }
        });
    }

    private void OnSessionStateChanged(
        object? sender,
        LibraProStateChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = _dispatcher.BeginInvoke(() => ApplyState(eventArgs.State));
    }

    private void ApplyState(LibraProState state)
    {
        MainPower = state.MainPower;
        BackgroundPower = state.BackgroundPower;
        MainBrightness = state.MainBrightness;
        MainColorTemperature = state.MainColorTemperature;
        BackgroundBrightness = state.BackgroundBrightness;
        BackgroundRgb = state.BackgroundRgb;
    }

    private static string ToUserMessage(Exception exception) =>
        exception switch
        {
            InvalidOperationException when
                exception.Message.Contains(
                    "No discovery response",
                    StringComparison.Ordinal) =>
                "未发现 Yeelight Libra Pro。请确认灯具在线且局域网控制已开启。",
            InvalidOperationException when
                exception.Message.Contains(
                    "Multiple exact lamp15",
                    StringComparison.Ordinal) =>
                "发现多台 Libra Pro；设备选择功能尚未开放。",
            TimeoutException => "设备响应超时，请稍后重试。",
            _ => "操作未完成，请确认设备在线后重试。",
        };

    private CancellationToken ResolveToken(CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled
            ? cancellationToken
            : _lifetimeToken;

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
