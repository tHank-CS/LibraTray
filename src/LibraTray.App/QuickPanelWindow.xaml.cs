using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LibraTray.App.Presentation;
using LibraTray.Core.Devices.LibraPro;

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

    internal event EventHandler? SettingsRequested;

    internal event EventHandler? DeviceDetailsRequested;

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

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (!IsVisible || !sizeInfo.HeightChanged)
        {
            return;
        }

        Rect workArea = SystemParameters.WorkArea;
        Top = Math.Max(
            workArea.Top,
            workArea.Bottom - ActualHeight - 16);
    }

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

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DeviceDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DeviceDetailsRequested?.Invoke(this, EventArgs.Empty);
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
        if (sender is ToggleButton { IsChecked: bool enabled })
        {
            await _viewModel.SetMainPowerAsync(enabled);
        }
    }

    private async void BackgroundPower_Click(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is ToggleButton { IsChecked: bool enabled })
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

    private async void MainBrightness_KeyReleased(object sender, KeyEventArgs e)
    {
        if (IsSliderAdjustmentKey(e.Key) && sender is Slider slider)
        {
            await _viewModel.SetMainBrightnessAsync((int)Math.Round(slider.Value));
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

    private async void MainColorTemperature_KeyReleased(
        object sender,
        KeyEventArgs e)
    {
        if (IsSliderAdjustmentKey(e.Key) && sender is Slider slider)
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

    private async void BackgroundBrightness_KeyReleased(
        object sender,
        KeyEventArgs e)
    {
        if (IsSliderAdjustmentKey(e.Key) && sender is Slider slider)
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

    private async void CustomBackgroundColor_Click(
        object sender,
        RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var dialog = new CustomColorWindow(_viewModel.BackgroundRgb)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.SetBackgroundRgbAsync(dialog.RgbValue);
        }
    }

    private void WholeColorMode_Checked(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _viewModel.SelectWholeColorMode();
    }

    private void SegmentColorMode_Checked(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _viewModel.SelectSegmentColorMode();
    }

    private void LeftSegmentColor_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var dialog = new CustomColorWindow(_viewModel.LeftSegmentRgb)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.SetLeftSegmentRgb(dialog.RgbValue);
        }
    }

    private void RightSegmentColor_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var dialog = new CustomColorWindow(_viewModel.RightSegmentRgb)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.SetRightSegmentRgb(dialog.RgbValue);
        }
    }

    private void SwapSegmentColor_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _viewModel.SwapSegmentRgb();
    }

    private async void ApplySegmentColor_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (EnsureSegmentFirmwareAllowed())
        {
            await _viewModel.ApplySegmentRgbAsync();
        }
    }

    private async void RestoreWholeColor_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await _viewModel.SetBackgroundRgbAsync(_viewModel.BackgroundRgb);
    }

    private async void ApplyPresetButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_viewModel.SelectedPreset?.AmbientColorMode
                != AmbientColorMode.Segmented
            || EnsureSegmentFirmwareAllowed())
        {
            await _viewModel.ApplySelectedPresetAsync();
        }
    }

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var dialog = new PresetNameWindow { Owner = this };
        if (dialog.ShowDialog() == true
            && !_viewModel.TrySaveCurrentPreset(
                dialog.PresetName,
                out string? error))
        {
            MessageBox.Show(
                this,
                error,
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_viewModel.SelectedPreset is null)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            UiText.Format(
                "Message.DeletePreset",
                _viewModel.SelectedPreset.Name),
            "LibraTray",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.DeleteSelectedPreset();
        }
    }

    private static bool IsSliderAdjustmentKey(Key key) =>
        key is Key.Left
            or Key.Right
            or Key.Up
            or Key.Down
            or Key.PageUp
            or Key.PageDown
            or Key.Home
            or Key.End;

    internal bool EnsureSegmentFirmwareAllowed()
    {
        SegmentRgbFirmwareAccess gate = _viewModel.GetSegmentFirmwareGate();
        if (gate == SegmentRgbFirmwareAccess.Allowed)
        {
            return true;
        }

        if (gate == SegmentRgbFirmwareAccess.Unsupported)
        {
            MessageBox.Show(
                this,
                UiText.Get("Message.SegmentRgbUnavailable"),
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        string message = UiText.Get(
            gate == SegmentRgbFirmwareAccess.RequiresFirmwareConfirmation
                ? "Message.SegmentFirmwareWarning"
                : "Message.SegmentFirmwareUnknownWarning");
        if (MessageBox.Show(
                this,
                message,
                "LibraTray",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return false;
        }

        _viewModel.AcknowledgeCurrentSegmentFirmware();
        return true;
    }
}
