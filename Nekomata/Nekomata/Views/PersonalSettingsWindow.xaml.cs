using Nekomata.UI.Services;
using Nekomata.Integrations.MicrosoftGraph.Copilot;
using System.Windows;

namespace Nekomata.UI.Views;

public partial class PersonalSettingsWindow : Window
{
    private readonly PersonalProfileService _profile;
    private readonly PersonalSecretService _secrets;
    private readonly CopilotChatService _copilot;

    public PersonalSettingsWindow(PersonalProfileService profile, PersonalSecretService secrets, CopilotChatService copilot)
    {
        _profile = profile;
        _secrets = secrets;
        _copilot = copilot;
        InitializeComponent();
        NameInput.Text = profile.Current.DisplayName;
        MicrosoftAiEndpointInput.Text = profile.Current.AzureOpenAIEndpoint;
        MicrosoftAiDeploymentInput.Text = profile.Current.AzureOpenAIDeployment;
        SpotifyClientIdInput.Text = profile.Current.SpotifyClientId;
        YouTubeMusicUrlInput.Text = profile.Current.YouTubeMusicUrl;
        RadioStationUrlInput.Text = profile.Current.RadioStationUrl;
        MediaProviderInput.SelectedIndex = profile.Current.PreferredMediaProvider switch
        {
            "YouTube Music" => 1,
            "Radio" => 2,
            _ => 0
        };
        ConversationProviderInput.SelectedIndex = profile.Current.ConversationProvider switch
        {
            "Microsoft 365 Copilot (Preview)" => 1,
            "Microsoft AI (Entra)" => 2,
            "OpenAI" => 3,
            "No AI" => 4,
            _ => 0
        };
        CopilotWebSearchInput.IsChecked = profile.Current.CopilotWebSearchEnabled;
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
            _profile.SaveAzureOpenAI(MicrosoftAiEndpointInput.Text, MicrosoftAiDeploymentInput.Text);
            _profile.SaveSpotifyClientId(SpotifyClientIdInput.Text);
            var mediaProvider = (MediaProviderInput.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Spotify";
            _profile.SaveMediaPreference(mediaProvider, YouTubeMusicUrlInput.Text, RadioStationUrlInput.Text);
            var conversationProvider = (ConversationProviderInput.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Automatic";
            _profile.SaveConversationProvider(conversationProvider, CopilotWebSearchInput.IsChecked == true);
            if (RemoveOpenAiKey.IsChecked == true) _secrets.DeleteOpenAiApiKey();
            else if (!string.IsNullOrWhiteSpace(OpenAiKeyInput.Password)) _secrets.SaveOpenAiApiKey(OpenAiKeyInput.Password);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Settings could not be saved", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TestCopilot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button) button.IsEnabled = false;
        CopilotTestStatus.Text = "Requesting the separate Copilot permissions and checking this account…";
        try
        {
            await _copilot.VerifyAccessAsync();
            ConversationProviderInput.SelectedIndex = 1;
            CopilotTestStatus.Text = "Connected · Microsoft 365 Copilot has been selected. Click Save Changes to use it for Guardian conversations.";
        }
        catch (Exception ex) { CopilotTestStatus.Text = ex.Message; }
        finally { if (sender is System.Windows.Controls.Button testButton) testButton.IsEnabled = true; }
    }
}
