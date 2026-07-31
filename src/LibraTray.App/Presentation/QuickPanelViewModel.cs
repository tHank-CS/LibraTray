using System.ComponentModel;
using System.Runtime.CompilerServices;
using LibraTray.Core.Identity;

namespace LibraTray.App.Presentation;

internal sealed class QuickPanelViewModel : INotifyPropertyChanged
{
    private string _connectionStatus = "正在等待局域网设备";
    private bool _isDeviceOnline;

    public QuickPanelViewModel()
    {
        DeviceName = ProductIdentityCatalog.LibraProFriendlyProductName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DeviceName { get; }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetField(ref _connectionStatus, value);
    }

    public bool IsDeviceOnline
    {
        get => _isDeviceOnline;
        set => SetField(ref _isDeviceOnline, value);
    }

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
