using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LibraTray.App.Interop;
using LibraTray.App.Presentation;
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
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIconService? _trayIcon;
    private ContextMenu? _trayMenu;

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
        _deviceSession = new LibraProDeviceSession();
        _quickPanelViewModel = new QuickPanelViewModel(
            _deviceSession,
            Dispatcher,
            _appCancellation.Token);
        _quickPanel = new QuickPanelWindow(_quickPanelViewModel);
        if (showRequested)
        {
            _quickPanel.ShowInTaskbar = true;
            _quickPanel.WindowStyle = WindowStyle.SingleBorderWindow;
        }

        _trayIcon = new TrayIconService(
            _quickPanel,
            "LibraTray — Yeelight Libra Pro");
        _trayIcon.PrimaryActivated += OnTrayPrimaryActivated;
        _trayIcon.ContextRequested += OnTrayContextRequested;
        _hotkeys = new GlobalHotkeyService(_quickPanel);
        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        _quickPanelViewModel.SetHotkeyRegistrationFailures(
            _hotkeys.RegistrationFailures
                .Select(failure => failure.Definition.DisplayText)
                .ToArray());
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

        _quickPanelViewModel?.Dispose();
        if (_deviceSession is not null)
        {
            _deviceSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_trayIcon is not null)
        {
            _trayIcon.PrimaryActivated -= OnTrayPrimaryActivated;
            _trayIcon.ContextRequested -= OnTrayContextRequested;
            _trayIcon.Dispose();
        }

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

    private void OnTrayPrimaryActivated(object? sender, EventArgs e) =>
        ShowQuickPanel();

    private void OnHotkeyPressed(
        object? sender,
        GlobalHotkeyPressedEventArgs eventArgs)
    {
        _ = sender;
        _ = ExecuteHotkeyAsync(eventArgs.Action);
    }

    private void OnTrayContextRequested(object? sender, EventArgs e)
    {
        if (_trayMenu is null)
        {
            return;
        }

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

    private ContextMenu CreateTrayMenu()
    {
        var openItem = new MenuItem
        {
            Header = "打开快速控制",
            FontWeight = FontWeights.SemiBold,
        };
        openItem.Click += (_, _) => ShowQuickPanel();

        var statusItem = new MenuItem
        {
            Header = "设备：尚未连接",
            IsEnabled = false,
        };

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
        menu.Items.Add(exitItem);
        return menu;
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
