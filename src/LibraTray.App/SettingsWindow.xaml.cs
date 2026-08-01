using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LibraTray.App.Interop;
using LibraTray.Core.Configuration;

namespace LibraTray.App;

public partial class SettingsWindow : Window
{
    private readonly LibraTraySettings _originalSettings;

    internal SettingsWindow(LibraTraySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _originalSettings = settings;
        InitializeComponent();

        AliasTextBox.Text = settings.UserAlias ?? string.Empty;
        BrightnessStepTextBox.Text = settings.BrightnessStep.ToString(
            CultureInfo.InvariantCulture);
        TemperatureStepTextBox.Text = settings.ColorTemperatureStep.ToString(
            CultureInfo.InvariantCulture);
        MainPowerHotkeyTextBox.Text = settings.Hotkeys.ToggleMainPower;
        BackgroundPowerHotkeyTextBox.Text =
            settings.Hotkeys.ToggleBackgroundPower;
        BrightnessUpHotkeyTextBox.Text =
            settings.Hotkeys.IncreaseMainBrightness;
        BrightnessDownHotkeyTextBox.Text =
            settings.Hotkeys.DecreaseMainBrightness;
        TemperatureUpHotkeyTextBox.Text =
            settings.Hotkeys.IncreaseMainColorTemperature;
        TemperatureDownHotkeyTextBox.Text =
            settings.Hotkeys.DecreaseMainColorTemperature;
    }

    internal LibraTraySettings? SavedSettings { get; private set; }

    private void HotkeyTextBox_GotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        _ = e;
        if (sender is TextBox input)
        {
            input.SelectAll();
        }
    }

    private void HotkeyTextBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (sender is not TextBox input)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (IsModifierKey(key))
        {
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.None
            && key is Key.Tab or Key.Escape)
        {
            return;
        }

        e.Handled = true;
        if (modifiers == ModifierKeys.None)
        {
            ShowValidation("快捷键必须至少包含 Ctrl、Alt、Shift 或 Win 中的一项。");
            return;
        }

        var gesture = new KeyGesture(key, modifiers);
        string display = gesture.GetDisplayStringForCulture(
            CultureInfo.InvariantCulture);
        if (!GlobalHotkeyService.TryNormalizeGesture(
                display,
                out string? normalized))
        {
            ShowValidation("该组合键无法注册，请选择其他组合。");
            return;
        }

        input.Text = normalized;
        input.SelectAll();
        ValidationTextBlock.Visibility = Visibility.Collapsed;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!TryReadInteger(
                BrightnessStepTextBox,
                1,
                25,
                "亮度步进",
                out int brightnessStep)
            || !TryReadInteger(
                TemperatureStepTextBox,
                50,
                1_000,
                "色温步进",
                out int temperatureStep))
        {
            return;
        }

        TextBox[] hotkeyInputs =
        [
            MainPowerHotkeyTextBox,
            BackgroundPowerHotkeyTextBox,
            BrightnessUpHotkeyTextBox,
            BrightnessDownHotkeyTextBox,
            TemperatureUpHotkeyTextBox,
            TemperatureDownHotkeyTextBox,
        ];
        var gestures = new string[hotkeyInputs.Length];
        var uniqueGestures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < hotkeyInputs.Length; index++)
        {
            if (!GlobalHotkeyService.TryNormalizeGesture(
                    hotkeyInputs[index].Text,
                    out string? gesture))
            {
                ShowValidation("快捷键格式无效；每项必须包含修饰键和一个按键。");
                hotkeyInputs[index].Focus();
                return;
            }

            if (!uniqueGestures.Add(gesture))
            {
                ShowValidation("快捷键不能重复。");
                hotkeyInputs[index].Focus();
                return;
            }

            gestures[index] = gesture;
        }

        SavedSettings = _originalSettings with
        {
            UserAlias = AliasTextBox.Text,
            BrightnessStep = brightnessStep,
            ColorTemperatureStep = temperatureStep,
            Hotkeys = new GlobalHotkeySettings
            {
                ToggleMainPower = gestures[0],
                ToggleBackgroundPower = gestures[1],
                IncreaseMainBrightness = gestures[2],
                DecreaseMainBrightness = gestures[3],
                IncreaseMainColorTemperature = gestures[4],
                DecreaseMainColorTemperature = gestures[5],
            },
        };
        DialogResult = true;
    }

    private bool TryReadInteger(
        TextBox input,
        int minimum,
        int maximum,
        string label,
        out int value)
    {
        if (int.TryParse(
                input.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value)
            && value >= minimum
            && value <= maximum)
        {
            return true;
        }

        ShowValidation($"{label}必须在 {minimum}–{maximum} 之间。");
        input.Focus();
        return false;
    }

    private void ShowValidation(string message)
    {
        ValidationTextBlock.Text = message;
        ValidationTextBlock.Visibility = Visibility.Visible;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftAlt
            or Key.RightAlt
            or Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftShift
            or Key.RightShift
            or Key.LWin
            or Key.RWin;
}
