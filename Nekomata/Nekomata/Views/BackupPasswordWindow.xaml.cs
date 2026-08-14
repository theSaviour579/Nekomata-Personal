using System.Windows;

namespace Nekomata.UI.Views;

public partial class BackupPasswordWindow : Window
{
    public BackupPasswordWindow() => InitializeComponent();
    public string Passphrase => PasswordInput.Password;

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (Passphrase.Length < 12)
        {
            MessageBox.Show("Use a backup password containing at least 12 characters.", "Backup Password", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
