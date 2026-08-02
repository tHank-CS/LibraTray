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
    private const int MiddleButtonUp = 0x0208;
    private const int MouseWheel = 0x020A;
    private const int RightButtonUp = 0x0205;
    private const int ContextMenu = 0x007B;
    private const int NotifySelect = 0x0400;
    private const int NotifyKeyboardSelect = 0x0401;
    private const int DefaultApplicationIcon = 32512;
    private const int LowLevelMouseHook = 14;
    private const int WheelDelta = 120;

    private readonly HwndSource _source;
    private readonly LowLevelMouseProcedure _mouseHookProcedure;
    private NotifyIconData _iconData;
    private nint _mouseHook;
    private long _suppressLeftActivationUntil;
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
        _mouseHookProcedure = MouseHookProcedure;

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

    public event EventHandler? MiddleClicked;

    public event EventHandler? ContextRequested;

    public event EventHandler<TrayMouseWheelEventArgs>? MouseWheelScrolled;

    public bool TrySetMouseWheelEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!enabled)
        {
            RemoveMouseHook();
            return true;
        }

        if (_mouseHook != 0)
        {
            return true;
        }

        _mouseHook = SetWindowsHookExW(
            LowLevelMouseHook,
            _mouseHookProcedure,
            GetModuleHandleW(null),
            0);
        return _mouseHook != 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RemoveMouseHook();
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
            case NotifySelect:
                ActivatePrimaryOnce();
                handled = true;
                break;
            case LeftButtonDoubleClick:
                _suppressLeftActivationUntil =
                    Environment.TickCount64 + GetDoubleClickTime();
                handled = true;
                break;
            case MiddleButtonUp:
                MiddleClicked?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
            case NotifyKeyboardSelect:
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

    private void ActivatePrimaryOnce()
    {
        long now = Environment.TickCount64;
        if (now < _suppressLeftActivationUntil)
        {
            return;
        }

        // The version-4 Shell callback can report both WM_LBUTTONUP and
        // NIN_SELECT for one gesture. Suppress only the immediate duplicate.
        _suppressLeftActivationUntil = now + 150;
        PrimaryActivated?.Invoke(this, EventArgs.Empty);
    }

    private nint MouseHookProcedure(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && unchecked((int)wParam) == MouseWheel)
        {
            LowLevelMouseEventData mouseEvent =
                Marshal.PtrToStructure<LowLevelMouseEventData>(lParam);
            if (IsPointOverIcon(mouseEvent.Position))
            {
                int delta = unchecked((short)(mouseEvent.MouseData >> 16));
                if (delta != 0)
                {
                    MouseWheelScrolled?.Invoke(
                        this,
                        new TrayMouseWheelEventArgs(delta / WheelDelta));
                }
            }
        }

        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private bool IsPointOverIcon(NativePoint point)
    {
        var identifier = new NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            WindowHandle = _iconData.WindowHandle,
            Identifier = _iconData.Identifier,
            ItemGuid = Guid.Empty,
        };
        return ShellNotifyIconGetRect(ref identifier, out NativeRect rectangle) == 0
            && point.X >= rectangle.Left
            && point.X < rectangle.Right
            && point.Y >= rectangle.Top
            && point.Y < rectangle.Bottom;
    }

    private void RemoveMouseHook()
    {
        if (_mouseHook == 0)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_mouseHook);
        _mouseHook = 0;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public nint WindowHandle;
        public uint Identifier;
        public Guid ItemGuid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LowLevelMouseEventData
    {
        public readonly NativePoint Position;
        public readonly uint MouseData;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInformation;
    }

    private delegate nint LowLevelMouseProcedure(
        int code,
        nint wParam,
        nint lParam);

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

    [DllImport(
        "shell32.dll",
        EntryPoint = "Shell_NotifyIconGetRect",
        ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ShellNotifyIconGetRect(
        ref NotifyIconIdentifier identifier,
        out NativeRect iconLocation);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowsHookExW",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SetWindowsHookExW(
        int hookId,
        LowLevelMouseProcedure procedure,
        nint module,
        uint threadId);

    [DllImport(
        "user32.dll",
        EntryPoint = "UnhookWindowsHookEx",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport(
        "user32.dll",
        EntryPoint = "CallNextHookEx",
        ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint CallNextHookEx(
        nint hook,
        int code,
        nint wParam,
        nint lParam);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetDoubleClickTime",
        ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetDoubleClickTime();

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleW",
        ExactSpelling = true,
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetModuleHandleW(string? moduleName);
}

internal sealed class TrayMouseWheelEventArgs(int steps) : EventArgs
{
    public int Steps { get; } = steps;
}
