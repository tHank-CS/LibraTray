using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LibraTray.App.Interop;

internal sealed class TrayIconService : IDisposable
{
    private const uint NotifyAdd = 0;
    private const uint NotifyDelete = 2;
    private const uint NotifySetVersion = 4;
    private const uint NotifyMessage = 0x00000001;
    private const uint NotifyIcon = 0x00000002;
    private const uint NotifyTip = 0x00000004;
    private const uint NotifyShowTip = 0x00000080;
    private const uint NotifyIconVersion4 = 4;
    private const uint CallbackMessage = 0x8001;
    private const int LeftButtonUp = 0x0202;
    private const int LeftButtonDoubleClick = 0x0203;
    private const int RightButtonUp = 0x0205;
    private const int ContextMenu = 0x007B;
    private const int KeyboardSelect = 0x0400;
    private const int DefaultApplicationIcon = 32512;

    private readonly HwndSource _source;
    private NotifyIconData _iconData;
    private bool _disposed;

    public TrayIconService(Window owner, string tooltip)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(tooltip);

        var helper = new WindowInteropHelper(owner);
        nint handle = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException(
                "The WPF window did not create a native message source.");
        _source.AddHook(WndProc);

        nint icon = LoadIconW(0, DefaultApplicationIcon);
        if (icon == 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows did not provide the default tray icon.");
        }

        _iconData = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = handle,
            Identifier = 1,
            Flags = NotifyMessage | NotifyIcon | NotifyTip | NotifyShowTip,
            CallbackMessage = CallbackMessage,
            IconHandle = icon,
            Tip = tooltip.Length <= 127 ? tooltip : tooltip[..127],
            Info = string.Empty,
            InfoTitle = string.Empty,
        };

        if (!ShellNotifyIconW(NotifyAdd, ref _iconData))
        {
            _source.RemoveHook(WndProc);
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows rejected the tray icon registration.");
        }

        _iconData.TimeoutOrVersion = NotifyIconVersion4;
        _ = ShellNotifyIconW(NotifySetVersion, ref _iconData);
    }

    public event EventHandler? PrimaryActivated;

    public event EventHandler? ContextRequested;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = ShellNotifyIconW(NotifyDelete, ref _iconData);
        _source.RemoveHook(WndProc);
        GC.SuppressFinalize(this);
    }

    private nint WndProc(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        _ = hwnd;
        _ = wParam;

        if (message != CallbackMessage)
        {
            return 0;
        }

        int callback = unchecked((int)lParam) & 0xFFFF;
        switch (callback)
        {
            case LeftButtonUp:
            case LeftButtonDoubleClick:
            case KeyboardSelect:
                PrimaryActivated?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
            case RightButtonUp:
            case ContextMenu:
                ContextRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint WindowHandle;
        public uint Identifier;
        public uint Flags;
        public uint CallbackMessage;
        public nint IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIconHandle;
    }

    [DllImport(
        "shell32.dll",
        EntryPoint = "Shell_NotifyIconW",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SuppressMessage(
        "Interoperability",
        "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
        Justification = "The fixed NOTIFYICONDATA layout is simpler and audited with classic marshalling.")]
    private static extern bool ShellNotifyIconW(
        uint message,
        ref NotifyIconData data);

    [DllImport(
        "user32.dll",
        EntryPoint = "LoadIconW",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SuppressMessage(
        "Interoperability",
        "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
        Justification = "This loads a shared Windows system icon and uses no managed string marshalling.")]
    private static extern nint LoadIconW(nint instance, nint iconName);
}
