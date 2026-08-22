using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using LibraTray.App.Presentation;
using LibraTray.Core.Devices.LibraPro;
using LibraTray.Core.Diagnostics;
using LibraTray.Core.Identity;

namespace LibraTray.App;

public partial class DeviceDetailsWindow : Window
{
    private readonly LibraProDeviceSession _session;
    private readonly QuickPanelViewModel _quickPanelViewModel;
    private DateTimeOffset? _lastStateUpdate;
    private bool _closed;

    internal DeviceDetailsWindow(
        LibraProDeviceSession session,
        QuickPanelViewModel quickPanelViewModel)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(quickPanelViewModel);
        _session = session;
        _quickPanelViewModel = quickPanelViewModel;
        _lastStateUpdate = session.CurrentState is null
            ? null
            : DateTimeOffset.Now;
        InitializeComponent();
        DataContext = quickPanelViewModel;
        _session.StatusChanged += OnSessionStatusChanged;
        _session.StateChanged += OnSessionStateChanged;
        UpdateView();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_closed)
        {
            _closed = true;
            _session.StatusChanged -= OnSessionStatusChanged;
            _session.StateChanged -= OnSessionStateChanged;
        }

        base.OnClosed(e);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshButton.IsEnabled = false;
        DiagnosticActionTextBlock.Text = UiText.Get("Message.ReadingState");
        try
        {
            if (_session.CurrentState is null)
            {
                await _session.DiscoverAndConnectAsync();
            }
            else
            {
                _ = await _session.RefreshAsync();
            }

            DiagnosticActionTextBlock.Text = UiText.Get("Message.StateRefreshed");
        }
        catch (Exception)
        {
            // UI diagnostics must not terminate the tray host for any transport
            // or protocol failure; the session already reports detailed status.
            DiagnosticActionTextBlock.Text = UiText.Get("Message.RefreshFailed");
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            UpdateView();
        }
    }

    private async void BackgroundPostButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_quickPanelViewModel.IsSegmentColorMode
            && _quickPanelViewModel.ExperimentalSegmentRgbEnabled
            && !EnsureSegmentFirmwareAllowed())
        {
            return;
        }

        BackgroundPostButton.IsEnabled = false;
        BackgroundPostStatusTextBlock.Text = UiText.Get("Message.ApplyingSettings");
        try
        {
            bool completed = await _quickPanelViewModel.RunBackgroundPostAsync();
            BackgroundPostStatusTextBlock.Text = UiText.Get(
                completed
                    ? "Message.BackgroundPostDone"
                    : "Message.BackgroundPostFailed");
        }
        catch (Exception)
        {
            BackgroundPostStatusTextBlock.Text = UiText.Get(
                "Message.BackgroundPostFailed");
        }
        finally
        {
            BackgroundPostButton.IsEnabled = _quickPanelViewModel.CanControl;
            UpdateView();
        }
    }

    private bool EnsureSegmentFirmwareAllowed()
    {
        SegmentRgbFirmwareAccess gate = _quickPanelViewModel.GetSegmentFirmwareGate();
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

        _quickPanelViewModel.AcknowledgeCurrentSegmentFirmware();
        return true;
    }

    private void CopyDiagnosticButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            Clipboard.SetText(DiagnosticTextBox.Text);
            DiagnosticActionTextBlock.Text = UiText.Get("Message.SummaryCopied");
        }
        catch (ExternalException)
        {
            DiagnosticActionTextBlock.Text = UiText.Get(
                "Message.ClipboardUnavailable");
        }
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        string path = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "LibraTray",
            "logs");
        if (!Directory.Exists(path))
        {
            MessageBox.Show(
                UiText.Get("Message.NoProbeLogs"),
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            _ = Process.Start(
                new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                UiText.Get("Message.OpenLogsFailed"),
                "LibraTray",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close();
    }

    private void WindowHeader_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _ = sender;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnSessionStatusChanged(
        object? sender,
        LibraProSessionStatusChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _ = Dispatcher.BeginInvoke(UpdateView);
    }

    private void OnSessionStateChanged(
        object? sender,
        LibraProStateChangedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _lastStateUpdate = DateTimeOffset.Now;
            UpdateView();
        });
    }

    private void UpdateView()
    {
        if (_closed)
        {
            return;
        }

        DeviceIdentity? identity = _session.Identity;
        LibraProConnectionInfo? connection = _session.ConnectionInfo;
        LibraProState? state = _session.CurrentState;
        DeviceNameTextBlock.Text = _quickPanelViewModel.DeviceName;
        ConnectionStatusTextBlock.Text = _quickPanelViewModel.ConnectionStatus;
        FriendlyNameTextBlock.Text = identity?.FriendlyProductName
            ?? ProductIdentityCatalog.LibraProFriendlyProductName;
        HardwareModelTextBlock.Text = identity?.HardwareModel
            ?? ProductIdentityCatalog.LibraProHardwareModel;
        FirmwareTextBlock.Text = SafeText(
            connection?.FirmwareVersion,
            64,
            UiText.Get("Text.NotConnected"));
        ConnectedAtTextBlock.Text = connection is null
            ? UiText.Get("Text.NotConnected")
            : connection.ConnectedAtUtc.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.CurrentCulture);
        StateSummaryTextBlock.Text = FormatState(state);
        LastUpdatedTextBlock.Text = _lastStateUpdate is null
            ? UiText.Get("Message.NoConfirmedState")
            : UiText.Format(
                "Message.LastUpdated",
                _lastStateUpdate.Value.ToString(
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.CurrentCulture));
        DiagnosticTextBox.Text = LibraProDiagnosticReport.Create(
            GetApplicationVersion(),
            _session.Status,
            identity,
            connection,
            state,
            DateTimeOffset.UtcNow);
    }

    internal void RefreshLocalizedText()
    {
        DiagnosticActionTextBlock.Text = string.Empty;
        UpdateView();
    }

    private static string FormatState(LibraProState? state)
    {
        if (state is null)
        {
            return UiText.Get("Message.NoConfirmedStateAvailable");
        }

        return UiText.Format(
            "Message.StateSummary",
            OnOff(state.MainPower),
            state.MainBrightness,
            state.MainColorTemperature,
            OnOff(state.BackgroundPower),
            state.BackgroundBrightness,
            state.BackgroundRgb);
    }

    private static string SafeText(
        string? value,
        int maximumLength,
        string? fallback = null)
    {
        fallback ??= UiText.Get("Message.InvalidValue");
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string safe = new(
            value
                .Where(character => !char.IsControl(character))
                .Take(maximumLength)
                .ToArray());
        return safe.Length == 0 ? fallback : safe;
    }

    private static string GetApplicationVersion() =>
        typeof(App).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    private static string OnOff(bool enabled) => UiText.Get(
        enabled ? "Text.On" : "Text.Off");
}
