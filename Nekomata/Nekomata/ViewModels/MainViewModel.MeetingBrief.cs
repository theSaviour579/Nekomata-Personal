using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.UI.ViewModels;
public partial class MainViewModel
{
    private sealed class MeetingBriefSnapshot
    {
        public string MeetingId { get; set; } = "";
        public DateTimeOffset Start { get; set; }
        public Dictionary<string,string> Evidence { get; set; } = [];
    }
    private async Task<(List<string> Lines, List<MeetingBriefSource> Sources)> GatherMeetingBriefAsync(CalendarEvent meeting)
    {
        var lines = new List<string>();
        var sources = new List<MeetingBriefSource>();
        var evidence = new Dictionary<string,string>();
        var linkedTaskIds = new HashSet<long>();
        try
        {
            var loops = await _services.GetRequiredService<IGuardianOpenLoopRepository>().GetOpenAsync().WaitAsync(TimeSpan.FromSeconds(8));
            var linked = loops.Where(x => x.CalendarEventId == meeting.Id).Take(8).ToList();
            foreach (var loop in linked)
            {
                if (loop.TaskId is { } taskId) linkedTaskIds.Add(taskId);
                var text = $"{loop.Title} · {loop.Type}\nOwner/waiting on: {loop.WaitingOn}\nDue: {(loop.DueAt is { } due ? due.LocalDateTime.ToString("ddd dd MMM HH:mm") : "not recorded")}\n{loop.Detail}";
                evidence[$"loop:{loop.Id}"] = text;
                sources.Add(new() { Title = "Linked commitment · " + loop.Title, Content = text });
            }
            lines.Add("LINKED COMMITMENTS / DECISIONS\n" + (linked.Count == 0 ? "No explicitly linked open commitments found in the available records." : string.Join("\n", linked.Select(x => $"• {x.Title} · {x.Type} · {x.WaitingOn}"))));
            lines.Add("SUGGESTED AGENDA\n1. Agree today's intended outcome.\n" +
                string.Join("\n", linked.Take(4).Select((x,i) => $"{i+2}. Review ‘{x.Title}’: confirm status, owner and next date.")) +
                "\nClose: record decisions, owners and dates in meeting notes. This is a proposed agenda, not an extracted agreement.");
        }
        catch (Exception ex) { lines.Add("Commitments unavailable: " + ex.Message); }
        var tasks = Workspace.Tasks.Where(x => linkedTaskIds.Contains(x.Id)).Take(8).ToList();
        lines.Add("LINKED OBJECTIVES\n" + (tasks.Count == 0 ? "No explicit objective links found." : string.Join("\n", tasks.Select(x => $"• {x.Title} · {x.Status} · {x.BlockingReason}"))));
        foreach (var task in tasks) evidence[$"task:{task.Id}"] = $"{task.Title} · {task.Status} · {task.BlockingReason}";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var emails = await _services.GetRequiredService<IEmailService>().SearchInboxAndSentAsync(meeting.Subject, timeout.Token);
            foreach (var email in emails.OrderByDescending(x => x.ReceivedAt).Take(5))
            {
                var text = $"{email.Subject}\nFrom: {email.SenderAddress}\nTo: {string.Join(", ", email.ToRecipientAddresses)}\nDate: {email.ReceivedAt.LocalDateTime:g}\nMessage ID: {email.Id}\n\n{email.BodyPreview}";
                sources.Add(new() { Title = "Possible email context · " + email.Subject, Content = text });
                evidence[$"email:{email.Id}"] = $"Possible email: {email.Subject} · {email.ReceivedAt.LocalDateTime:g} · {email.SenderAddress}";
            }
            lines.Add(emails.Count == 0 ? "EMAIL EVIDENCE\nNo subject-search matches found." : "EMAIL EVIDENCE\nSubject-search candidates are available below. These are possible context, not confirmed meeting links.");
        }
        catch (Exception ex) { lines.Add("Email context unavailable: " + ex.Message); }
        var folder = Path.Combine(DataFolder, "meeting-briefs");
        var path = Path.Combine(folder, MeetingBriefEvidence.Signature(meeting.Subject, meeting.Attendees) + ".json");
        try
        {
            var previous = File.Exists(path) ? JsonSerializer.Deserialize<MeetingBriefSnapshot>(File.ReadAllText(path)) : null;
            if (previous != null && previous.Start < meeting.Start)
            {
                var changes = MeetingBriefEvidence.Changes(previous.Evidence, evidence);
                lines.Add($"SINCE THE MATCHING BRIEF FOR {previous.Start.LocalDateTime:ddd dd MMM}\nSame subject and attendee list—not a verified recurring-meeting link.\n" +
                    (changes.Count == 0 ? "No changes detected in the evidence retrieved. Missing records are not evidence of completion." : string.Join("\n", changes.Take(6))));
            }
            else lines.Add("PREVIOUS MEETING CONTEXT\nNo earlier matching brief available. No previous outcomes have been inferred.");
            if (meeting.Start > DateTimeOffset.Now && (previous == null || previous.Start < meeting.Start))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new MeetingBriefSnapshot { MeetingId = meeting.Id, Start = meeting.Start, Evidence = evidence }));
                File.Move(path + ".tmp", path, true);
            }
        }
        catch (Exception ex) { lines.Add("Brief history unavailable: " + ex.Message); }
        return (lines,sources);
    }
}
