using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LibraTray.App.Presentation;

namespace LibraTray.App;

public partial class QuickPanelWindow : Window
{
    private readonly QuickPanelViewModel _viewModel;
    private bool _allowClose;

    internal QuickPanelWindow(QuickPanelViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    internal void PrepareForShutdown() => _allowClose = true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Hide();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await _viewModel.ConnectAsync();
    }

    private async void MainPower_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is CheckBox { IsChecked: bool enabled })
        {
            await _viewModel.SetMainPowerAsync(enabled);
        }
    }

    private async void BackgroundPower_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is CheckBox { IsChecked: bool enabled })
        {
            await _viewModel.SetBackgroundPowerAsync(enabled);
        }
    }

    private async void MainBrightness_PointerReleased(
        object sender,
        MouseButtonEventArgs e)
    {
        _ = e;
        if (sender is Slider slider)
        {
            await _viewModel.SetMainBrightnessAsync(
                (int)Math.Round(slider.Value));
        }
    }

    private async void MainColorTemperature_PointerReleased(
        object sender,
        MouseButtonEventArgs e)
    {
        _ = e;
        if (sender is Slider slider)
        {
            await _viewModel.SetMainColorTemperatureAsync(
                (int)Math.Round(slider.Value));
        }
    }

    private async void BackgroundBrightness_PointerReleased(
        object sender,
        MouseButtonEventArgs e)
    {
        _ = e;
        if (sender is Slider slider)
        {
            await _viewModel.SetBackgroundBrightnessAsync(
                (int)Math.Round(slider.Value));
        }
    }

    private async void BackgroundColor_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Button { Tag: string value }
            && int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int rgb))
        {
            await _viewModel.SetBackgroundRgbAsync(rgb);
        }
    }
}
