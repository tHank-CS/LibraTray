using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LibraTray.Core.Configuration;

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
    Shift = 0x0004,
    Windows = 0x0008,
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

    private readonly nint _windowHandle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, GlobalHotkeyDefinition> _registered = [];
    private bool _disposed;

    public GlobalHotkeyService(
        Window owner,
        GlobalHotkeySettings settings)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(settings);
        _windowHandle = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_windowHandle)
            ?? throw new InvalidOperationException(
                "The WPF window did not create a native message source.");
        _source.AddHook(WndProc);

        var failures = new List<GlobalHotkeyRegistrationFailure>();
        foreach ((int id, GlobalHotkeyAction action, string gesture) in
            EnumerateSettings(settings))
        {
            if (!TryCreateDefinition(id, action, gesture, out var definition))
            {
                failures.Add(
                    new GlobalHotkeyRegistrationFailure(
                        new GlobalHotkeyDefinition(
                            id,
                            action,
                            GlobalHotkeyModifiers.NoRepeat,
                            Key.None,
                            gesture),
                        87));
                continue;
            }

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

    internal static bool TryNormalizeGesture(
        string gesture,
        [NotNullWhen(true)] out string? normalized)
    {
        bool parsed = TryCreateDefinition(
            0,
            GlobalHotkeyAction.ToggleMainPower,
            gesture,
            out GlobalHotkeyDefinition? definition);
        normalized = definition?.DisplayText;
        return parsed;
    }

    public event EventHandler<GlobalHotkeyPressedEventArgs>? HotkeyPressed;

    public IReadOnlyList<GlobalHotkeyRegistrationFailure> RegistrationFailures
    {
        get;
    }

    private static IEnumerable<(
        int Id,
        GlobalHotkeyAction Action,
        string Gesture)> EnumerateSettings(GlobalHotkeySettings settings)
    {
        yield return (0x4C01, GlobalHotkeyAction.ToggleMainPower, settings.ToggleMainPower);
        yield return (0x4C02, GlobalHotkeyAction.ToggleBackgroundPower, settings.ToggleBackgroundPower);
        yield return (0x4C03, GlobalHotkeyAction.IncreaseMainBrightness, settings.IncreaseMainBrightness);
        yield return (0x4C04, GlobalHotkeyAction.DecreaseMainBrightness, settings.DecreaseMainBrightness);
        yield return (0x4C05, GlobalHotkeyAction.IncreaseMainColorTemperature, settings.IncreaseMainColorTemperature);
        yield return (0x4C06, GlobalHotkeyAction.DecreaseMainColorTemperature, settings.DecreaseMainColorTemperature);
    }

    private static bool TryCreateDefinition(
        int id,
        GlobalHotkeyAction action,
        string gesture,
        [NotNullWhen(true)] out GlobalHotkeyDefinition? definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        KeyGesture parsed;
        try
        {
            var converter = new KeyGestureConverter();
            if (converter.ConvertFromInvariantString(gesture.Trim())
                    is not KeyGesture converted)
            {
                return false;
            }

            parsed = converted;
        }
        catch (Exception exception) when (
            exception is FormatException or NotSupportedException)
        {
            return false;
        }

        if (parsed.Key == Key.None || parsed.Modifiers == ModifierKeys.None)
        {
            return false;
        }

        GlobalHotkeyModifiers modifiers = GlobalHotkeyModifiers.NoRepeat;
        if (parsed.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= GlobalHotkeyModifiers.Alt;
        }

        if (parsed.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= GlobalHotkeyModifiers.Control;
        }

        if (parsed.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= GlobalHotkeyModifiers.Shift;
        }

        if (parsed.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= GlobalHotkeyModifiers.Windows;
        }

        definition = new GlobalHotkeyDefinition(
            id,
            action,
            modifiers,
            parsed.Key,
            parsed.GetDisplayStringForCulture(
                System.Globalization.CultureInfo.InvariantCulture));
        return true;
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
