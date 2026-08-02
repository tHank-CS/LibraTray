using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LibraTray.App.Interop;
using LibraTray.App.Presentation;
using LibraTray.Core.Automation;
using LibraTray.Core.Configuration;
using LibraTray.Core.Devices.LibraPro;
using Microsoft.Win32;

namespace LibraTray.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the Application lifetime; OnExit deterministically disposes owned resources.")]
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\LibraTray.App";
    private readonly CancellationTokenSource _appCancellation = new();
    private QuickPanelWindow? _quickPanel;
    private QuickPanelViewModel? _quickPanelViewModel;
    private LibraProDeviceSession? _deviceSession;
    private GlobalHotkeyService? _hotkeys;
    private LibraTraySettings _settings = new();
    private LibraTraySettingsStore? _settingsStore;
    private SettingsWindow? _settingsWindow;
    private DeviceDetailsWindow? _deviceDetailsWindow;
    private OnScreenDisplayWindow? _onScreenDisplay;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIconService? _trayIcon;
    private ContextMenu? _trayMenu;
    private DispatcherTimer? _trayWheelTimer;
    private int _pendingTrayWheelSteps;
    private WindowsLifecycleEventService? _lifecycleEvents;
    private WindowsLifecycleAutomationController? _automation;
    private ShutdownRestoreCoordinator? _shutdownRestore;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsStore = new LibraTraySettingsStore();
        _settings = _settingsStore.Load();
        AppearanceManager.Apply(this, _settings);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        if (!TryAcquireSingleInstance())
        {
            MessageBox.Show(
                UiText.Get("Message.AlreadyRunning"),
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        bool osdPreviewRequested = e.Args.Contains(
            "--osd-preview",
            StringComparer.OrdinalIgnoreCase)
            || Environment.GetCommandLineArgs()
                .Skip(1)
                .Contains("--osd-preview", StringComparer.OrdinalIgnoreCase);
        bool showRequested = osdPreviewRequested || e.Args.Contains(
            "--show",
            StringComparer.OrdinalIgnoreCase)
            || Environment.GetCommandLineArgs()
                .Skip(1)
                .Contains("--show", StringComparer.OrdinalIgnoreCase);
        _deviceSession = new LibraProDeviceSession();
        _quickPanelViewModel = new QuickPanelViewModel(
            _deviceSession,
            Dispatcher,
            _settings,
            _appCancellation.Token);
        _quickPanelViewModel.PresetsChanged += OnPresetsChanged;
        _quickPanelViewModel.ManualControlRequested += OnManualControlRequested;
        _automation = new WindowsLifecycleAutomationController(
            _settings.WindowsAutomation,
            () => _deviceSession.CurrentState,
            (target, token) =>
                _deviceSession.ApplyTargetStateAsync(target, token),
            RefreshForAutomationAsync);
        _automation.StatusChanged += OnAutomationStatusChanged;
        _shutdownRestore = new ShutdownRestoreCoordinator(
            _settings.WindowsAutomation,
            new ShutdownRestoreTicketStore(),
            () => _deviceSession.DeviceId,
            () => _deviceSession.CurrentState,
            (target, token) =>
                _deviceSession.ApplyTargetStateAsync(target, token),
            RefreshForAutomationAsync);
        _shutdownRestore.StatusChanged += OnShutdownRestoreStatusChanged;
        _quickPanel = new QuickPanelWindow(_quickPanelViewModel);
        _quickPanel.SettingsRequested += OnSettingsRequested;
        _quickPanel.DeviceDetailsRequested += OnDeviceDetailsRequested;
        if (showRequested)
        {
            _quickPanel.ShowInTaskbar = true;
            _quickPanel.WindowStyle = WindowStyle.SingleBorderWindow;
        }

        _trayIcon = new TrayIconService(
            _quickPanel,
            "LibraTray — Yeelight Libra Pro");
        _trayIcon.PrimaryActivated += OnTrayPrimaryActivated;
        _trayIcon.MiddleClicked += OnTrayMiddleClicked;
        _trayIcon.ContextRequested += OnTrayContextRequested;
        _trayIcon.MouseWheelScrolled += OnTrayMouseWheelScrolled;
        _trayWheelTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Input,
            OnTrayWheelTimerTick,
            Dispatcher);
        _trayWheelTimer.Stop();
        _ = _trayIcon.TrySetMouseWheelEnabled(
            _settings.AdjustBrightnessWithTrayWheel);
        RegisterHotkeys();
        ConfigureWindowsLifecycleEvents();
        _trayMenu = CreateTrayMenu();
        _ = ConnectAndRestoreAsync();

        if (showRequested)
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                () =>
                {
                    ShowQuickPanel();
                    if (osdPreviewRequested && _quickPanelViewModel is not null)
                    {
                        GetOsd().ShowBrightness(
                            _quickPanelViewModel.DeviceName,
                            50);
                    }
                });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _appCancellation.Cancel();
        DisposeWindowsLifecycleEvents();
        if (_automation is not null)
        {
            _automation.StatusChanged -= OnAutomationStatusChanged;
            _automation.Dispose();
        }
        if (_shutdownRestore is not null)
        {
            _shutdownRestore.StatusChanged -= OnShutdownRestoreStatusChanged;
            _shutdownRestore.Dispose();
        }

        if (_hotkeys is not null)
        {
            _hotkeys.HotkeyPressed -= OnHotkeyPressed;
            _hotkeys.Dispose();
        }

        if (_quickPanelViewModel is not null)
        {
            _quickPanelViewModel.PresetsChanged -= OnPresetsChanged;
            _quickPanelViewModel.ManualControlRequested -=
                OnManualControlRequested;
            _quickPanelViewModel.Dispose();
        }
        if (_quickPanel is not null)
        {
            _quickPanel.SettingsRequested -= OnSettingsRequested;
            _quickPanel.DeviceDetailsRequested -= OnDeviceDetailsRequested;
        }
        if (_deviceDetailsWindow is not null)
        {
            _deviceDetailsWindow.Closed -= OnDeviceDetailsWindowClosed;
            _deviceDetailsWindow.Close();
            _deviceDetailsWindow = null;
        }
        _onScreenDisplay?.Close();
        _onScreenDisplay = null;

        if (_deviceSession is not null)
        {
            _deviceSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_trayIcon is not null)
        {
            _trayIcon.PrimaryActivated -= OnTrayPrimaryActivated;
            _trayIcon.MiddleClicked -= OnTrayMiddleClicked;
            _trayIcon.ContextRequested -= OnTrayContextRequested;
            _trayIcon.MouseWheelScrolled -= OnTrayMouseWheelScrolled;
            _trayIcon.Dispose();
        }

        _trayWheelTimer?.Stop();

        _appCancellation.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _ownsSingleInstanceMutex = false;
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: false,
            SingleInstanceMutexName);

        try
        {
            _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsSingleInstanceMutex = true;
        }

        return _ownsSingleInstanceMutex;
    }

    private void OnTrayPrimaryActivated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_quickPanel?.IsVisible == true)
        {
            _quickPanel.Hide();
        }
        else
        {
            ShowQuickPanel();
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ShowSettings();
    }

    private void OnDeviceDetailsRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ShowDeviceInfo();
    }

    private void OnTrayMiddleClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_quickPanelViewModel is not null)
        {
            _ = _quickPanelViewModel.ToggleMainPowerFromHotkeyAsync();
        }
    }

    private void OnTrayMouseWheelScrolled(
        object? sender,
        TrayMouseWheelEventArgs eventArgs)
    {
        _ = sender;
        _pendingTrayWheelSteps = Math.Clamp(
            _pendingTrayWheelSteps + eventArgs.Steps,
            -20,
            20);
        _trayWheelTimer?.Stop();
        _trayWheelTimer?.Start();
    }

    private async void OnTrayWheelTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _trayWheelTimer?.Stop();
        int steps = _pendingTrayWheelSteps;
        _pendingTrayWheelSteps = 0;
        if (steps != 0 && _quickPanelViewModel is not null)
        {
            await _quickPanelViewModel.AdjustMainBrightnessFromHotkeyAsync(steps);
            ShowBrightnessOsd();
        }
    }

    private void OnPresetsChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_settingsStore is null || _quickPanelViewModel is null)
        {
            return;
        }

        try
        {
            _settings = _settingsStore.Save(
                _settings with
                {
                    Presets = _quickPanelViewModel.Presets.ToArray(),
                });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                UiText.Get("Message.SavePresetFailed"),
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnHotkeyPressed(
        object? sender,
        GlobalHotkeyPressedEventArgs eventArgs)
    {
        _ = sender;
        await ExecuteHotkeyAsync(eventArgs.Action);
        ShowAdjustmentOsd(eventArgs.Action);
    }

    private void OnTrayContextRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_quickPanelViewModel is null)
        {
            return;
        }

        _trayMenu = CreateTrayMenu();
        _trayMenu.Placement = PlacementMode.MousePoint;
        _trayMenu.IsOpen = true;
    }

    private void ShowQuickPanel()
    {
        if (_quickPanel is null)
        {
            return;
        }

        Rect workArea = SystemParameters.WorkArea;
        _quickPanel.Left = Math.Max(
            workArea.Left,
            workArea.Right - _quickPanel.Width - 16);
        _quickPanel.Top = Math.Max(
            workArea.Top,
            workArea.Bottom - _quickPanel.Height - 16);
        _quickPanel.Show();
        _quickPanel.Activate();
    }

    private void ShowSettings()
    {
        if (_quickPanel is null || _settingsStore is null)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings)
        {
            Owner = _quickPanel,
        };
        _settingsWindow.ImportRequested += OnSettingsImportRequested;
        _settingsWindow.ExportRequested += OnSettingsExportRequested;
        try
        {
            if (_settingsWindow.ShowDialog() == true
                && _settingsWindow.SavedSettings is { } changed)
            {
                bool startupRegistrationChanged =
                    changed.WindowsAutomation.StartWithWindows
                    != _settings.WindowsAutomation.StartWithWindows;
                _settings = _settingsStore.Save(changed);
                AppearanceManager.Apply(this, _settings);
                if (!_settings.ShowOnScreenDisplay)
                {
                    _onScreenDisplay?.Hide();
                }
                if (startupRegistrationChanged)
                {
                    WindowsStartupRegistrationService.SetEnabled(
                        _settings.WindowsAutomation.StartWithWindows);
                }
                _quickPanelViewModel?.ApplySettings(_settings);
                _deviceDetailsWindow?.RefreshLocalizedText();
                _ = _trayIcon?.TrySetMouseWheelEnabled(
                    _settings.AdjustBrightnessWithTrayWheel);
                _automation?.Configure(_settings.WindowsAutomation);
                _shutdownRestore?.Configure(_settings.WindowsAutomation);
                ConfigureWindowsLifecycleEvents();
                RegisterHotkeys();
                _trayMenu = CreateTrayMenu();
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or SecurityException)
        {
            MessageBox.Show(
                UiText.Get("Message.SaveSettingsFailed"),
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (_settingsWindow is not null)
            {
                _settingsWindow.ImportRequested -= OnSettingsImportRequested;
                _settingsWindow.ExportRequested -= OnSettingsExportRequested;
            }

            _settingsWindow = null;
        }
    }

    private void OnUserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_settings.Theme != AppTheme.System)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() => AppearanceManager.Apply(this, _settings));
    }

    private void OnSettingsImportRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_settingsWindow is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            AddExtension = true,
            CheckFileExists = true,
            DefaultExt = LibraTraySettingsTransferService.FileExtension,
            Filter = UiText.Get("Message.ImportFilter"),
            Multiselect = false,
            Title = UiText.Get("Message.ImportTitle"),
        };
        if (dialog.ShowDialog(_settingsWindow) != true)
        {
            return;
        }

        try
        {
            LibraTraySettings imported =
                LibraTraySettingsTransferService.Import(dialog.FileName);
            MessageBoxResult confirmation = MessageBox.Show(
                _settingsWindow,
                BuildImportPreview(imported),
                UiText.Get("Message.ImportConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirmation == MessageBoxResult.Yes)
            {
                _settingsWindow.LoadImportedSettings(imported);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            MessageBox.Show(
                _settingsWindow,
                UiText.Get("Message.ImportFailed"),
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnSettingsExportRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_settingsWindow is null
            || !_settingsWindow.TryCreatePendingSettings(
                out LibraTraySettings? pending)
            || pending is null)
        {
            return;
        }

        MessageBoxResult privacyConfirmation = MessageBox.Show(
            _settingsWindow,
            UiText.Get("Message.ExportPrivacy"),
            UiText.Get("Message.ExportPrivacyTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (privacyConfirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = LibraTraySettingsTransferService.FileExtension,
            FileName = $"LibraTray-settings-{DateTime.Now:yyyyMMdd}",
            Filter = UiText.Get("Message.ExportFilter"),
            OverwritePrompt = true,
            Title = UiText.Get("Message.ExportTitle"),
        };
        if (dialog.ShowDialog(_settingsWindow) != true)
        {
            return;
        }

        try
        {
            LibraTraySettingsTransferService.Export(dialog.FileName, pending);
            MessageBox.Show(
                _settingsWindow,
                UiText.Get("Message.ExportSucceeded"),
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or SecurityException
                or InvalidOperationException)
        {
            MessageBox.Show(
                _settingsWindow,
                UiText.Get("Message.ExportFailed"),
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string BuildImportPreview(LibraTraySettings settings)
    {
        int automationCount = 0;
        if (settings.WindowsAutomation.LockAndUnlockEnabled)
        {
            automationCount++;
        }

        if (settings.WindowsAutomation.DisplayPowerEnabled)
        {
            automationCount++;
        }

        if (settings.WindowsAutomation.ShutdownAndStartupEnabled)
        {
            automationCount++;
        }

        return UiText.Format(
            "Message.ImportPreview",
            UiText.Get(settings.UserAlias is null ? "Message.NotSet" : "Message.Set"),
            settings.Presets.Count,
            automationCount,
            UiText.Get(settings.WindowsAutomation.StartWithWindows
                ? "Message.Enabled"
                : "Message.Disabled"),
            DescribeTheme(settings.Theme),
            DescribeLanguage(settings.Language));
    }

    private static string DescribeTheme(AppTheme theme) => UiText.Get(
        theme switch
        {
            AppTheme.Light => "Text.ThemeLight",
            AppTheme.Dark => "Text.ThemeDark",
            _ => "Text.ThemeSystem",
        });

    private static string DescribeLanguage(AppLanguage language) => UiText.Get(
        language switch
        {
            AppLanguage.ChineseSimplified => "Text.LanguageChinese",
            AppLanguage.English => "Text.LanguageEnglish",
            _ => "Text.LanguageSystem",
        });

    private void RegisterHotkeys()
    {
        if (_quickPanel is null)
        {
            return;
        }

        if (_hotkeys is not null)
        {
            _hotkeys.HotkeyPressed -= OnHotkeyPressed;
            _hotkeys.Dispose();
        }

        _hotkeys = new GlobalHotkeyService(_quickPanel, _settings.Hotkeys);
        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        _quickPanelViewModel?.SetHotkeyRegistrationFailures(
            _hotkeys.RegistrationFailures
                .Select(failure => failure.Definition.DisplayText)
                .ToArray());
    }

    private void ConfigureWindowsLifecycleEvents()
    {
        if (_quickPanel is null)
        {
            return;
        }

        if (!_settings.WindowsAutomation.IsAnyEnabled)
        {
            DisposeWindowsLifecycleEvents();
            _quickPanelViewModel?.SetAutomationStatus(
                _settings.WindowsAutomation.ShutdownAndStartupEnabled
                    ? UiText.Get("Message.AutomationShutdownEnabled")
                    : null);
            return;
        }

        if (_lifecycleEvents is not null)
        {
            _quickPanelViewModel?.SetAutomationStatus(
                UiText.Get("Message.AutomationEnabled"));
            return;
        }

        try
        {
            _lifecycleEvents = new WindowsLifecycleEventService(_quickPanel);
            _lifecycleEvents.LifecycleEvent += OnWindowsLifecycleEvent;
            _quickPanelViewModel?.SetAutomationStatus(
                UiText.Get("Message.AutomationEnabled"));
        }
        catch (Win32Exception)
        {
            _quickPanelViewModel?.SetAutomationStatus(
                UiText.Get("Message.AutomationUnavailable"));
        }
    }

    private void DisposeWindowsLifecycleEvents()
    {
        if (_lifecycleEvents is null)
        {
            return;
        }

        _lifecycleEvents.LifecycleEvent -= OnWindowsLifecycleEvent;
        _lifecycleEvents.Dispose();
        _lifecycleEvents = null;
    }

    private void OnManualControlRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _automation?.RecordManualControl();
        _shutdownRestore?.RecordManualControl();
    }

    private void OnWindowsLifecycleEvent(
        object? sender,
        WindowsLifecycleEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.Kind == WindowsLifecycleEventKind.SessionEnding)
        {
            PrepareForConfirmedSessionEnding();
        }
        else if (_automation is not null)
        {
            _ = _automation.HandleAsync(
                eventArgs.Kind,
                _appCancellation.Token);
        }
    }

    private void PrepareForConfirmedSessionEnding()
    {
        if (_shutdownRestore is null
            || !_settings.WindowsAutomation.ShutdownAndStartupEnabled)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(3));
        try
        {
            _shutdownRestore.PrepareForShutdownAsync(
                    _automation?.PendingRestoreSnapshot,
                    timeout.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
            // Windows shutdown must continue even when the lamp is offline.
        }
    }

    private void OnAutomationStatusChanged(
        object? sender,
        WindowsAutomationStatusEventArgs eventArgs)
    {
        _ = sender;
        string? status = eventArgs.Outcome switch
        {
            WindowsAutomationOutcome.StateCaptured =>
                UiText.Get("Message.AutomationCaptured"),
            WindowsAutomationOutcome.LightsTurnedOff =>
                UiText.Format(
                    "Message.AutomationLightsOff",
                    DescribeTrigger(eventArgs.Trigger)),
            WindowsAutomationOutcome.StateAlreadyCurrent =>
                UiText.Get("Message.AutomationAlreadyCurrent"),
            WindowsAutomationOutcome.StateRestored =>
                UiText.Get("Message.AutomationRestored"),
            WindowsAutomationOutcome.SkippedRecentManualControl =>
                UiText.Get("Message.AutomationSkippedManual"),
            WindowsAutomationOutcome.SkippedNewerState =>
                UiText.Get("Message.AutomationSkippedNewer"),
            WindowsAutomationOutcome.Failed =>
                UiText.Get("Message.AutomationFailed"),
            _ => null,
        };
        if (status is not null)
        {
            _ = Dispatcher.BeginInvoke(
                () => _quickPanelViewModel?.SetAutomationStatus(status));
        }
    }

    private void OnShutdownRestoreStatusChanged(
        object? sender,
        ShutdownRestoreStatusEventArgs eventArgs)
    {
        _ = sender;
        string? status = eventArgs.Outcome switch
        {
            ShutdownRestoreOutcome.TicketSaved =>
                UiText.Get("Message.ShutdownTicketSaved"),
            ShutdownRestoreOutcome.StateAlreadyCurrent =>
                UiText.Get("Message.ShutdownAlreadyCurrent"),
            ShutdownRestoreOutcome.StateRestored =>
                UiText.Get("Message.ShutdownRestored"),
            ShutdownRestoreOutcome.SkippedDifferentDevice =>
                UiText.Get("Message.ShutdownDifferentDevice"),
            ShutdownRestoreOutcome.SkippedExpired =>
                UiText.Get("Message.ShutdownExpired"),
            ShutdownRestoreOutcome.SkippedNewerState =>
                UiText.Get("Message.ShutdownNewerState"),
            ShutdownRestoreOutcome.SkippedManualControl =>
                UiText.Get("Message.ShutdownManual"),
            ShutdownRestoreOutcome.Failed =>
                UiText.Get("Message.ShutdownFailed"),
            _ => null,
        };
        if (status is not null)
        {
            _ = Dispatcher.BeginInvoke(
                () => _quickPanelViewModel?.SetAutomationStatus(status));
        }
    }

    private async Task ConnectAndRestoreAsync()
    {
        try
        {
            await ConnectAndRestoreCoreAsync();
        }
        catch (OperationCanceledException) when (_appCancellation.IsCancellationRequested)
        {
        }
    }

    private async Task ConnectAndRestoreCoreAsync()
    {
        if (_quickPanelViewModel is null || _deviceSession is null)
        {
            return;
        }

        bool pendingRestore =
            _settings.WindowsAutomation.ShutdownAndStartupEnabled
            && _shutdownRestore?.HasPendingRestore == true;
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        do
        {
            await _quickPanelViewModel.ConnectAsync(_appCancellation.Token);
            if (_deviceSession.CurrentState is not null || !pendingRestore)
            {
                break;
            }

            _quickPanelViewModel.SetAutomationStatus(
                UiText.Get("Message.WaitingRestore"));
            await Task.Delay(
                TimeSpan.FromSeconds(3),
                _appCancellation.Token);
        }
        while (DateTimeOffset.UtcNow < deadline);

        if (_deviceSession.CurrentState is not null && pendingRestore)
        {
            await _shutdownRestore!.TryRestoreAfterStartupAsync(
                _appCancellation.Token);
        }
    }

    private async Task<LibraProState?> RefreshForAutomationAsync(
        CancellationToken cancellationToken)
    {
        if (_deviceSession is null)
        {
            return null;
        }

        if (_deviceSession.CurrentState is null)
        {
            await _deviceSession.DiscoverAndConnectAsync(
                cancellationToken: cancellationToken);
            return _deviceSession.CurrentState;
        }

        return await _deviceSession.RefreshAsync(cancellationToken);
    }

    private static string DescribeTrigger(WindowsLifecycleEventKind trigger) =>
        trigger switch
        {
            WindowsLifecycleEventKind.SessionLocked =>
                UiText.Get("Message.TriggerLocked"),
            WindowsLifecycleEventKind.DisplayOff =>
                UiText.Get("Message.TriggerDisplayOff"),
            _ => UiText.Get("Message.TriggerChanged"),
        };

    private ContextMenu CreateTrayMenu()
    {
        QuickPanelViewModel? viewModel = _quickPanelViewModel;
        var openItem = new MenuItem
        {
            Header = UiText.Get("Tray.Open"),
            FontWeight = FontWeights.SemiBold,
        };
        openItem.Click += (_, _) => ShowQuickPanel();

        var statusItem = new MenuItem
        {
            Header = viewModel is null
                ? UiText.Get("Tray.NotConnected")
                : $"{viewModel.DeviceName} · {viewModel.ConnectionStatus}",
            IsEnabled = false,
        };

        var mainPowerItem = new MenuItem
        {
            Header = UiText.Get("Text.MainLight"),
            IsCheckable = true,
            IsChecked = viewModel?.MainPower == true,
            IsEnabled = viewModel?.CanControl == true,
        };
        mainPowerItem.Click += async (_, _) =>
        {
            if (viewModel is not null)
            {
                await viewModel.SetMainPowerAsync(!viewModel.MainPower);
            }
        };

        var backgroundPowerItem = new MenuItem
        {
            Header = UiText.Get("Text.AmbientLight"),
            IsCheckable = true,
            IsChecked = viewModel?.BackgroundPower == true,
            IsEnabled = viewModel?.CanControl == true,
        };
        backgroundPowerItem.Click += async (_, _) =>
        {
            if (viewModel is not null)
            {
                await viewModel.SetBackgroundPowerAsync(
                    !viewModel.BackgroundPower);
            }
        };

        var allOnItem = new MenuItem
        {
            Header = UiText.Get("Tray.AllOn"),
            IsEnabled = viewModel?.CanControl == true,
        };
        allOnItem.Click += async (_, _) =>
        {
            if (viewModel is not null)
            {
                await viewModel.SetAllPowerAsync(enabled: true);
            }
        };

        var allOffItem = new MenuItem
        {
            Header = UiText.Get("Tray.AllOff"),
            IsEnabled = viewModel?.CanControl == true,
        };
        allOffItem.Click += async (_, _) =>
        {
            if (viewModel is not null)
            {
                await viewModel.SetAllPowerAsync(enabled: false);
            }
        };

        var presetsItem = new MenuItem { Header = UiText.Get("Tray.Presets") };
        if (viewModel is null || viewModel.Presets.Count == 0)
        {
            presetsItem.Items.Add(
                new MenuItem
                {
                    Header = UiText.Get("Tray.NoPresets"),
                    IsEnabled = false,
                });
        }
        else
        {
            foreach (LibraProPreset preset in viewModel.Presets)
            {
                var presetItem = new MenuItem
                {
                    Header = preset.Name,
                    IsEnabled = viewModel.CanControl,
                };
                presetItem.Click += async (_, _) =>
                    await viewModel.ApplyPresetAsync(preset);
                presetsItem.Items.Add(presetItem);
            }
        }

        var refreshItem = new MenuItem
        {
            Header = viewModel?.IsDeviceOnline == true
                ? UiText.Get("Tray.Refresh")
                : UiText.Get("Tray.Reconnect"),
            IsEnabled = viewModel?.IsBusy != true,
        };
        refreshItem.Click += async (_, _) =>
        {
            if (viewModel is not null)
            {
                await viewModel.RefreshStateAsync();
            }
        };

        var settingsItem = new MenuItem { Header = UiText.Get("Text.Settings") };
        settingsItem.Click += (_, _) => ShowSettings();

        var deviceInfoItem = new MenuItem
        {
            Header = UiText.Get("Tray.DeviceInfo"),
        };
        deviceInfoItem.Click += (_, _) => ShowDeviceInfo();

        var exitItem = new MenuItem
        {
            Header = UiText.Get("Tray.Exit"),
        };
        exitItem.Click += (_, _) =>
        {
            _quickPanel?.PrepareForShutdown();
            Shutdown();
        };

        var menu = new ContextMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(statusItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(mainPowerItem);
        menu.Items.Add(backgroundPowerItem);
        menu.Items.Add(allOnItem);
        menu.Items.Add(allOffItem);
        menu.Items.Add(presetsItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(refreshItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(deviceInfoItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private void ShowDeviceInfo()
    {
        if (_deviceSession is null || _quickPanelViewModel is null)
        {
            return;
        }

        if (_deviceDetailsWindow is not null)
        {
            _deviceDetailsWindow.Activate();
            return;
        }

        _deviceDetailsWindow = new DeviceDetailsWindow(
            _deviceSession,
            _quickPanelViewModel);
        if (_quickPanel?.IsVisible == true)
        {
            _deviceDetailsWindow.Owner = _quickPanel;
        }
        _deviceDetailsWindow.Closed += OnDeviceDetailsWindowClosed;
        _deviceDetailsWindow.Show();
    }

    private void OnDeviceDetailsWindowClosed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_deviceDetailsWindow is not null)
        {
            _deviceDetailsWindow.Closed -= OnDeviceDetailsWindowClosed;
            _deviceDetailsWindow = null;
        }
    }

    private Task ExecuteHotkeyAsync(GlobalHotkeyAction action)
    {
        if (_quickPanelViewModel is null)
        {
            return Task.CompletedTask;
        }

        return action switch
        {
            GlobalHotkeyAction.ToggleMainPower =>
                _quickPanelViewModel.ToggleMainPowerFromHotkeyAsync(),
            GlobalHotkeyAction.ToggleBackgroundPower =>
                _quickPanelViewModel.ToggleBackgroundPowerFromHotkeyAsync(),
            GlobalHotkeyAction.IncreaseMainBrightness =>
                _quickPanelViewModel.AdjustMainBrightnessFromHotkeyAsync(1),
            GlobalHotkeyAction.DecreaseMainBrightness =>
                _quickPanelViewModel.AdjustMainBrightnessFromHotkeyAsync(-1),
            GlobalHotkeyAction.IncreaseMainColorTemperature =>
                _quickPanelViewModel
                    .AdjustMainColorTemperatureFromHotkeyAsync(1),
            GlobalHotkeyAction.DecreaseMainColorTemperature =>
                _quickPanelViewModel
                    .AdjustMainColorTemperatureFromHotkeyAsync(-1),
            _ => Task.CompletedTask,
        };
    }

    private void ShowAdjustmentOsd(GlobalHotkeyAction action)
    {
        switch (action)
        {
            case GlobalHotkeyAction.IncreaseMainBrightness:
            case GlobalHotkeyAction.DecreaseMainBrightness:
                ShowBrightnessOsd();
                break;
            case GlobalHotkeyAction.IncreaseMainColorTemperature:
            case GlobalHotkeyAction.DecreaseMainColorTemperature:
                ShowColorTemperatureOsd();
                break;
        }
    }

    private void ShowBrightnessOsd()
    {
        if (!CanShowOsd(out QuickPanelViewModel? viewModel))
        {
            return;
        }

        GetOsd().ShowBrightness(viewModel.DeviceName, viewModel.MainBrightness);
    }

    private void ShowColorTemperatureOsd()
    {
        if (!CanShowOsd(out QuickPanelViewModel? viewModel))
        {
            return;
        }

        GetOsd().ShowColorTemperature(
            viewModel.DeviceName,
            viewModel.MainColorTemperature);
    }

    private bool CanShowOsd(
        [NotNullWhen(true)] out QuickPanelViewModel? viewModel)
    {
        viewModel = _quickPanelViewModel;
        return _settings.ShowOnScreenDisplay
            && viewModel is { IsDeviceOnline: true, ErrorMessage: null }
            && !_appCancellation.IsCancellationRequested;
    }

    private OnScreenDisplayWindow GetOsd() =>
        _onScreenDisplay ??= new OnScreenDisplayWindow();
}
