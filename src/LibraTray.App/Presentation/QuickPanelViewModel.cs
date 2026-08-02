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

    private string _connectionStatus;
    private string _deviceName;
    private string? _errorMessage;
    private string? _hotkeyStatus;
    private string? _automationStatus;
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
    private LibraProSessionStatusChangedEventArgs? _lastStatus;
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
        _connectionStatus = UiText.Get("Message.WaitingForDevice");
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

    public event EventHandler? ManualControlRequested;

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

    public string? AutomationStatus
    {
        get => _automationStatus;
        private set => SetField(ref _automationStatus, value);
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
        RunManualAsync(
            () => _session.SetMainPowerAsync(
                enabled,
                ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public Task SetBackgroundPowerAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        RunManualAsync(
            () => _session.SetBackgroundPowerAsync(
                enabled,
                ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public Task SetMainBrightnessAsync(
        int brightness,
        CancellationToken cancellationToken = default) =>
        RunManualAsync(
            () => _session.SetMainBrightnessAsync(
                brightness,
                ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public Task SetMainColorTemperatureAsync(
        int colorTemperature,
        CancellationToken cancellationToken = default) =>
        RunManualAsync(
            () => _session.SetMainColorTemperatureAsync(
                colorTemperature,
                ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public Task SetBackgroundBrightnessAsync(
        int brightness,
        CancellationToken cancellationToken = default) =>
        RunManualAsync(
            () => _session.SetBackgroundBrightnessAsync(
                brightness,
                ResolveToken(cancellationToken)),
            ResolveToken(cancellationToken));

    public Task SetBackgroundRgbAsync(
        int rgb,
        CancellationToken cancellationToken = default) =>
        RunManualAsync(
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

    public Task AdjustMainBrightnessFromHotkeyAsync(int steps)
    {
        if (steps == 0)
        {
            return Task.CompletedTask;
        }

        int boundedSteps = Math.Clamp(steps, -20, 20);
        return QueueHotkeyAsync(async cancellationToken =>
        {
            int current = _session.CurrentState?.MainBrightness
                ?? MainBrightness;
            int value = Math.Clamp(
                current + boundedSteps * _mainBrightnessStep,
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
            ? RunManualAsync(
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
            : UiText.Format(
                "Message.HotkeyConflict",
                string.Join(", ", failedHotkeys));
    }

    public void SetAutomationStatus(string? status) =>
        AutomationStatus = status;

    public void ApplySettings(LibraTraySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DeviceName = ResolveDeviceName(settings.UserAlias);
        _mainBrightnessStep = settings.BrightnessStep;
        _mainColorTemperatureStep = settings.ColorTemperatureStep;
        RefreshLocalizedText();
    }

    public Task ApplySelectedPresetAsync() =>
        SelectedPreset is { } preset
            ? ApplyPresetAsync(preset)
            : Task.CompletedTask;

    public Task ApplyPresetAsync(LibraProPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return IsDeviceOnline
            ? RunManualAsync(
                () => _session.ApplyTargetStateAsync(
                    preset.ToTargetState(),
                    _lifetimeToken),
                _lifetimeToken)
            : Task.CompletedTask;
    }

    public Task SetAllPowerAsync(bool enabled) =>
        IsDeviceOnline
            ? RunManualAsync(
                async () =>
                {
                    _ = await _session.SetMainPowerAsync(
                        enabled,
                        _lifetimeToken);
                    _ = await _session.SetBackgroundPowerAsync(
                        enabled,
                        _lifetimeToken);
                },
                _lifetimeToken)
            : Task.CompletedTask;

    public Task RefreshStateAsync() =>
        IsDeviceOnline
            ? RunAsync(
                () => _session.RefreshAsync(_lifetimeToken),
                _lifetimeToken)
            : ConnectAsync(_lifetimeToken);

    public bool TrySaveCurrentPreset(string name, out string? error)
    {
        error = null;
        if (_session.CurrentState is not { } state)
        {
            error = UiText.Get("Message.NoStateForPreset");
            return false;
        }

        string normalizedName = name.Trim();
        if (normalizedName.Length == 0 || normalizedName.Length > 64)
        {
            error = UiText.Get("Message.PresetNameLength");
            return false;
        }

        if (Presets.Count >= 20)
        {
            error = UiText.Get("Message.PresetLimit");
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
                ConnectionStatus = UiText.Get("Message.ApplyingSettings");
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
                ConnectionStatus = UiText.Get("Message.Connected");
            }

            _operationLock.Release();
        }
    }

    private Task RunManualAsync(
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        ManualControlRequested?.Invoke(this, EventArgs.Empty);
        return RunAsync(operation, cancellationToken);
    }

    private void OnSessionStatusChanged(
        object? sender,
        LibraProSessionStatusChangedEventArgs eventArgs)
    {
        _ = sender;
        _lastStatus = eventArgs;
        _ = _dispatcher.BeginInvoke(() =>
        {
            IsDeviceOnline =
                eventArgs.Status
                    is LibraProSessionStatus.Connected
                    or LibraProSessionStatus.Retrying;
            ConnectionStatus = DescribeStatus(eventArgs);
            if (eventArgs.Status == LibraProSessionStatus.Retrying)
            {
                ErrorMessage = null;
            }
            else if (eventArgs.Status == LibraProSessionStatus.Connected)
            {
                ErrorMessage = eventArgs.Exception is null
                    ? null
                    : UiText.Get("Message.SyncFailed");
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
                UiText.Get("Message.DeviceNotFound"),
            InvalidOperationException when
                exception.Message.Contains(
                    "Multiple exact lamp15",
                    StringComparison.Ordinal) =>
                UiText.Get("Message.MultipleDevices"),
            TimeoutException => UiText.Get("Message.DeviceTimeout"),
            _ => UiText.Get("Message.OperationFailed"),
        };

    private void RefreshLocalizedText()
    {
        ConnectionStatus = _lastStatus is null
            ? UiText.Get("Message.WaitingForDevice")
            : DescribeStatus(_lastStatus);
        ErrorMessage = null;
    }

    private static string DescribeStatus(
        LibraProSessionStatusChangedEventArgs eventArgs) =>
        eventArgs.Status switch
        {
            LibraProSessionStatus.Discovering =>
                UiText.Get("Message.Discovering"),
            LibraProSessionStatus.Connecting =>
                UiText.Get("Message.Connecting"),
            LibraProSessionStatus.Connected => UiText.Get("Message.Connected"),
            LibraProSessionStatus.Retrying => UiText.Format(
                "Message.Retrying",
                eventArgs.RetryAttempt,
                eventArgs.RetryLimit),
            LibraProSessionStatus.Faulted =>
                UiText.Get("Message.ConnectionFailed"),
            _ => UiText.Get("Text.NotConnected"),
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
