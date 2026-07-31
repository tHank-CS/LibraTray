using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using LibraTray.App.Presentation;

namespace LibraTray.App;

public partial class QuickPanelWindow : Window
{
    private bool _allowClose;

    public QuickPanelWindow()
    {
        InitializeComponent();
        DataContext = new QuickPanelViewModel();
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
}
