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
    private const int WmQueryEndSession = 0x0011;
    private const int WmEndSession = 0x0016;
    private const int WmWtsSessionChange = 0x02B1;
    private const int WtsSessionLock = 0x7;
    private const int WtsSessionUnlock = 0x8;
    private const int PbtPowerSettingChange = 0x8013;
    private const uint NotifyForThisSession = 0;
    private const uint DeviceNotifyWindowHandle = 0;
    private const uint SpiGetScreenSaverRunning = 0x0072;
    private const uint SpiGetScreenSaverSecure = 0x0076;
    private const int WtsInfoEx = 25;
    private const uint WtsCurrentSession = 0xFFFFFFFF;
    private const int WtsSessionStateLocked = 0;
    private const int WtsSessionStateUnlocked = 1;
    // WTSINFOEX contains a 32-bit level followed by an 8-byte-aligned
    // WTSINFOEX_LEVEL1_W union on the x64-only target supported by LibraTray.
    // SessionFlags is the first field after the level-1 session identifier.
    private const int WtsInfoExSessionFlagsOffset = 16;

    private static readonly Guid ConsoleDisplayState =
        new("6fe69556-704a-47a0-8f24-c28d936fda47");

    private readonly HwndSource _source;
    private readonly nint _windowHandle;
    private nint _displayNotification;
    private readonly Timer _sessionStateTimer;
    private bool _sessionRegistered;
    private readonly WindowsSessionStateFusion _sessionStateFusion = new();
    private int _sessionPollInProgress;
    private volatile bool _monitorSessionState;
    private readonly string _shutdownReason;
    private readonly object _sessionStateSync = new();
    private bool _shutdownReasonRegistered;
    private bool _disposed;

    public WindowsLifecycleEventService(
        Window owner,
        bool monitorSessionState,
        string shutdownReason)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(shutdownReason);
        _monitorSessionState = monitorSessionState;
        _shutdownReason = shutdownReason;
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

            _sessionStateTimer = new Timer(
                _ => PollSessionState(),
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));
        }
        catch
        {
            _sessionStateTimer = null!;
            Dispose();
            throw;
        }
    }

    public event EventHandler<WindowsLifecycleEventArgs>? LifecycleEvent;

    public void SetSessionStateMonitoringEnabled(bool enabled)
    {
        _monitorSessionState = enabled;
        if (enabled)
        {
            PollSessionState();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessionStateTimer?.Dispose();
        DestroyShutdownReason();
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
        if (message == WmQueryEndSession)
        {
            _shutdownReasonRegistered = ShutdownBlockReasonCreate(
                _windowHandle,
                _shutdownReason);
            Publish(WindowsLifecycleEventKind.SessionEndingRequested);
            handled = true;
            return 1;
        }

        if (message == WmEndSession)
        {
            if (wParam != IntPtr.Zero)
            {
                Publish(WindowsLifecycleEventKind.SessionEnding);
            }
            else
            {
                Publish(WindowsLifecycleEventKind.SessionEndingCanceled);
            }

            DestroyShutdownReason();
        }
        else if (message == WmWtsSessionChange && _monitorSessionState)
        {
            int change = unchecked((int)wParam);
            if (change == WtsSessionLock)
            {
                ReconcileSessionState(forceLocked: true, explicitUnlock: false);
            }
            else if (change == WtsSessionUnlock)
            {
                ReconcileSessionState(forceLocked: false, explicitUnlock: true);
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

    private void DestroyShutdownReason()
    {
        if (_shutdownReasonRegistered)
        {
            _ = ShutdownBlockReasonDestroy(_windowHandle);
            _shutdownReasonRegistered = false;
        }
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
            if (lifecycleEvent == WindowsLifecycleEventKind.DisplayOn
                && _monitorSessionState)
            {
                ReconcileSessionState(forceLocked: null, explicitUnlock: false);
            }

            Publish(lifecycleEvent);
        }
    }

    private void PollSessionState()
    {
        if (_disposed
            || !_monitorSessionState
            || Interlocked.Exchange(ref _sessionPollInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            ReconcileSessionState(forceLocked: null, explicitUnlock: false);
        }
        finally
        {
            Interlocked.Exchange(ref _sessionPollInProgress, 0);
        }
    }

    private void ReconcileSessionState(bool? forceLocked, bool explicitUnlock)
    {
        lock (_sessionStateSync)
        {
            bool? queriedLocked = TryQuerySessionLocked();
            bool? secureScreenSaver = TryQuerySecureScreenSaverRunning();
            WindowsLifecycleEventKind? transition = _sessionStateFusion.Observe(
                queriedLocked,
                secureScreenSaver,
                forceLocked,
                explicitUnlock);
            if (transition is { } lifecycleEvent)
            {
                Publish(lifecycleEvent);
            }
        }
    }

    private static bool? TryQuerySecureScreenSaverRunning()
    {
        if (!SystemParametersInfo(
                SpiGetScreenSaverRunning,
                0,
                out bool running,
                0)
            || !SystemParametersInfo(
                SpiGetScreenSaverSecure,
                0,
                out bool secure,
                0))
        {
            return null;
        }

        return running && secure;
    }

    private static bool? TryQuerySessionLocked()
    {
        if (!WtsQuerySessionInformation(
                0,
                WtsCurrentSession,
                WtsInfoEx,
                out nint buffer,
                out int bytesReturned)
            || buffer == 0)
        {
            return null;
        }

        try
        {
            if (bytesReturned < WtsInfoExSessionFlagsOffset + sizeof(int))
            {
                return null;
            }

            if (Marshal.ReadInt32(buffer, 0) != 1)
            {
                return null;
            }

            return Marshal.ReadInt32(buffer, WtsInfoExSessionFlagsOffset) switch
            {
                WtsSessionStateLocked => true,
                WtsSessionStateUnlocked => false,
                _ => null,
            };
        }
        finally
        {
            WtsFreeMemory(buffer);
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
        "wtsapi32.dll",
        EntryPoint = "WTSQuerySessionInformationW",
        ExactSpelling = true,
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WtsQuerySessionInformation(
        nint serverHandle,
        uint sessionId,
        int infoClass,
        out nint buffer,
        out int bytesReturned);

    [DllImport(
        "wtsapi32.dll",
        EntryPoint = "WTSFreeMemory",
        ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void WtsFreeMemory(nint memory);

    [DllImport(
        "user32.dll",
        EntryPoint = "SystemParametersInfoW",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value,
        uint update);

    [DllImport(
        "user32.dll",
        EntryPoint = "ShutdownBlockReasonCreate",
        ExactSpelling = true,
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShutdownBlockReasonCreate(
        nint windowHandle,
        string reason);

    [DllImport(
        "user32.dll",
        EntryPoint = "ShutdownBlockReasonDestroy",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShutdownBlockReasonDestroy(nint windowHandle);

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
