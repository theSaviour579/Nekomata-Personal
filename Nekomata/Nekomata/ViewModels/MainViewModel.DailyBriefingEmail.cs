using Microsoft.Extensions.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Planning;
using System.IO;
using System.Text;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private DateTimeOffset _nextBriefingEmailAttemptAt;
    private bool _briefingEmailBusy;

    private async Task TrySendDailyBriefingEmailAsync()
    {
        var profile = _personalProfile.Current;
        var today = DateTime.Today;
        if (!profile.EmailBriefingEnabled || !profile.WorkScheduleConfigured ||
            _briefingEmailBusy || DateTimeOffset.Now < _nextBriefingEmailAttemptAt ||
            File.Exists(DailyBriefingSentPath(today)))
            return;

        var settings = _services.GetRequiredService<WorkingDaySettings>();
        if (DateTime.Now < settings.GetStart(today)) return;

        _briefingEmailBusy = true;
        _nextBriefingEmailAttemptAt = DateTimeOffset.Now.AddMinutes(15);
        try
        {
            var email = _services.GetRequiredService<IEmailService>();
            var mailbox = await email.GetMailboxAddressAsync();
            var briefing = Workspace.Briefing;
            var body = new StringBuilder()
                .AppendLine(briefing.Greeting)
                .AppendLine()
                .AppendLine(briefing.Headline)
                .AppendLine(briefing.GuardianComment)
                .AppendLine()
                .AppendLine($"Objective: {(string.IsNullOrWhiteSpace(briefing.ObjectiveTitle) ? "No ranked objective available" : briefing.ObjectiveTitle)}")
                .AppendLine($"Due today: {briefing.TasksDueToday}")
                .AppendLine($"Overdue: {briefing.OverdueTasks}")
                .AppendLine($"Calendar: {briefing.CalendarSummary}")
                .AppendLine($"Working capacity: {briefing.AvailableMinutesToday} minutes")
                .AppendLine()
                .AppendLine("Sent by Nekomata Personal using your configured daily briefing preference.")
                .ToString();

            await email.SendMessageAsync(mailbox, $"Nekomata daily briefing · {today:dddd d MMMM}", body);
            var path = DailyBriefingSentPath(today);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Daily briefing email could not be sent: {ex}");
        }
        finally
        {
            _briefingEmailBusy = false;
        }
    }

    private static string DailyBriefingSentPath(DateTime day) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nekomata Personal", "daily-briefings", $"{day:yyyy-MM-dd}.sent");
}
