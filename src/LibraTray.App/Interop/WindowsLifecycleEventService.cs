using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LibraTray.Core.Automation;

namespace LibraTray.App.Interop;

internal sealed class WindowsLifecycleEventService : IDisposable
{
    private const int WmPowerBroadcast = 0x0218;
    private const int WmEndSession = 0x0016;
    private const int WmWtsSessionChange = 0x02B1;
    private const int WtsSessionLock = 0x7;
    private const int WtsSessionUnlock = 0x8;
    private const int PbtPowerSettingChange = 0x8013;
    private const uint NotifyForThisSession = 0;
    private const uint DeviceNotifyWindowHandle = 0;

    private static readonly Guid ConsoleDisplayState =
        new("6fe69556-704a-47a0-8f24-c28d936fda47");

    private readonly HwndSource _source;
    private readonly nint _windowHandle;
    private nint _displayNotification;
    private bool _sessionRegistered;
    private bool _disposed;

    public WindowsLifecycleEventService(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var helper = new WindowInteropHelper(owner);
        _windowHandle = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException(
                "The WPF window did not create a native message source.");
        _source.AddHook(WndProc);

        try
        {
            if (!WtsRegisterSessionNotification(
                    _windowHandle,
                    NotifyForThisSession))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows rejected session-change registration.");
            }

            _sessionRegistered = true;
            Guid setting = ConsoleDisplayState;
            _displayNotification = RegisterPowerSettingNotification(
                _windowHandle,
                ref setting,
                DeviceNotifyWindowHandle);
            if (_displayNotification == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows rejected display-power registration.");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public event EventHandler<WindowsLifecycleEventArgs>? LifecycleEvent;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_displayNotification != 0)
        {
            _ = UnregisterPowerSettingNotification(_displayNotification);
            _displayNotification = 0;
        }

        if (_sessionRegistered)
        {
            _ = WtsUnRegisterSessionNotification(_windowHandle);
            _sessionRegistered = false;
        }

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
        _ = handled;
        if (message == WmEndSession && wParam != IntPtr.Zero)
        {
            Publish(WindowsLifecycleEventKind.SessionEnding);
        }
        else if (message == WmWtsSessionChange)
        {
            int change = unchecked((int)wParam);
            if (change == WtsSessionLock)
            {
                Publish(WindowsLifecycleEventKind.SessionLocked);
            }
            else if (change == WtsSessionUnlock)
            {
                Publish(WindowsLifecycleEventKind.SessionUnlocked);
            }
        }
        else if (message == WmPowerBroadcast)
        {
            int powerEvent = unchecked((int)wParam);
            if (powerEvent == PbtPowerSettingChange)
            {
                PublishDisplayState(lParam);
            }
        }

        return 0;
    }

    private void PublishDisplayState(nint dataPointer)
    {
        if (dataPointer == 0)
        {
            return;
        }

        PowerBroadcastSetting setting =
            Marshal.PtrToStructure<PowerBroadcastSetting>(dataPointer);
        if (setting.PowerSetting != ConsoleDisplayState
            || setting.DataLength < sizeof(int))
        {
            return;
        }

        int value = Marshal.ReadInt32(
            dataPointer,
            Marshal.SizeOf<PowerBroadcastSetting>());
        WindowsLifecycleEventKind? kind = value switch
        {
            0 => WindowsLifecycleEventKind.DisplayOff,
            1 => WindowsLifecycleEventKind.DisplayOn,
            2 => WindowsLifecycleEventKind.DisplayDimmed,
            _ => null,
        };
        if (kind is { } lifecycleEvent)
        {
            Publish(lifecycleEvent);
        }
    }

    private void Publish(WindowsLifecycleEventKind kind) =>
        LifecycleEvent?.Invoke(this, new WindowsLifecycleEventArgs(kind));

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PowerBroadcastSetting
    {
        public readonly Guid PowerSetting;
        public readonly uint DataLength;
    }

    [DllImport(
        "wtsapi32.dll",
        EntryPoint = "WTSRegisterSessionNotification",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SuppressMessage(
        "Interoperability",
        "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
        Justification = "The fixed Win32 signature is isolated in this disposable adapter.")]
    private static extern bool WtsRegisterSessionNotification(
        nint windowHandle,
        uint flags);

    [DllImport(
        "wtsapi32.dll",
        EntryPoint = "WTSUnRegisterSessionNotification",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WtsUnRegisterSessionNotification(
        nint windowHandle);

    [DllImport(
        "user32.dll",
        EntryPoint = "RegisterPowerSettingNotification",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint RegisterPowerSettingNotification(
        nint recipient,
        ref Guid powerSettingGuid,
        uint flags);

    [DllImport(
        "user32.dll",
        EntryPoint = "UnregisterPowerSettingNotification",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(
        nint handle);
}

internal sealed class WindowsLifecycleEventArgs(
    WindowsLifecycleEventKind kind) : EventArgs
{
    public WindowsLifecycleEventKind Kind { get; } = kind;
}
