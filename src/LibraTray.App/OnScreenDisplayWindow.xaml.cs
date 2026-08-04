using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LibraTray.App.Presentation;

namespace LibraTray.App;

[SuppressMessage(
    "Interoperability",
    "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
    Justification = "The fixed pointer-sized window-style signatures require no generated marshalling.")]
public partial class OnScreenDisplayWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const long NoActivateStyle = 0x08000000L;
    private const long ToolWindowStyle = 0x00000080L;
    private const long TransparentStyle = 0x00000020L;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const double ScreenMargin = 24;

    private readonly DispatcherTimer _hideTimer;

    public OnScreenDisplayWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(1_400),
            DispatcherPriority.Background,
            OnHideTimerTick,
            Dispatcher);
        _hideTimer.Stop();
    }

    internal void ShowBrightness(string deviceName, int value)
    {
        int boundedValue = Math.Clamp(value, 1, 100);
        ShowValue(
            deviceName,
            UiText.Get("Text.OsdMainBrightness"),
            $"{boundedValue}%",
            boundedValue);
    }

    internal void ShowColorTemperature(string deviceName, int value)
    {
        int boundedValue = Math.Clamp(value, 2_700, 6_500);
        double progress = (boundedValue - 2_700) / 38.0;
        ShowValue(
            deviceName,
            UiText.Get("Text.OsdMainTemperature"),
            string.Create(
                CultureInfo.CurrentCulture,
                $"{boundedValue} K"),
            progress);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint handle = new WindowInteropHelper(this).Handle;
        nint styles = GetWindowLongPtr(handle, ExtendedStyleIndex);
        nint updated = styles
            | (nint)(NoActivateStyle | ToolWindowStyle | TransparentStyle);
        _ = SetWindowLongPtr(handle, ExtendedStyleIndex, updated);
    }

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer.Stop();
        base.OnClosed(e);
    }

    private void ShowValue(
        string deviceName,
        string metric,
        string value,
        double progress)
    {
        DeviceNameTextBlock.Text = deviceName;
        MetricTextBlock.Text = metric;
        ValueTextBlock.Text = value;
        ValueProgressBar.Value = Math.Clamp(progress, 0, 100);

        _hideTimer.Stop();
        if (!IsVisible)
        {
            Show();
        }

        PositionOnActiveMonitor();
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            PositionOnActiveMonitor);
        _hideTimer.Start();
    }

    private void PositionOnActiveMonitor()
    {
        nint monitor = GetCursorPos(out NativePoint cursor)
            ? MonitorFromPoint(cursor, MonitorDefaultToNearest)
            : 0;
        if (monitor == 0)
        {
            monitor = MonitorFromWindow(
                GetForegroundWindow(),
                MonitorDefaultToNearest);
        }

        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - ScreenMargin;
            Top = workArea.Bottom - ActualHeight - ScreenMargin;
            return;
        }

        PresentationSource? source = PresentationSource.FromVisual(this);
        Matrix transform = source?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        Point bottomRight = transform.Transform(
            new Point(monitorInfo.WorkArea.Right, monitorInfo.WorkArea.Bottom));
        Left = bottomRight.X - ActualWidth - ScreenMargin;
        Top = bottomRight.Y - ActualHeight - ScreenMargin;
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _hideTimer.Stop();
        Hide();
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SetWindowLongPtr(
        nint windowHandle,
        int index,
        nint newValue);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetCursorPos",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport(
        "user32.dll",
        EntryPoint = "MonitorFromPoint",
        ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint MonitorFromPoint(
        NativePoint point,
        uint flags);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetForegroundWindow",
        ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetForegroundWindow();

    [DllImport(
        "user32.dll",
        EntryPoint = "MonitorFromWindow",
        ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint MonitorFromWindow(
        nint windowHandle,
        uint flags);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetMonitorInfoW",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;

        public NativeRect MonitorArea;

        public NativeRect WorkArea;

        public uint Flags;
    }
}
