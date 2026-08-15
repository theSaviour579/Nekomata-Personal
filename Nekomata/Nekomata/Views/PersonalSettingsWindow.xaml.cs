using Nekomata.UI.Services;
using System.Windows;

namespace Nekomata.UI.Views;

public partial class PersonalSettingsWindow : Window
{
    private readonly PersonalProfileService _profile;
    private readonly PersonalSecretService _secrets;

    public PersonalSettingsWindow(PersonalProfileService profile, PersonalSecretService secrets)
    {
        _profile = profile;
        _secrets = secrets;
        InitializeComponent();
        NameInput.Text = profile.Current.DisplayName;
        Loaded += (_, _) => NameInput.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text))
        {
            MessageBox.Show("Please enter the name Nekomata should use.", "Your name", MessageBoxButton.OK, MessageBoxImage.Information);
            NameInput.Focus();
            return;
        }

        try
        {
            _profile.Save(NameInput.Text, _profile.Current.StartWithWindows);
            if (RemoveOpenAiKey.IsChecked == true) _secrets.DeleteOpenAiApiKey();
            else if (!string.IsNullOrWhiteSpace(OpenAiKeyInput.Password)) _secrets.SaveOpenAiApiKey(OpenAiKeyInput.Password);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Settings could not be saved", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
