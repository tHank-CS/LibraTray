using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LibraTray.App;

public partial class CustomColorWindow : Window
{
    internal CustomColorWindow(int initialRgb)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialRgb);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialRgb, 0xFFFFFF);
        InitializeComponent();
        HexTextBox.Text = $"#{initialRgb:X6}";
        Loaded += (_, _) =>
        {
            HexTextBox.Focus();
            HexTextBox.SelectAll();
        };
    }

    internal int RgbValue { get; private set; }

    private void HexTextBox_TextChanged(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (TryParseRgb(out int rgb))
        {
            PreviewEllipse.Fill = new SolidColorBrush(
                Color.FromRgb(
                    (byte)(rgb >> 16),
                    (byte)(rgb >> 8),
                    (byte)rgb));
            ValidationTextBlock.Text = "格式示例：#33AADD";
            ValidationTextBlock.Foreground =
                (Brush)FindResource("TextSecondaryBrush");
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!TryParseRgb(out int rgb))
        {
            ValidationTextBlock.Text = "请输入有效的六位 RGB 十六进制颜色。";
            ValidationTextBlock.Foreground = new SolidColorBrush(
                Color.FromRgb(0xB5, 0x47, 0x3C));
            HexTextBox.Focus();
            return;
        }

        RgbValue = rgb;
        DialogResult = true;
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
}
