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

        if (!TryAcquireSingleInstance())
        {
            MessageBox.Show(
                "LibraTray 已在运行。请使用系统托盘图标打开控制面板。",
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        bool showRequested = e.Args.Contains(
            "--show",
            StringComparer.OrdinalIgnoreCase)
            || Environment.GetCommandLineArgs()
                .Skip(1)
                .Contains("--show", StringComparer.OrdinalIgnoreCase);
        _settingsStore = new LibraTraySettingsStore();
        _settings = _settingsStore.Load();
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
                ShowQuickPanel);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
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

    private void OnTrayWheelTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _trayWheelTimer?.Stop();
        int steps = _pendingTrayWheelSteps;
        _pendingTrayWheelSteps = 0;
        if (steps != 0 && _quickPanelViewModel is not null)
        {
            _ = _quickPanelViewModel.AdjustMainBrightnessFromHotkeyAsync(steps);
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
                "预设无法保存，请确认当前用户对本地应用数据目录具有写入权限。",
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnHotkeyPressed(
        object? sender,
        GlobalHotkeyPressedEventArgs eventArgs)
    {
        _ = sender;
        _ = ExecuteHotkeyAsync(eventArgs.Action);
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
        try
        {
            if (_settingsWindow.ShowDialog() == true
                && _settingsWindow.SavedSettings is { } changed)
            {
                bool startupRegistrationChanged =
                    changed.WindowsAutomation.StartWithWindows
                    != _settings.WindowsAutomation.StartWithWindows;
                _settings = _settingsStore.Save(changed);
                if (startupRegistrationChanged)
                {
                    WindowsStartupRegistrationService.SetEnabled(
                        _settings.WindowsAutomation.StartWithWindows);
                }
                _quickPanelViewModel?.ApplySettings(_settings);
                _ = _trayIcon?.TrySetMouseWheelEnabled(
                    _settings.AdjustBrightnessWithTrayWheel);
                _automation?.Configure(_settings.WindowsAutomation);
                _shutdownRestore?.Configure(_settings.WindowsAutomation);
                ConfigureWindowsLifecycleEvents();
                RegisterHotkeys();
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or SecurityException)
        {
            MessageBox.Show(
                "设置或开机启动项无法保存，请确认当前用户具有写入权限。",
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _settingsWindow = null;
        }
    }

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
                    ? "Windows 自动化已启用 · 关机恢复采用一次性凭据"
                    : null);
            return;
        }

        if (_lifecycleEvents is not null)
        {
            return;
        }

        try
        {
            _lifecycleEvents = new WindowsLifecycleEventService(_quickPanel);
            _lifecycleEvents.LifecycleEvent += OnWindowsLifecycleEvent;
            _quickPanelViewModel?.SetAutomationStatus(
                "Windows 自动化已启用 · 近期手动操作优先");
        }
        catch (Win32Exception)
        {
            _quickPanelViewModel?.SetAutomationStatus(
                "Windows 自动化不可用：系统事件注册失败");
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
                "Windows 自动化：已保存恢复状态",
            WindowsAutomationOutcome.LightsTurnedOff =>
                $"Windows 自动化：{DescribeTrigger(eventArgs.Trigger)}，灯光已关闭",
            WindowsAutomationOutcome.StateAlreadyCurrent =>
                "Windows 自动化：设备已处于预期状态",
            WindowsAutomationOutcome.StateRestored =>
                "Windows 自动化：已复读并恢复先前状态",
            WindowsAutomationOutcome.SkippedRecentManualControl =>
                "Windows 自动化：检测到近期手动操作，已跳过",
            WindowsAutomationOutcome.SkippedNewerState =>
                "Windows 自动化：发现更新的设备状态，未覆盖",
            WindowsAutomationOutcome.Failed =>
                "Windows 自动化未完成；设备状态未被盲目覆盖",
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
                "Windows 自动化：关灯已确认，下次启动可恢复",
            ShutdownRestoreOutcome.StateAlreadyCurrent =>
                "Windows 自动化：设备已处于保存状态",
            ShutdownRestoreOutcome.StateRestored =>
                "Windows 自动化：已恢复关机前状态",
            ShutdownRestoreOutcome.SkippedDifferentDevice =>
                "Windows 自动化：设备身份不匹配，未恢复",
            ShutdownRestoreOutcome.SkippedExpired =>
                "Windows 自动化：恢复记录已过期",
            ShutdownRestoreOutcome.SkippedNewerState =>
                "Windows 自动化：设备状态已改变，未覆盖",
            ShutdownRestoreOutcome.SkippedManualControl =>
                "Windows 自动化：检测到手动操作，未恢复",
            ShutdownRestoreOutcome.Failed =>
                "Windows 自动化未完成；不会盲目恢复状态",
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
                "Windows 自动化：等待网络和灯具上线后恢复");
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
            WindowsLifecycleEventKind.SessionLocked => "Windows 已锁定",
            WindowsLifecycleEventKind.DisplayOff => "显示器已关闭",
            _ => "系统状态已变化",
        };

    private ContextMenu CreateTrayMenu()
    {
        QuickPanelViewModel? viewModel = _quickPanelViewModel;
        var openItem = new MenuItem
        {
            Header = "打开快速控制",
            FontWeight = FontWeights.SemiBold,
        };
        openItem.Click += (_, _) => ShowQuickPanel();

        var statusItem = new MenuItem
        {
            Header = viewModel is null
                ? "Yeelight Libra Pro · 尚未连接"
                : $"{viewModel.DeviceName} · {viewModel.ConnectionStatus}",
            IsEnabled = false,
        };

        var mainPowerItem = new MenuItem
        {
            Header = "主灯",
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
            Header = "氛围灯",
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
            Header = "全部开启",
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
            Header = "全部关闭",
            IsEnabled = viewModel?.CanControl == true,
        };
        allOffItem.Click += async (_, _) =>
        {
            if (viewModel is not null)
            {
                await viewModel.SetAllPowerAsync(enabled: false);
            }
        };

        var presetsItem = new MenuItem { Header = "预设" };
        if (viewModel is null || viewModel.Presets.Count == 0)
        {
            presetsItem.Items.Add(
                new MenuItem
                {
                    Header = "尚未保存预设",
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
                ? "刷新真实状态"
                : "重新连接",
            IsEnabled = viewModel?.IsBusy != true,
        };
        refreshItem.Click += async (_, _) =>
        {
            if (viewModel is not null)
            {
                await viewModel.RefreshStateAsync();
            }
        };

        var settingsItem = new MenuItem { Header = "设置" };
        settingsItem.Click += (_, _) => ShowSettings();

        var deviceInfoItem = new MenuItem { Header = "设备信息" };
        deviceInfoItem.Click += (_, _) => ShowDeviceInfo();

        var exitItem = new MenuItem
        {
            Header = "退出 LibraTray",
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
}
