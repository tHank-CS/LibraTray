using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LibraTray.App.Interop;
using LibraTray.App.Presentation;
using LibraTray.Core.Configuration;

namespace LibraTray.App;

public partial class SettingsWindow : Window
{
    private LibraTraySettings _workingSettings;

    internal SettingsWindow(LibraTraySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _workingSettings = settings;
        InitializeComponent();
        LoadAppearanceOptions();
        LoadSettings(settings);
    }

    internal event EventHandler? ImportRequested;

    internal event EventHandler? ExportRequested;

    internal LibraTraySettings? SavedSettings { get; private set; }

    internal void LoadSettings(LibraTraySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _workingSettings = settings;

        AliasTextBox.Text = settings.UserAlias ?? string.Empty;
        ThemeComboBox.SelectedIndex = (int)settings.Theme;
        LanguageComboBox.SelectedIndex = (int)settings.Language;
        ShowOsdCheckBox.IsChecked = settings.ShowOnScreenDisplay;
        BrightnessStepTextBox.Text = settings.BrightnessStep.ToString(
            CultureInfo.InvariantCulture);
        BackgroundBrightnessStepTextBox.Text =
            settings.BackgroundBrightnessStep.ToString(
                CultureInfo.InvariantCulture);
        TemperatureStepTextBox.Text = settings.ColorTemperatureStep.ToString(
            CultureInfo.InvariantCulture);
        ExtremeColorTemperatureCheckBox.IsChecked =
            settings.AllowExtremeColorTemperature;
        ExperimentalSegmentRgbCheckBox.IsChecked =
            settings.ExperimentalSegmentRgbEnabled;
        TrayWheelCheckBox.IsChecked =
            settings.AdjustBrightnessWithTrayWheel;
        LockAutomationCheckBox.IsChecked =
            settings.WindowsAutomation.LockAndUnlockEnabled;
        StartWithWindowsCheckBox.IsChecked =
            settings.WindowsAutomation.StartWithWindows;
        TurnOnLightsAfterStartupCheckBox.IsChecked =
            settings.WindowsAutomation.TurnOnLightsAfterWindowsStartup;
        ShutdownAutomationCheckBox.IsChecked =
            settings.WindowsAutomation.ShutdownAndStartupEnabled;
        DisplayAutomationCheckBox.IsChecked =
            settings.WindowsAutomation.DisplayPowerEnabled;
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
        SavedSettings = null;
        ValidationTextBlock.Visibility = Visibility.Collapsed;
    }

    internal void LoadImportedSettings(LibraTraySettings settings)
    {
        LoadSettings(settings);
        Title = UiText.Get("Message.SettingsImportedPending");
    }

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
            ShowValidation(UiText.Get("Message.HotkeyModifierRequired"));
            return;
        }

        var gesture = new KeyGesture(key, modifiers);
        string display = gesture.GetDisplayStringForCulture(
            CultureInfo.InvariantCulture);
        if (!GlobalHotkeyService.TryNormalizeGesture(
                display,
                out string? normalized))
        {
            ShowValidation(UiText.Get("Message.HotkeyUnavailable"));
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
        if (!TryCreatePendingSettings(out LibraTraySettings? settings))
        {
            return;
        }

        SavedSettings = settings;
        DialogResult = true;
    }

    internal bool TryCreatePendingSettings(
        out LibraTraySettings? settings)
    {
        settings = null;
        if (!TryReadInteger(
                BrightnessStepTextBox,
                1,
                25,
                UiText.Get("Message.BrightnessStepLabel"),
                out int brightnessStep)
            || !TryReadInteger(
                BackgroundBrightnessStepTextBox,
                1,
                100,
                UiText.Get("Message.AmbientBrightnessStepLabel"),
                out int backgroundBrightnessStep)
            || !TryReadInteger(
                TemperatureStepTextBox,
                50,
                1_000,
                UiText.Get("Message.TemperatureStepLabel"),
                out int temperatureStep))
        {
            return false;
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
                ShowValidation(UiText.Get("Message.HotkeyInvalid"));
                hotkeyInputs[index].Focus();
                return false;
            }

            if (!uniqueGestures.Add(gesture))
            {
                ShowValidation(UiText.Get("Message.HotkeyDuplicate"));
                hotkeyInputs[index].Focus();
                return false;
            }

            gestures[index] = gesture;
        }

        settings = _workingSettings with
        {
            UserAlias = AliasTextBox.Text,
            BrightnessStep = brightnessStep,
            BackgroundBrightnessStep = backgroundBrightnessStep,
            ColorTemperatureStep = temperatureStep,
            AllowExtremeColorTemperature =
                ExtremeColorTemperatureCheckBox.IsChecked == true,
            ExperimentalSegmentRgbEnabled =
                ExperimentalSegmentRgbCheckBox.IsChecked == true,
            AdjustBrightnessWithTrayWheel = TrayWheelCheckBox.IsChecked == true,
            Theme = (AppTheme)Math.Max(0, ThemeComboBox.SelectedIndex),
            Language = (AppLanguage)Math.Max(0, LanguageComboBox.SelectedIndex),
            ShowOnScreenDisplay = ShowOsdCheckBox.IsChecked == true,
            WindowsAutomation = _workingSettings.WindowsAutomation with
            {
                LockAndUnlockEnabled =
                    LockAutomationCheckBox.IsChecked == true,
                StartWithWindows =
                    StartWithWindowsCheckBox.IsChecked == true,
                TurnOnLightsAfterWindowsStartup =
                    StartWithWindowsCheckBox.IsChecked == true
                    && TurnOnLightsAfterStartupCheckBox.IsChecked == true,
                ShutdownAndStartupEnabled =
                    ShutdownAutomationCheckBox.IsChecked == true,
                DisplayPowerEnabled =
                    DisplayAutomationCheckBox.IsChecked == true,
            },
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
        return true;
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ImportRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ExportRequested?.Invoke(this, EventArgs.Empty);
    }

    private void WindowHeader_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _ = sender;
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close();
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

        ShowValidation(UiText.Format(
            "Message.RangeError",
            label,
            minimum,
            maximum));
        input.Focus();
        return false;
    }

    private void ShowValidation(string message)
    {
        ValidationTextBlock.Text = message;
        ValidationTextBlock.Visibility = Visibility.Visible;
    }

    private void LoadAppearanceOptions()
    {
        ThemeComboBox.ItemsSource = new[]
        {
            UiText.Get("Text.ThemeSystem"),
            UiText.Get("Text.ThemeLight"),
            UiText.Get("Text.ThemeDark"),
        };
        LanguageComboBox.ItemsSource = new[]
        {
            UiText.Get("Text.LanguageSystem"),
            UiText.Get("Text.LanguageChinese"),
            UiText.Get("Text.LanguageEnglish"),
        };
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
