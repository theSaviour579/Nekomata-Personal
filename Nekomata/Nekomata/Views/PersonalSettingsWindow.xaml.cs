using Nekomata.UI.Services;
using Nekomata.Integrations.MicrosoftGraph.Copilot;
using Nekomata.Models.Planning;
using System.Windows;
using System.Windows.Controls;

namespace Nekomata.UI.Views;

public partial class PersonalSettingsWindow : Window
{
    private readonly PersonalProfileService _profile;
    private readonly PersonalSecretService _secrets;
    private readonly CopilotChatService _copilot;
    private readonly WorkingDaySettings _workingDay;

    public PersonalSettingsWindow(PersonalProfileService profile, PersonalSecretService secrets, CopilotChatService copilot, WorkingDaySettings workingDay)
    {
        _profile = profile;
        _secrets = secrets;
        _copilot = copilot;
        _workingDay = workingDay;
        InitializeComponent();
        NameInput.Text = profile.Current.DisplayName;
        JobTitleInput.Text = profile.Current.JobTitle;
        WorkStartInput.Text = FormatTime(workingDay.StartTime);
        WorkEndInput.Text = FormatTime(workingDay.EndTime);
        LunchChoice.IsChecked = workingDay.IncludeLunchBreak;
        LunchStartInput.Text = FormatTime(workingDay.LunchStartTime);
        LunchEndInput.Text = FormatTime(workingDay.LunchStartTime.Add(TimeSpan.FromMinutes(workingDay.LunchDurationMinutes)));
        WrapUpTimeInput.Text = profile.Current.WorkScheduleConfigured
            ? FormatTime(profile.Current.WrapUpTime)
            : FormatTime(workingDay.EndTime.Subtract(TimeSpan.FromMinutes(15)));
        EmailBriefingChoice.IsChecked = profile.Current.EmailBriefingEnabled;
        UpdateLunchInputs();
        MicrosoftAiEndpointInput.Text = profile.Current.AzureOpenAIEndpoint;
        MicrosoftAiDeploymentInput.Text = profile.Current.AzureOpenAIDeployment;
        SpotifyClientIdInput.Text = profile.Current.SpotifyClientId;
        YouTubeMusicUrlInput.Text = profile.Current.YouTubeMusicUrl;
        AppleMusicUrlInput.Text = profile.Current.AppleMusicUrl;
        RadioStationUrlInput.Text = profile.Current.RadioStationUrl;
        MediaProviderInput.SelectedIndex = profile.Current.PreferredMediaProvider switch
        {
            "Apple Music" => 1,
            "YouTube Music" => 2,
            "Radio" => 3,
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
            var lunchStart = TimeSpan.Zero;
            var lunchEnd = TimeSpan.Zero;
            if (!TryReadTime(WorkStartInput, "working-day start", out var workStart) ||
                !TryReadTime(WorkEndInput, "working-day finish", out var workEnd) ||
                !TryReadTime(WrapUpTimeInput, "wrap-up", out var wrapUp) ||
                (LunchChoice.IsChecked == true &&
                 (!TryReadTime(LunchStartInput, "lunch start", out lunchStart) ||
                  !TryReadTime(LunchEndInput, "lunch finish", out lunchEnd))))
                return;
            if (LunchChoice.IsChecked != true)
            {
                lunchStart = TimeSpan.Zero;
                lunchEnd = TimeSpan.Zero;
            }

            _profile.SaveWorkSchedule(
                workStart, workEnd, LunchChoice.IsChecked == true,
                lunchStart, lunchEnd, wrapUp, EmailBriefingChoice.IsChecked == true);
            _profile.Save(NameInput.Text, _profile.Current.StartWithWindows);
            _profile.SaveJobTitle(JobTitleInput.Text);
            _profile.ApplyWorkSchedule(_workingDay);
            _profile.SaveAzureOpenAI(MicrosoftAiEndpointInput.Text, MicrosoftAiDeploymentInput.Text);
            _profile.SaveSpotifyClientId(SpotifyClientIdInput.Text);
            var mediaProvider = (MediaProviderInput.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Spotify";
            _profile.SaveMediaPreference(mediaProvider, YouTubeMusicUrlInput.Text, AppleMusicUrlInput.Text, RadioStationUrlInput.Text);
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

    private void LunchChoice_Changed(object sender, RoutedEventArgs e) => UpdateLunchInputs();

    private void UpdateLunchInputs()
    {
        if (LunchStartInput is null || LunchEndInput is null) return;
        LunchStartInput.IsEnabled = LunchChoice.IsChecked == true;
        LunchEndInput.IsEnabled = LunchChoice.IsChecked == true;
    }

    private static string FormatTime(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}";

    private static bool TryReadTime(TextBox input, string label, out TimeSpan value)
    {
        if (TimeOnly.TryParseExact(input.Text.Trim(), "HH:mm", out var time) ||
            TimeOnly.TryParse(input.Text.Trim(), out time))
        {
            value = time.ToTimeSpan();
            return true;
        }
        value = default;
        MessageBox.Show($"Enter a valid {label} time in HH:mm format.", "Check your schedule", MessageBoxButton.OK, MessageBoxImage.Information);
        input.Focus();
        return false;
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
