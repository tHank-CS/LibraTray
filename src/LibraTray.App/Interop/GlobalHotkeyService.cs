using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace LibraTray.App.Interop;

internal enum GlobalHotkeyAction
{
    ToggleMainPower,
    ToggleBackgroundPower,
    IncreaseMainBrightness,
    DecreaseMainBrightness,
    IncreaseMainColorTemperature,
    DecreaseMainColorTemperature,
}

[Flags]
internal enum GlobalHotkeyModifiers : uint
{
    Alt = 0x0001,
    Control = 0x0002,
    NoRepeat = 0x4000,
}

internal sealed record GlobalHotkeyDefinition(
    int Id,
    GlobalHotkeyAction Action,
    GlobalHotkeyModifiers Modifiers,
    Key Key,
    string DisplayText);

internal sealed record GlobalHotkeyRegistrationFailure(
    GlobalHotkeyDefinition Definition,
    int ErrorCode);

internal sealed class GlobalHotkeyPressedEventArgs : EventArgs
{
    public GlobalHotkeyPressedEventArgs(GlobalHotkeyAction action)
    {
        Action = action;
    }

    public GlobalHotkeyAction Action { get; }
}

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private static readonly GlobalHotkeyDefinition[] DefaultDefinitions =
    [
        new(
            0x4C01,
            GlobalHotkeyAction.ToggleMainPower,
            GlobalHotkeyModifiers.Control
                | GlobalHotkeyModifiers.Alt
                | GlobalHotkeyModifiers.NoRepeat,
            Key.L,
            "Ctrl+Alt+L"),
        new(
            0x4C02,
            GlobalHotkeyAction.ToggleBackgroundPower,
            GlobalHotkeyModifiers.Control
                | GlobalHotkeyModifiers.Alt
                | GlobalHotkeyModifiers.NoRepeat,
            Key.A,
            "Ctrl+Alt+A"),
        new(
            0x4C03,
            GlobalHotkeyAction.IncreaseMainBrightness,
            GlobalHotkeyModifiers.Control
                | GlobalHotkeyModifiers.Alt
                | GlobalHotkeyModifiers.NoRepeat,
            Key.Up,
            "Ctrl+Alt+Up"),
        new(
            0x4C04,
            GlobalHotkeyAction.DecreaseMainBrightness,
            GlobalHotkeyModifiers.Control
                | GlobalHotkeyModifiers.Alt
                | GlobalHotkeyModifiers.NoRepeat,
            Key.Down,
            "Ctrl+Alt+Down"),
        new(
            0x4C05,
            GlobalHotkeyAction.IncreaseMainColorTemperature,
            GlobalHotkeyModifiers.Control
                | GlobalHotkeyModifiers.Alt
                | GlobalHotkeyModifiers.NoRepeat,
            Key.Right,
            "Ctrl+Alt+Right"),
        new(
            0x4C06,
            GlobalHotkeyAction.DecreaseMainColorTemperature,
            GlobalHotkeyModifiers.Control
                | GlobalHotkeyModifiers.Alt
                | GlobalHotkeyModifiers.NoRepeat,
            Key.Left,
            "Ctrl+Alt+Left"),
    ];

    private readonly nint _windowHandle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, GlobalHotkeyDefinition> _registered = [];
    private bool _disposed;

    public GlobalHotkeyService(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _windowHandle = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException(
                "The WPF window did not create a native message source.");
        _source.AddHook(WndProc);

        var failures = new List<GlobalHotkeyRegistrationFailure>();
        foreach (GlobalHotkeyDefinition definition in DefaultDefinitions)
        {
            int virtualKey = KeyInterop.VirtualKeyFromKey(definition.Key);
            if (RegisterHotKey(
                    _windowHandle,
                    definition.Id,
                    (uint)definition.Modifiers,
                    (uint)virtualKey))
            {
                _registered.Add(definition.Id, definition);
            }
            else
            {
                failures.Add(
                    new GlobalHotkeyRegistrationFailure(
                        definition,
                        Marshal.GetLastWin32Error()));
            }
        }

        RegistrationFailures = failures;
    }

    public event EventHandler<GlobalHotkeyPressedEventArgs>? HotkeyPressed;

    public IReadOnlyList<GlobalHotkeyRegistrationFailure> RegistrationFailures
    {
        get;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (int id in _registered.Keys)
        {
            _ = UnregisterHotKey(_windowHandle, id);
        }

        _registered.Clear();
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
        _ = lParam;
        if (message == HotkeyMessage
            && _registered.TryGetValue(
                unchecked((int)wParam),
                out GlobalHotkeyDefinition? definition))
        {
            HotkeyPressed?.Invoke(
                this,
                new GlobalHotkeyPressedEventArgs(definition.Action));
            handled = true;
        }

        return 0;
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "RegisterHotKey",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SuppressMessage(
        "Interoperability",
        "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
        Justification = "The fixed primitive RegisterHotKey signature requires no generated marshalling.")]
    private static extern bool RegisterHotKey(
        nint windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport(
        "user32.dll",
        EntryPoint = "UnregisterHotKey",
        ExactSpelling = true,
        SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SuppressMessage(
        "Interoperability",
        "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
        Justification = "The fixed primitive UnregisterHotKey signature requires no generated marshalling.")]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}
