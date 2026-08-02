using System.Windows;
using LibraTray.App.Presentation;

namespace LibraTray.App;

public partial class PresetNameWindow : Window
{
    internal PresetNameWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    internal string PresetName => NameTextBox.Text;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            ValidationTextBlock.Text = UiText.Get("Message.PresetNameRequired");
            ValidationTextBlock.Visibility = Visibility.Visible;
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
