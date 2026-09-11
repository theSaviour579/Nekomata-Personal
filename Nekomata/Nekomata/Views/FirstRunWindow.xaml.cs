using Nekomata.UI.Services;
using Nekomata.UI.ViewModels;
using Nekomata.Models.Planning;
using System.Windows;
using System.Windows.Controls;

namespace Nekomata.UI.Views;

public partial class FirstRunWindow : Window
{
    private readonly FirstRunService _firstRun;
    private readonly StartupRegistrationService _startup;
    private readonly MainViewModel _main;
    private readonly PersonalSecretService _secrets;
    private readonly PersonalProfileService _profile;
    private readonly WorkingDaySettings _workingDay;
    public FirstRunWindow(FirstRunService firstRun, StartupRegistrationService startup, MainViewModel main, PersonalProfileService profile, PersonalSecretService secrets, WorkingDaySettings workingDay)
    {
        _firstRun = firstRun;
        _startup = startup;
        _main = main;
        _secrets = secrets;
        _profile = profile;
        _workingDay = workingDay;
        InitializeComponent();
        StartupChoice.IsChecked = startup.IsEnabled;
        NameInput.Text = profile.Current.DisplayName;
        if (profile.Current.WorkScheduleConfigured)
        {
            WorkStartInput.Text = FormatTime(profile.Current.WorkdayStart);
            WorkEndInput.Text = FormatTime(profile.Current.WorkdayEnd);
            LunchChoice.IsChecked = profile.Current.IncludeLunchBreak;
            LunchStartInput.Text = FormatTime(profile.Current.LunchStart);
            LunchEndInput.Text = FormatTime(profile.Current.LunchEnd);
            WrapUpTimeInput.Text = FormatTime(profile.Current.WrapUpTime);
            EmailBriefingChoice.IsChecked = profile.Current.EmailBriefingEnabled;
        }
        else
        {
            LunchChoice.IsChecked = true;
        }
        UpdateLunchInputs();
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

        var lunchStart = TimeSpan.Zero;
        var lunchEnd = TimeSpan.Zero;
        if (!TryReadTime(WorkStartInput, "working-day start", out var workStart) ||
            !TryReadTime(WorkEndInput, "working-day finish", out var workEnd) ||
            !TryReadTime(WrapUpTimeInput, "wrap-up", out var wrapUp) ||
            (LunchChoice.IsChecked == true &&
             (!TryReadTime(LunchStartInput, "lunch start", out lunchStart) ||
              !TryReadTime(LunchEndInput, "lunch finish", out lunchEnd))))
            return false;

        if (LunchChoice.IsChecked != true)
        {
            lunchStart = TimeSpan.Zero;
            lunchEnd = TimeSpan.Zero;
        }

        try
        {
            _firstRun.Complete(
                NameInput.Text, StartupChoice.IsChecked == true,
                workStart, workEnd, LunchChoice.IsChecked == true,
                lunchStart, lunchEnd, wrapUp,
                EmailBriefingChoice.IsChecked == true);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "Check your schedule", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        _startup.SetEnabled(StartupChoice.IsChecked == true);
        _main.StartWithWindows = StartupChoice.IsChecked == true;
        _profile.ApplyWorkSchedule(_workingDay);
        _secrets.SaveOpenAiApiKey(OpenAiKeyInput.Password);
        _main.ApplyPersonalProfile();
        return true;
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
}
