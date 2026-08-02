using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LibraTray.App.Interop;
using LibraTray.App.Presentation;
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
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIconService? _trayIcon;
    private ContextMenu? _trayMenu;
    private DispatcherTimer? _trayWheelTimer;
    private int _pendingTrayWheelSteps;

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
        _quickPanel = new QuickPanelWindow(_quickPanelViewModel);
        _quickPanel.SettingsRequested += OnSettingsRequested;
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
        _trayMenu = CreateTrayMenu();
        _ = _quickPanelViewModel.ConnectAsync(_appCancellation.Token);

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
        if (_hotkeys is not null)
        {
            _hotkeys.HotkeyPressed -= OnHotkeyPressed;
            _hotkeys.Dispose();
        }

        if (_quickPanelViewModel is not null)
        {
            _quickPanelViewModel.PresetsChanged -= OnPresetsChanged;
            _quickPanelViewModel.Dispose();
        }
        if (_quickPanel is not null)
        {
            _quickPanel.SettingsRequested -= OnSettingsRequested;
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
                _settings = _settingsStore.Save(changed);
                _quickPanelViewModel?.ApplySettings(_settings);
                _ = _trayIcon?.TrySetMouseWheelEnabled(
                    _settings.AdjustBrightnessWithTrayWheel);
                RegisterHotkeys();
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                "设置无法保存，请确认当前用户对本地应用数据目录具有写入权限。",
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
        string displayName = _quickPanelViewModel?.DeviceName
            ?? "Yeelight Libra Pro";
        string? reportedName = _deviceSession?.Identity?.ReportedName;
        MessageBox.Show(
            $"设备名称：{displayName}\n"
            + "产品名称：Yeelight Libra Pro\n"
            + "硬件型号：YLTD003\n"
            + $"设备上报名：{(string.IsNullOrWhiteSpace(reportedName) ? "未提供" : reportedName)}\n"
            + $"连接状态：{_quickPanelViewModel?.ConnectionStatus ?? "未连接"}",
            "LibraTray · 设备信息",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
