using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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
        InternalModelTextBlock.Text = connection?.InternalModel
            ?? identity?.InternalModel
            ?? UiText.Get("Text.NotConnected");
        ReportedNameTextBlock.Text = string.IsNullOrWhiteSpace(
            identity?.ReportedName)
                ? UiText.Get("Message.NotProvided")
                : SafeText(identity.ReportedName, 128);
        DeviceIdTextBlock.Text = MaskDeviceId(connection?.DeviceId);
        FirmwareTextBlock.Text = SafeText(
            connection?.FirmwareVersion,
            64,
            UiText.Get("Text.NotConnected"));
        EndpointTextBlock.Text = connection?.ControlEndPoint.ToString()
            ?? UiText.Get("Text.NotConnected");
        ConnectedAtTextBlock.Text = connection is null
            ? UiText.Get("Text.NotConnected")
            : connection.ConnectedAtUtc.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.CurrentCulture);
        CapabilitiesTextBlock.Text = connection is null
            ? UiText.Get("Text.NotConnected")
            : FormatCapabilities(connection.Capabilities);
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

    private static string FormatCapabilities(
        IReadOnlyList<string> capabilities)
    {
        if (capabilities.Count == 0)
        {
            return UiText.Get("Message.NotDeclared");
        }

        return string.Join(
            " · ",
            capabilities.Take(32).Select(value => SafeText(value, 48)))
            + (capabilities.Count > 32
                ? UiText.Format(
                    "Message.MoreCapabilities",
                    capabilities.Count - 32)
                : string.Empty);
    }

    private static string MaskDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return UiText.Get("Text.NotConnected");
        }

        string value = SafeText(deviceId, 128);
        return value.Length <= 8
            ? UiText.Get("Message.Hidden")
            : $"{value[..4]}…{value[^4..]}";
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
