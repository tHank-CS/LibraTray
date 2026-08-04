using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LibraTray.App.Presentation;

namespace LibraTray.App;

public partial class CustomColorWindow : Window
{
    private double _hue;
    private double _saturation;
    private double _value;
    private bool _isDraggingColorField;
    private bool _isDraggingHue;
    private bool _updatingText;

    internal CustomColorWindow(int initialRgb)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialRgb);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialRgb, 0xFFFFFF);
        InitializeComponent();
        ApplyRgb(initialRgb, updateText: true);
        Loaded += (_, _) =>
        {
            UpdatePickerVisuals();
            ColorField.Focus();
        };
    }

    internal int RgbValue { get; private set; }

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

    private void ColorField_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _isDraggingColorField = true;
        _ = ColorField.CaptureMouse();
        UpdateColorFieldFromPoint(e.GetPosition(ColorField));
        e.Handled = true;
    }

    private void ColorField_MouseMove(object sender, MouseEventArgs e)
    {
        _ = sender;
        if (_isDraggingColorField && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateColorFieldFromPoint(e.GetPosition(ColorField));
        }
    }

    private void HueStrip_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _isDraggingHue = true;
        _ = HueStrip.CaptureMouse();
        UpdateHueFromPoint(e.GetPosition(HueStrip));
        e.Handled = true;
    }

    private void HueStrip_MouseMove(object sender, MouseEventArgs e)
    {
        _ = sender;
        if (_isDraggingHue && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateHueFromPoint(e.GetPosition(HueStrip));
        }
    }

    private void Picker_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _ = sender;
        _isDraggingColorField = false;
        _isDraggingHue = false;
        if (Mouse.Captured is not null)
        {
            Mouse.Capture(null);
        }

        e.Handled = true;
    }

    private void ColorField_KeyDown(object sender, KeyEventArgs e)
    {
        _ = sender;
        double increment = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? 0.05
            : 0.01;
        switch (e.Key)
        {
            case Key.Left:
                _saturation = Math.Clamp(_saturation - increment, 0, 1);
                break;
            case Key.Right:
                _saturation = Math.Clamp(_saturation + increment, 0, 1);
                break;
            case Key.Up:
                _value = Math.Clamp(_value + increment, 0, 1);
                break;
            case Key.Down:
                _value = Math.Clamp(_value - increment, 0, 1);
                break;
            default:
                return;
        }

        UpdateSelectedColor(updateText: true);
        e.Handled = true;
    }

    private void HueStrip_KeyDown(object sender, KeyEventArgs e)
    {
        _ = sender;
        double increment = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? 10
            : 1;
        if (e.Key is Key.Left or Key.Down)
        {
            _hue = (_hue - increment + 360) % 360;
        }
        else if (e.Key is Key.Right or Key.Up)
        {
            _hue = (_hue + increment) % 360;
        }
        else
        {
            return;
        }

        UpdateSelectedColor(updateText: true);
        e.Handled = true;
    }

    private void Picker_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdatePickerVisuals();
    }

    private void HexTextBox_TextChanged(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_updatingText || !TryParseRgb(out int rgb))
        {
            return;
        }

        ApplyRgb(rgb, updateText: false);
        ShowExampleText();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!TryParseRgb(out int rgb))
        {
            ValidationTextBlock.Text = UiText.Get("Message.CustomColorInvalid");
            ValidationTextBlock.Foreground = (Brush)FindResource("ErrorBrush");
            HexTextBox.Focus();
            return;
        }

        RgbValue = rgb;
        DialogResult = true;
    }

    private void UpdateColorFieldFromPoint(Point point)
    {
        if (ColorField.ActualWidth <= 0 || ColorField.ActualHeight <= 0)
        {
            return;
        }

        _saturation = Math.Clamp(point.X / ColorField.ActualWidth, 0, 1);
        _value = 1 - Math.Clamp(point.Y / ColorField.ActualHeight, 0, 1);
        UpdateSelectedColor(updateText: true);
    }

    private void UpdateHueFromPoint(Point point)
    {
        if (HueStrip.ActualWidth <= 0)
        {
            return;
        }

        _hue = Math.Clamp(point.X / HueStrip.ActualWidth, 0, 1) * 360;
        if (_hue >= 360)
        {
            _hue = 0;
        }

        UpdateSelectedColor(updateText: true);
    }

    private void ApplyRgb(int rgb, bool updateText)
    {
        Color color = Color.FromRgb(
            (byte)(rgb >> 16),
            (byte)(rgb >> 8),
            (byte)rgb);
        (_hue, _saturation, _value) = ToHsv(color);
        UpdateSelectedColor(updateText);
    }

    private void UpdateSelectedColor(bool updateText)
    {
        Color color = FromHsv(_hue, _saturation, _value);
        int rgb = (color.R << 16) | (color.G << 8) | color.B;
        RgbValue = rgb;
        PreviewSwatch.Background = new SolidColorBrush(color);
        HueFieldGradientStop.Color = FromHsv(_hue, 1, 1);
        if (updateText)
        {
            _updatingText = true;
            HexTextBox.Text = $"#{rgb:X6}";
            HexTextBox.CaretIndex = HexTextBox.Text.Length;
            _updatingText = false;
        }

        ShowExampleText();
        UpdatePickerVisuals();
    }

    private void UpdatePickerVisuals()
    {
        if (ColorField.ActualWidth > 0 && ColorField.ActualHeight > 0)
        {
            Canvas.SetLeft(
                ColorFieldMarker,
                _saturation * ColorField.ActualWidth
                    - ColorFieldMarker.Width / 2);
            Canvas.SetTop(
                ColorFieldMarker,
                (1 - _value) * ColorField.ActualHeight
                    - ColorFieldMarker.Height / 2);
        }

        if (HueStrip.ActualWidth > 0)
        {
            Canvas.SetLeft(
                HueMarker,
                (_hue / 360) * HueStrip.ActualWidth - HueMarker.Width / 2);
        }
    }

    private void ShowExampleText()
    {
        ValidationTextBlock.Text = UiText.Get("Text.CustomColorExample");
        ValidationTextBlock.Foreground = (Brush)FindResource(
            "TextSecondaryBrush");
    }

    private bool TryParseRgb(out int rgb)
    {
        rgb = 0;
        string value = HexTextBox.Text.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        return value.Length == 6
            && int.TryParse(
                value,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out rgb);
    }

    private static (double Hue, double Saturation, double Value) ToHsv(
        Color color)
    {
        double red = color.R / 255.0;
        double green = color.G / 255.0;
        double blue = color.B / 255.0;
        double maximum = Math.Max(red, Math.Max(green, blue));
        double minimum = Math.Min(red, Math.Min(green, blue));
        double delta = maximum - minimum;
        double hue = 0;
        if (delta > 0)
        {
            if (maximum == red)
            {
                hue = 60 * (((green - blue) / delta) % 6);
            }
            else if (maximum == green)
            {
                hue = 60 * (((blue - red) / delta) + 2);
            }
            else
            {
                hue = 60 * (((red - green) / delta) + 4);
            }
        }

        if (hue < 0)
        {
            hue += 360;
        }

        double saturation = maximum == 0 ? 0 : delta / maximum;
        return (hue, saturation, maximum);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        double chroma = value * saturation;
        double secondary = chroma * (1 - Math.Abs(((hue / 60) % 2) - 1));
        double offset = value - chroma;
        (double red, double green, double blue) = hue switch
        {
            < 60 => (chroma, secondary, 0.0),
            < 120 => (secondary, chroma, 0.0),
            < 180 => (0.0, chroma, secondary),
            < 240 => (0.0, secondary, chroma),
            < 300 => (secondary, 0.0, chroma),
            _ => (chroma, 0.0, secondary),
        };

        return Color.FromRgb(
            (byte)Math.Round((red + offset) * 255),
            (byte)Math.Round((green + offset) * 255),
            (byte)Math.Round((blue + offset) * 255));
    }
}
