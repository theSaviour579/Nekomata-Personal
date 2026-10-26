using System.Text.RegularExpressions;
using Nekomata.AI.Models.Actions;
using Nekomata.UI.Controls.Common;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

// Voice actions: when a spoken request produces a Guardian proposal (calendar moves,
// task or project changes, new tasks), Guardian reads it back in plain English and asks
// "Shall I go ahead?". A clear yes applies it through the same reviewed path as the
// APPLY button (GuardianApplyService: audit log + undo); a clear no drops it; anything
// else is treated as a refinement ("make it 11 instead") and goes back to Guardian;
// silence leaves the proposal on screen untouched. Only unambiguous whole-utterance
// answers count as yes or no, so a misheard phrase can never approve something.
public partial class MainViewModel
{
    private const int SpokenProposalItemLimit = 3;

    private static readonly Regex Affirmative = new(
        @"^(yes|yeah|yep|yup|sure|ok|okay|go ahead|do it|please do|confirm(ed)?|approve(d)?|sounds good|go for it|perfect|that'?s (fine|right|good))( please| then| thanks| thank you)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Negative = new(
        @"^(no|nope|nah|cancel|don'?t|do not|leave it|not now|scrap that|forget it|no thanks|no thank you|stop)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private bool HasVoiceConfirmableProposal() =>
        GuardianProposalVisible && PendingGuardianAction is { } action &&
        (action.Tasks.Any(t => t.Selected && !t.IsRemoved) || action.Changes.Any(c => c.Selected && !c.IsRemoved));

    // Returns the next thing the user said that the conversation loop should handle, or
    // null to end the conversation.
    private async Task<string?> ConfirmProposalByVoiceAsync(GuardianListeningService listening)
    {
        await SpeakAndSettleAsync(DescribeProposalForSpeech(PendingGuardianAction!) + " Shall I go ahead?");

        var reply = await ListenOnceAsync(listening, followUp: true);
        if (string.IsNullOrWhiteSpace(reply)) return null; // proposal stays on screen

        var answer = NormaliseUtterance(reply);
        if (Affirmative.IsMatch(answer))
        {
            GuardianPresenceState = GuardianPresenceState.Thinking;
            await CreateSelectedTasksAsync();
            var applied = !GuardianProposalVisible;
            await SpeakAndSettleAsync(applied
                ? $"Done. {FirstSentence(GuardianResponse)} Say undo if that's not right."
                : $"That didn't go through. {FirstSentence(GuardianResponse)}");
            return await ListenOnceAsync(listening, followUp: true);
        }
        if (Negative.IsMatch(answer))
        {
            DismissGuardianProposal();
            await SpeakAndSettleAsync("Okay, I've left it as it is.");
            return await ListenOnceAsync(listening, followUp: true);
        }
        return reply; // a refinement or a new question
    }

    private async Task SpeakAndSettleAsync(string text)
    {
        await SpeakGuardianAsync(text, true);
        await WaitForAsync(() => _guardianSpeakingDepth == 0, TurnTimeout);
        await Task.Delay(300);
    }

    private string DescribeProposalForSpeech(GuardianActionResponse action)
    {
        var items = new List<string>();
        foreach (var task in action.Tasks.Where(t => t.Selected && !t.IsRemoved))
            items.Add($"add a task, {task.Title}" + (task.DueAt is { } due ? $", due {SpokenDay(due)}" : ""));
        foreach (var change in action.Changes.Where(c => c.Selected && !c.IsRemoved))
            items.Add(DescribeChange(change));

        if (items.Count == 0) return "There's nothing selected to apply.";
        var spoken = items.Take(SpokenProposalItemLimit).ToArray();
        var more = items.Count - spoken.Length;
        var list = spoken.Length == 1 ? spoken[0] : string.Join(", ", spoken[..^1]) + " and " + spoken[^1];
        return items.Count == 1
            ? $"I'll {list}."
            : $"That's {items.Count} changes: I'll {list}{(more > 0 ? $", plus {more} more on screen" : "")}.";
    }

    private string DescribeChange(GuardianChange change)
    {
        var property = change.Property.Trim();
        var verb = property.ToLowerInvariant();
        if (change.IsCalendarChange)
        {
            var title = change.CalendarTitle;
            var parts = change.NewValue.Split('|', StringSplitOptions.TrimEntries);
            var when = parts.Length >= 1 && DateTimeOffset.TryParse(parts[0], out var start)
                ? $"{SpokenDay(start.LocalDateTime)} at {start.LocalDateTime:HH:mm}"
                : "";
            if (verb.Contains("delete") || verb.Contains("cancel") || verb.Contains("remove"))
                return $"remove {title} from your calendar";
            if (verb.Contains("move") || verb.Contains("update") || verb.Contains("reschedule"))
                return when.Length > 0 ? $"move {title} to {when}" : $"move {title}";
            return when.Length > 0 ? $"book {title} for {when}" : $"book {title}";
        }

        var entity = change.EntityType.ToLowerInvariant() switch
        {
            "task" => Workspace.Tasks.FirstOrDefault(t => t.Id == change.EntityId)?.Title,
            "project" => Workspace.Projects.FirstOrDefault(p => p.Id == change.EntityId)?.Name,
            _ => null
        } ?? (string.IsNullOrWhiteSpace(change.OriginalTitle) ? $"the {change.EntityType.ToLowerInvariant()}" : change.OriginalTitle);

        if (verb == "status" && change.NewValue.Length > 0)
            return $"mark {entity} as {change.NewValue.ToLowerInvariant()}";
        return $"set {entity}'s {verb} to {change.NewValue}";
    }

    private static string SpokenDay(DateTime value)
    {
        var date = value.Date;
        if (date == DateTime.Today) return "today";
        if (date == DateTime.Today.AddDays(1)) return "tomorrow";
        if (date > DateTime.Today && date < DateTime.Today.AddDays(7)) return $"on {date:dddd}";
        return $"on {date:dddd d MMMM}";
    }

    private static string FirstSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        var end = line.IndexOfAny(['.', '!', '?']);
        var sentence = end > 0 ? line[..(end + 1)] : line;
        return sentence.Length > 160 ? sentence[..160].TrimEnd() + "…" : sentence;
    }

    private static string NormaliseUtterance(string heard)
    {
        var normalised = Regex.Replace(heard.Trim().ToLowerInvariant(), @"[^\w' ]", "").Trim();
        return Regex.Replace(normalised, @"\s+", " ");
    }
}
