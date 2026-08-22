using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
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
    private readonly SegmentRgbRuntimeStateStore _segmentStateStore;

    private string _connectionStatus;
    private string _deviceName;
    private string? _errorMessage;
    private string? _hotkeyStatus;
    private string? _automationStatus;
    private string? _diagnosticStatus;
    private LibraProPreset? _selectedPreset;
    private bool _isDeviceOnline;
    private bool _isBusy;
    private bool _mainPower;
    private bool _backgroundPower;
    private int _mainBrightness = 50;
    private int _mainColorTemperature = 4_000;
    private int _backgroundBrightness = 50;
    private int _backgroundRgb = 13_395_711;
    private int _leftSegmentRgb = 16_711_680;
    private int _rightSegmentRgb = 255;
    private AmbientColorMode _ambientColorMode;
    private bool _experimentalSegmentRgbEnabled;
    private string? _segmentRgbStatus;
    private SegmentRgbRuntimeState? _segmentRuntimeState;
    private bool _unknownFirmwareAcknowledgedForSession;
    private string? _loadedSegmentDeviceKey;
    private int _mainBrightnessStep = 5;
    private int _backgroundBrightnessStep = 20;
    private int _mainColorTemperatureStep = 100;
    private int _mainColorTemperatureMinimum = 3_000;
    private int _mainColorTemperatureMaximum = 6_400;
    private DoubleCollection _mainBrightnessTicks = CreateBrightnessTicks(5);
    private DoubleCollection _backgroundBrightnessTicks =
        CreateBrightnessTicks(20);
    private DoubleCollection _mainColorTemperatureTicks =
        CreateColorTemperatureTicks(100, 3_000, 6_400);
    private LibraProSessionStatusChangedEventArgs? _lastStatus;
    private bool _disposed;

    public QuickPanelViewModel(
        LibraProDeviceSession session,
        Dispatcher dispatcher,
        LibraTraySettings settings,
        SegmentRgbRuntimeStateStore segmentStateStore,
        CancellationToken lifetimeToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(segmentStateStore);
        _session = session;
        _dispatcher = dispatcher;
        _lifetimeToken = lifetimeToken;
        _segmentStateStore = segmentStateStore;
        _segmentRuntimeState = segmentStateStore.Load();
        _connectionStatus = UiText.Get("Message.WaitingForDevice");
        _deviceName = ResolveDeviceName(settings.UserAlias);
        ApplyStepSettings(settings);
        _experimentalSegmentRgbEnabled = settings.ExperimentalSegmentRgbEnabled;
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

    public string? DiagnosticStatus
    {
        get => _diagnosticStatus;
        private set => SetField(ref _diagnosticStatus, value);
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
                OnPropertyChanged(nameof(TrayRefreshLabel));
                OnPropertyChanged(nameof(CanApplySegmentRgb));
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
                OnPropertyChanged(nameof(CanRefresh));
                OnPropertyChanged(nameof(CanApplySegmentRgb));
            }
        }
    }

    public bool CanControl => IsDeviceOnline && !IsBusy;

    public bool CanRetry => !IsDeviceOnline && !IsBusy;

    public bool CanRefresh => !IsBusy;

    public string TrayRefreshLabel => UiText.Get(
        IsDeviceOnline ? "Tray.Refresh" : "Tray.Reconnect");

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

    public bool ExperimentalSegmentRgbEnabled
    {
        get => _experimentalSegmentRgbEnabled;
        private set
        {
            if (SetField(ref _experimentalSegmentRgbEnabled, value))
            {
                OnPropertyChanged(nameof(CanApplySegmentRgb));
            }
        }
    }

    public bool IsWholeColorMode => _ambientColorMode == AmbientColorMode.Whole;

    public bool IsSegmentColorMode => _ambientColorMode == AmbientColorMode.Segmented;

    public int LeftSegmentRgb
    {
        get => _leftSegmentRgb;
        private set
        {
            if (SetField(ref _leftSegmentRgb, value))
            {
                OnPropertyChanged(nameof(LeftSegmentBrush));
            }
        }
    }

    public int RightSegmentRgb
    {
        get => _rightSegmentRgb;
        private set
        {
            if (SetField(ref _rightSegmentRgb, value))
            {
                OnPropertyChanged(nameof(RightSegmentBrush));
            }
        }
    }

    public Brush LeftSegmentBrush => CreateRgbBrush(LeftSegmentRgb);

    public Brush RightSegmentBrush => CreateRgbBrush(RightSegmentRgb);

    public string? SegmentRgbStatus
    {
        get => _segmentRgbStatus;
        private set => SetField(ref _segmentRgbStatus, value);
    }

    public bool HasSegmentRgbCapability =>
        _session.ConnectionInfo?.Capabilities.Contains(
            "set_segment_rgb",
            StringComparer.Ordinal) == true;

    public bool CanApplySegmentRgb => CanControl
        && ExperimentalSegmentRgbEnabled
        && HasSegmentRgbCapability;

    public bool HasPendingSegmentStartupReplay =>
        ExperimentalSegmentRgbEnabled
        && _segmentRuntimeState is
        {
            LastColorMode: AmbientColorMode.Segmented,
            LastSegmentRequest: not null,
        };

    public int MainBrightnessStep => _mainBrightnessStep;

    public int BackgroundBrightnessStep => _backgroundBrightnessStep;

    public int MainColorTemperatureStep => _mainColorTemperatureStep;

    public int MainColorTemperatureMinimum => _mainColorTemperatureMinimum;

    public int MainColorTemperatureMaximum => _mainColorTemperatureMaximum;

    public DoubleCollection MainBrightnessTicks => _mainBrightnessTicks;

    public DoubleCollection BackgroundBrightnessTicks =>
        _backgroundBrightnessTicks;

    public DoubleCollection MainColorTemperatureTicks =>
        _mainColorTemperatureTicks;

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

    public async Task SetBackgroundRgbAsync(
        int rgb,
        CancellationToken cancellationToken = default)
    {
        CancellationToken token = ResolveToken(cancellationToken);
        await RunManualAsync(
            async () =>
            {
                _ = await _session.SetBackgroundRgbAsync(rgb, token);
                SetAmbientColorMode(AmbientColorMode.Whole);
                SaveSegmentRuntimeState(AmbientColorMode.Whole, null);
                SegmentRgbStatus = null;
            },
            token);
    }

    public void SelectWholeColorMode() =>
        SetAmbientColorMode(AmbientColorMode.Whole);

    public void SelectSegmentColorMode()
    {
        if (ExperimentalSegmentRgbEnabled)
        {
            SetAmbientColorMode(AmbientColorMode.Segmented);
            SegmentRgbStatus = UiText.Get("Message.SegmentRgbUnconfirmed");
        }
    }

    public void SetLeftSegmentRgb(int rgb) => LeftSegmentRgb = rgb;

    public void SetRightSegmentRgb(int rgb) => RightSegmentRgb = rgb;

    public void SwapSegmentRgb() =>
        (LeftSegmentRgb, RightSegmentRgb) =
            (RightSegmentRgb, LeftSegmentRgb);

    public async Task ApplySegmentRgbAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanApplySegmentRgb
            || GetSegmentFirmwareGate() != SegmentRgbFirmwareAccess.Allowed)
        {
            throw new InvalidOperationException(
                "Segment RGB is not enabled or acknowledged for this connection.");
        }

        CancellationToken token = ResolveToken(cancellationToken);
        var request = new SegmentRgbRequest(LeftSegmentRgb, RightSegmentRgb);
        await RunManualAsync(
            async () =>
            {
                _ = await _session.SetSegmentRgbAsync(request, token);
                SetAmbientColorMode(AmbientColorMode.Segmented);
                SaveSegmentRuntimeState(AmbientColorMode.Segmented, request);
                SegmentRgbStatus = UiText.Get("Message.SegmentRgbApplied");
            },
            token);
    }

    public SegmentRgbFirmwareAccess GetSegmentFirmwareGate()
    {
        if (_session.ConnectionInfo is not { } connection)
        {
            return SegmentRgbFirmwareAccess.Unsupported;
        }

        return SegmentRgbFirmwarePolicy.Evaluate(
            connection.InternalModel,
            connection.Capabilities,
            connection.FirmwareVersion,
            _segmentRuntimeState?.AcknowledgedFirmwareVersions ?? [],
            _unknownFirmwareAcknowledgedForSession);
    }

    public void AcknowledgeCurrentSegmentFirmware()
    {
        string? firmware = _session.ConnectionInfo?.FirmwareVersion?.Trim();
        if (string.IsNullOrWhiteSpace(firmware))
        {
            _unknownFirmwareAcknowledgedForSession = true;
            return;
        }

        EnsureSegmentRuntimeState();
        if (_segmentRuntimeState is null)
        {
            SaveSegmentRuntimeState(AmbientColorMode.Whole, null);
        }

        if (_segmentRuntimeState is null)
        {
            throw new InvalidOperationException(
                "A device-specific segment state could not be created.");
        }

        string[] acknowledged = _segmentRuntimeState.AcknowledgedFirmwareVersions
            .Append(firmware)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _segmentRuntimeState = _segmentStateStore.Save(
            _segmentRuntimeState with
            {
                AcknowledgedFirmwareVersions = acknowledged,
                UpdatedUtc = DateTimeOffset.UtcNow,
            });
    }

    public async Task<bool> TryReplaySegmentOnWindowsStartupAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureSegmentRuntimeState();
        if (!ExperimentalSegmentRgbEnabled
            || _segmentRuntimeState?.LastColorMode != AmbientColorMode.Segmented
            || _segmentRuntimeState.LastSegmentRequest is not { } request
            || GetSegmentFirmwareGate() != SegmentRgbFirmwareAccess.Allowed)
        {
            return false;
        }

        _ = await _session.SetSegmentRgbAsync(request, cancellationToken);
        LeftSegmentRgb = request.LeftRgb;
        RightSegmentRgb = request.RightRgb;
        SetAmbientColorMode(AmbientColorMode.Segmented);
        SegmentRgbStatus = UiText.Get("Message.SegmentRgbApplied");
        return true;
    }

    public async Task<bool> RunBackgroundPostAsync(
        CancellationToken cancellationToken = default)
    {
        CancellationToken token = ResolveToken(cancellationToken);
        bool completed = false;
        await RunManualAsync(
            async () =>
            {
                LibraProState state = _session.CurrentState
                    ?? throw new InvalidOperationException(
                        "No confirmed device state is available.");
                EnsureSegmentRuntimeState();
                SegmentRgbRequest? segment = ExperimentalSegmentRgbEnabled
                    && _segmentRuntimeState?.LastColorMode
                        == AmbientColorMode.Segmented
                    && GetSegmentFirmwareGate()
                        == SegmentRgbFirmwareAccess.Allowed
                        ? _segmentRuntimeState.LastSegmentRequest
                        : null;
                _ = await _session.RunBackgroundPostAsync(
                    new LibraProBackgroundSnapshot(
                        state.BackgroundBrightness,
                        state.BackgroundRgb),
                    state.BackgroundPower,
                    segment,
                    token);
                completed = true;
            },
            token);
        return completed;
    }

    public LibraProTargetState? CreateCurrentTargetState()
    {
        if (_session.CurrentState is not { } state)
        {
            return null;
        }

        EnsureSegmentRuntimeState();
        AmbientColorMode mode = ExperimentalSegmentRgbEnabled
            ? _segmentRuntimeState?.LastColorMode ?? AmbientColorMode.Whole
            : AmbientColorMode.Whole;
        SegmentRgbRequest? segment = mode == AmbientColorMode.Segmented
            ? _segmentRuntimeState?.LastSegmentRequest
            : null;
        if (mode == AmbientColorMode.Segmented && segment is null)
        {
            mode = AmbientColorMode.Whole;
        }

        return new LibraProTargetState(
            state.MainPower,
            state.MainBrightness,
            state.MainColorTemperature,
            state.BackgroundPower,
            state.BackgroundBrightness,
            state.BackgroundRgb,
            mode,
            segment);
    }

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
            if ((normalizedDirection > 0
                    && current >= _mainColorTemperatureMaximum)
                || (normalizedDirection < 0
                    && current <= _mainColorTemperatureMinimum))
            {
                return;
            }

            int value = current < _mainColorTemperatureMinimum
                || current > _mainColorTemperatureMaximum
                    ? Math.Clamp(
                        current,
                        _mainColorTemperatureMinimum,
                        _mainColorTemperatureMaximum)
                    : Math.Clamp(
                        current
                            + normalizedDirection
                            * _mainColorTemperatureStep,
                        _mainColorTemperatureMinimum,
                        _mainColorTemperatureMaximum);
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

    public void SetDiagnosticStatus(string? status) =>
        DiagnosticStatus = status;

    public void ApplySettings(LibraTraySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DeviceName = ResolveDeviceName(settings.UserAlias);
        ApplyStepSettings(settings);
        ExperimentalSegmentRgbEnabled =
            settings.ExperimentalSegmentRgbEnabled;
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
                async () =>
                {
                    if (preset.AmbientColorMode == AmbientColorMode.Segmented
                        && (!ExperimentalSegmentRgbEnabled
                            || GetSegmentFirmwareGate()
                                != SegmentRgbFirmwareAccess.Allowed))
                    {
                        throw new InvalidOperationException(
                            "The segmented preset is not enabled or acknowledged.");
                    }

                    _ = await _session.ApplyTargetStateOnceAsync(
                        preset.ToTargetState(),
                        _lifetimeToken);
                    SetAmbientColorMode(preset.AmbientColorMode);
                    if (preset.SegmentRgb is { } segment)
                    {
                        LeftSegmentRgb = segment.LeftRgb;
                        RightSegmentRgb = segment.RightRgb;
                    }

                    SaveSegmentRuntimeState(
                        preset.AmbientColorMode,
                        preset.SegmentRgb);
                    SegmentRgbStatus = preset.AmbientColorMode
                        == AmbientColorMode.Segmented
                            ? UiText.Get("Message.SegmentRgbApplied")
                            : null;
                },
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
            AmbientColorMode = ExperimentalSegmentRgbEnabled
                ? _ambientColorMode
                : AmbientColorMode.Whole,
            SegmentRgb = ExperimentalSegmentRgbEnabled
                && _ambientColorMode == AmbientColorMode.Segmented
                    ? new SegmentRgbRequest(LeftSegmentRgb, RightSegmentRgb)
                    : null,
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
        EnsureSegmentRuntimeState();
        OnPropertyChanged(nameof(HasSegmentRgbCapability));
        OnPropertyChanged(nameof(CanApplySegmentRgb));
    }

    private void EnsureSegmentRuntimeState()
    {
        string? deviceKey = SegmentRgbRuntimeStateStore.CreateDeviceKey(
            _session.DeviceId);
        if (deviceKey is null
            || string.Equals(
                deviceKey,
                _loadedSegmentDeviceKey,
                StringComparison.Ordinal))
        {
            return;
        }

        _loadedSegmentDeviceKey = deviceKey;
        if (_segmentRuntimeState is { } stored
            && string.Equals(stored.DeviceKey, deviceKey, StringComparison.Ordinal))
        {
            SetAmbientColorMode(stored.LastColorMode);
            if (stored.LastSegmentRequest is { } request)
            {
                LeftSegmentRgb = request.LeftRgb;
                RightSegmentRgb = request.RightRgb;
                SegmentRgbStatus = UiText.Get("Message.SegmentRgbUnconfirmed");
            }

            return;
        }

        _segmentRuntimeState = null;
        SetAmbientColorMode(AmbientColorMode.Whole);
        LeftSegmentRgb = BackgroundRgb;
        RightSegmentRgb = BackgroundRgb;
        SegmentRgbStatus = null;
    }

    private void SaveSegmentRuntimeState(
        AmbientColorMode mode,
        SegmentRgbRequest? segmentRequest)
    {
        EnsureSegmentRuntimeState();
        string? deviceKey = SegmentRgbRuntimeStateStore.CreateDeviceKey(
            _session.DeviceId);
        if (deviceKey is null)
        {
            return;
        }

        _segmentRuntimeState = _segmentStateStore.Save(
            new SegmentRgbRuntimeState
            {
                DeviceKey = deviceKey,
                LastColorMode = mode,
                LastSegmentRequest = segmentRequest
                    ?? _segmentRuntimeState?.LastSegmentRequest,
                AcknowledgedFirmwareVersions =
                    _segmentRuntimeState?.AcknowledgedFirmwareVersions ?? [],
                UpdatedUtc = DateTimeOffset.UtcNow,
            });
    }

    private void SetAmbientColorMode(AmbientColorMode mode)
    {
        if (_ambientColorMode == mode)
        {
            return;
        }

        _ambientColorMode = mode;
        OnPropertyChanged(nameof(IsWholeColorMode));
        OnPropertyChanged(nameof(IsSegmentColorMode));
    }

    private static SolidColorBrush CreateRgbBrush(int rgb)
    {
        var brush = new SolidColorBrush(Color.FromRgb(
            (byte)(rgb >> 16),
            (byte)(rgb >> 8),
            (byte)rgb));
        brush.Freeze();
        return brush;
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

    private void ApplyStepSettings(LibraTraySettings settings)
    {
        _mainBrightnessStep = settings.BrightnessStep;
        _backgroundBrightnessStep = settings.BackgroundBrightnessStep;
        _mainColorTemperatureStep = settings.ColorTemperatureStep;
        _mainColorTemperatureMinimum = settings.AllowExtremeColorTemperature
            ? 2_700
            : 3_000;
        _mainColorTemperatureMaximum = settings.AllowExtremeColorTemperature
            ? 6_500
            : 6_400;
        _mainBrightnessTicks = CreateBrightnessTicks(_mainBrightnessStep);
        _backgroundBrightnessTicks = CreateBrightnessTicks(
            _backgroundBrightnessStep);
        _mainColorTemperatureTicks = CreateColorTemperatureTicks(
            _mainColorTemperatureStep,
            _mainColorTemperatureMinimum,
            _mainColorTemperatureMaximum);
        OnPropertyChanged(nameof(MainBrightnessStep));
        OnPropertyChanged(nameof(BackgroundBrightnessStep));
        OnPropertyChanged(nameof(MainColorTemperatureStep));
        OnPropertyChanged(nameof(MainColorTemperatureMinimum));
        OnPropertyChanged(nameof(MainColorTemperatureMaximum));
        OnPropertyChanged(nameof(MainBrightnessTicks));
        OnPropertyChanged(nameof(BackgroundBrightnessTicks));
        OnPropertyChanged(nameof(MainColorTemperatureTicks));
    }

    private static DoubleCollection CreateBrightnessTicks(int step)
    {
        var ticks = new DoubleCollection { 1 };
        for (int value = step; value <= 100; value += step)
        {
            if (value != 1)
            {
                ticks.Add(value);
            }
        }

        if (ticks[^1] != 100)
        {
            ticks.Add(100);
        }

        ticks.Freeze();
        return ticks;
    }

    private static DoubleCollection CreateColorTemperatureTicks(
        int step,
        int minimum,
        int maximum)
    {
        var ticks = new DoubleCollection();
        for (int value = minimum; value <= maximum; value += step)
        {
            ticks.Add(value);
        }

        if (ticks[^1] != maximum)
        {
            ticks.Add(maximum);
        }

        ticks.Freeze();
        return ticks;
    }

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
