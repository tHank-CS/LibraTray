using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using LibraTray.Core.Configuration;
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
    private string _deviceName;
    private string? _errorMessage;
    private string? _hotkeyStatus;
    private LibraProPreset? _selectedPreset;
    private bool _isDeviceOnline;
    private bool _isBusy;
    private bool _mainPower;
    private bool _backgroundPower;
    private int _mainBrightness = 50;
    private int _mainColorTemperature = 4_000;
    private int _backgroundBrightness = 50;
    private int _backgroundRgb = 13_395_711;
    private int _mainBrightnessStep;
    private int _mainColorTemperatureStep;
    private bool _disposed;

    public QuickPanelViewModel(
        LibraProDeviceSession session,
        Dispatcher dispatcher,
        LibraTraySettings settings,
        CancellationToken lifetimeToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(settings);
        _session = session;
        _dispatcher = dispatcher;
        _lifetimeToken = lifetimeToken;
        _deviceName = ResolveDeviceName(settings.UserAlias);
        _mainBrightnessStep = settings.BrightnessStep;
        _mainColorTemperatureStep = settings.ColorTemperatureStep;
        Presets = new ObservableCollection<LibraProPreset>(settings.Presets);
        _selectedPreset = Presets.FirstOrDefault();
        _session.StatusChanged += OnSessionStatusChanged;
        _session.StateChanged += OnSessionStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? PresetsChanged;

    public string DeviceName
    {
        get => _deviceName;
        private set => SetField(ref _deviceName, value);
    }

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

    public string? HotkeyStatus
    {
        get => _hotkeyStatus;
        private set => SetField(ref _hotkeyStatus, value);
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

    public ObservableCollection<LibraProPreset> Presets { get; }

    public LibraProPreset? SelectedPreset
    {
        get => _selectedPreset;
        set => SetField(ref _selectedPreset, value);
    }

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

    public Task ToggleMainPowerFromHotkeyAsync() =>
        QueueHotkeyAsync(
            cancellationToken => _session.SetMainPowerAsync(
                !(_session.CurrentState?.MainPower ?? MainPower),
                cancellationToken));

    public Task ToggleBackgroundPowerFromHotkeyAsync() =>
        QueueHotkeyAsync(
            cancellationToken => _session.SetBackgroundPowerAsync(
                !(_session.CurrentState?.BackgroundPower ?? BackgroundPower),
                cancellationToken));

    public Task AdjustMainBrightnessFromHotkeyAsync(int direction)
    {
        if (direction == 0)
        {
            return Task.CompletedTask;
        }

        int normalizedDirection = Math.Sign(direction);
        return QueueHotkeyAsync(async cancellationToken =>
        {
            int current = _session.CurrentState?.MainBrightness
                ?? MainBrightness;
            int value = Math.Clamp(
                current + normalizedDirection * _mainBrightnessStep,
                1,
                100);
            if (value != current)
            {
                _ = await _session.SetMainBrightnessAsync(
                    value,
                    cancellationToken);
            }
        });
    }

    public Task AdjustMainColorTemperatureFromHotkeyAsync(int direction)
    {
        if (direction == 0)
        {
            return Task.CompletedTask;
        }

        int normalizedDirection = Math.Sign(direction);
        return QueueHotkeyAsync(async cancellationToken =>
        {
            int current = _session.CurrentState?.MainColorTemperature
                ?? MainColorTemperature;
            int value = Math.Clamp(
                current + normalizedDirection * _mainColorTemperatureStep,
                3_000,
                6_500);
            if (value != current)
            {
                _ = await _session.SetMainColorTemperatureAsync(
                    value,
                    cancellationToken);
            }
        });
    }

    private Task QueueHotkeyAsync(
        Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return IsDeviceOnline
            ? RunAsync(
                () => operation(_lifetimeToken),
                _lifetimeToken)
            : Task.CompletedTask;
    }

    public void SetHotkeyRegistrationFailures(
        IReadOnlyList<string> failedHotkeys)
    {
        ArgumentNullException.ThrowIfNull(failedHotkeys);
        HotkeyStatus = failedHotkeys.Count == 0
            ? null
            : $"快捷键冲突：{string.Join("、", failedHotkeys)}";
    }

    public void ApplySettings(LibraTraySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DeviceName = ResolveDeviceName(settings.UserAlias);
        _mainBrightnessStep = settings.BrightnessStep;
        _mainColorTemperatureStep = settings.ColorTemperatureStep;
    }

    public Task ApplySelectedPresetAsync() =>
        SelectedPreset is { } preset && IsDeviceOnline
            ? RunAsync(
                () => _session.ApplyTargetStateAsync(
                    preset.ToTargetState(),
                    _lifetimeToken),
                _lifetimeToken)
            : Task.CompletedTask;

    public bool TrySaveCurrentPreset(string name, out string? error)
    {
        error = null;
        if (_session.CurrentState is not { } state)
        {
            error = "设备尚未提供可保存的确认状态。";
            return false;
        }

        string normalizedName = name.Trim();
        if (normalizedName.Length == 0 || normalizedName.Length > 64)
        {
            error = "预设名称必须包含 1–64 个字符。";
            return false;
        }

        if (Presets.Count >= 20)
        {
            error = "最多可以保存 20 个本地预设。";
            return false;
        }

        var preset = new LibraProPreset
        {
            Name = normalizedName,
            MainPower = state.MainPower,
            MainBrightness = state.MainBrightness,
            MainColorTemperature = state.MainColorTemperature,
            BackgroundPower = state.BackgroundPower,
            BackgroundBrightness = state.BackgroundBrightness,
            BackgroundRgb = state.BackgroundRgb,
        };
        Presets.Add(preset);
        SelectedPreset = preset;
        PresetsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void DeleteSelectedPreset()
    {
        if (SelectedPreset is not { } preset)
        {
            return;
        }

        int index = Presets.IndexOf(preset);
        _ = Presets.Remove(preset);
        SelectedPreset = Presets.Count == 0
            ? null
            : Presets[Math.Min(index, Presets.Count - 1)];
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

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

    private static string ResolveDeviceName(string? userAlias) =>
        ProductIdentityMapper
            .Resolve(ProductIdentityCatalog.LibraProInternalModel)
            .WithUserAlias(userAlias)
            .DisplayName;

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
