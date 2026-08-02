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
        DiagnosticActionTextBlock.Text = "正在复读设备状态…";
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

            DiagnosticActionTextBlock.Text = "状态已刷新";
        }
        catch (Exception)
        {
            // UI diagnostics must not terminate the tray host for any transport
            // or protocol failure; the session already reports detailed status.
            DiagnosticActionTextBlock.Text = "刷新失败，请确认灯具在线";
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
            DiagnosticActionTextBlock.Text = "脱敏摘要已复制";
        }
        catch (ExternalException)
        {
            DiagnosticActionTextBlock.Text = "剪贴板暂时不可用";
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
                "尚未找到探针日志目录。运行协议探针后会自动创建。",
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
                "无法打开日志目录，请稍后重试。",
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
            ?? "尚未连接";
        ReportedNameTextBlock.Text = string.IsNullOrWhiteSpace(
            identity?.ReportedName)
                ? "未提供"
                : SafeText(identity.ReportedName, 128);
        DeviceIdTextBlock.Text = MaskDeviceId(connection?.DeviceId);
        FirmwareTextBlock.Text = SafeText(
            connection?.FirmwareVersion,
            64,
            "尚未连接");
        EndpointTextBlock.Text = connection?.ControlEndPoint.ToString()
            ?? "尚未连接";
        ConnectedAtTextBlock.Text = connection is null
            ? "尚未连接"
            : connection.ConnectedAtUtc.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.CurrentCulture);
        CapabilitiesTextBlock.Text = connection is null
            ? "尚未连接"
            : FormatCapabilities(connection.Capabilities);
        StateSummaryTextBlock.Text = FormatState(state);
        LastUpdatedTextBlock.Text = _lastStateUpdate is null
            ? "尚无确认状态"
            : $"最近更新：{_lastStateUpdate:yyyy-MM-dd HH:mm:ss}";
        DiagnosticTextBox.Text = LibraProDiagnosticReport.Create(
            GetApplicationVersion(),
            _session.Status,
            identity,
            connection,
            state,
            DateTimeOffset.UtcNow);
    }

    private static string FormatState(LibraProState? state)
    {
        if (state is null)
        {
            return "尚无可用的确认状态。";
        }

        return $"主灯：{OnOff(state.MainPower)} · {state.MainBrightness}% · "
            + $"{state.MainColorTemperature} K\n"
            + $"氛围灯：{OnOff(state.BackgroundPower)} · "
            + $"{state.BackgroundBrightness}% · #{state.BackgroundRgb:X6}";
    }

    private static string FormatCapabilities(
        IReadOnlyList<string> capabilities)
    {
        if (capabilities.Count == 0)
        {
            return "未声明";
        }

        return string.Join(
            " · ",
            capabilities.Take(32).Select(value => SafeText(value, 48)))
            + (capabilities.Count > 32
                ? $" · …（另有 {capabilities.Count - 32} 项）"
                : string.Empty);
    }

    private static string MaskDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return "尚未连接";
        }

        string value = SafeText(deviceId, 128);
        return value.Length <= 8
            ? "[已隐藏]"
            : $"{value[..4]}…{value[^4..]}";
    }

    private static string SafeText(
        string? value,
        int maximumLength,
        string fallback = "无效值")
    {
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

    private static string OnOff(bool enabled) => enabled ? "开启" : "关闭";
}
