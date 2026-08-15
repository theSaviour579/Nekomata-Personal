using Nekomata.UI.Services;
using Nekomata.UI.ViewModels;
using System.Windows;

namespace Nekomata.UI.Views;

public partial class FirstRunWindow : Window
{
    private readonly FirstRunService _firstRun;
    private readonly StartupRegistrationService _startup;
    private readonly MainViewModel _main;
    private readonly PersonalSecretService _secrets;
    public FirstRunWindow(FirstRunService firstRun, StartupRegistrationService startup, MainViewModel main, PersonalProfileService profile, PersonalSecretService secrets)
    {
        _firstRun = firstRun;
        _startup = startup;
        _main = main;
        _secrets = secrets;
        InitializeComponent();
        StartupChoice.IsChecked = startup.IsEnabled;
        NameInput.Text = profile.Current.DisplayName;
        Loaded += (_, _) => NameInput.Focus();
    }

    private void OpenSetup_Click(object sender, RoutedEventArgs e)
    {
        if (!Complete())
            return;

        DialogResult = true;
    }

    private bool Complete()
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text))
        {
            MessageBox.Show("Please enter your name so Nekomata can personalise your assistant.", "Your name", MessageBoxButton.OK, MessageBoxImage.Information);
            NameInput.Focus();
            return false;
        }

        _startup.SetEnabled(StartupChoice.IsChecked == true);
        _main.StartWithWindows = StartupChoice.IsChecked == true;
        _firstRun.Complete(NameInput.Text, StartupChoice.IsChecked == true);
        _secrets.SaveOpenAiApiKey(OpenAiKeyInput.Password);
        _main.ApplyPersonalProfile();
        return true;
    }
}
