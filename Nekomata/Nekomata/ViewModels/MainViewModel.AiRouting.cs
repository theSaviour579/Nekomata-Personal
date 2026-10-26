using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Interfaces;
using Nekomata.Core.Guardian.Commands;

namespace Nekomata.UI.ViewModels;

public sealed class RouteDecision { public string Phrase { get; set; } = ""; }

// Smarter routing. Commands used to run only when the user happened to use one of the listed phrasings; anything else went to
// the general chat, which then answered without the data ("what's due"). Now, when the parser doesn't recognise
// what he said, a quick AI check compares it with everything Guardian can do (the "what can you do" catalogue plus a few
// more) and, if he clearly meant one, rewrites it into that command's form, which then runs. If not, it goes to chat as
// before. Short chit-chat and long messages skip the check.
public partial class MainViewModel
{
    private static readonly string[] RoutingExtras =
    [
        "brief me", "what's slipping?", "what do I owe?", "plan tomorrow", "plan my day", "hand off", "draft the monthly report",
        "team digest", "what's renewing?", "quiet for an hour", "what's X working on?", "how's the team doing?", "brief me on Sam",
        "assign the tasks", "what did I promise today?", "read me my important emails", "reply to Sam saying ...", "wrap up my day",
        "are you working properly?", "project status website relaunch", "draft the project update", "what needs approving?",
        "what's due?", "where did my time go?", "week in review", "what are you watching?",
        "what's gone stale?", "flag a serious project risk", "project update", "project risk resolved", "search my history",
        "prep me for the supplier call", "review my invites", "what did I miss?", "catch me up on Teams", "anything from my manager?",
        "when did we last change the delivery plan?", "capture a task for Sam: confirm the revised date", "what can you do?"
    ];

    private GuardianCommandMatch? _preRouted;
    private string? _aiRoutedText;

    private static readonly Regex Chitchat = new(@"^(?:hi|hello|hey|morning|good (?:morning|afternoon|evening)|thanks?(?: you)?|cheers|ok(?:ay)?|cool|great|nice|yes|no|yep|nope)\b[\s!.]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A recognised command for what the user said, or null to treat it as conversation.</summary>
    private async Task<GuardianCommandMatch?> RouteWithAiAsync(string text)
    {
        text = text.Trim();
        if (text.Length < 6 || text.Length > 240 || Chitchat.IsMatch(text)) return null;
        var learned = LearnedRoute(text);
        if (learned.Known)
        {
            if (learned.Match is not null && Routes.Confirmed.TryGetValue(RouteKey(text), out var confirmed)) NoteAiRoute(text, confirmed);
            return learned.Match;
        }
        try
        {
            var catalogue = string.Join("\n", Capabilities.Where(c => !c.Say.StartsWith('(') && !c.Say.StartsWith("Ctrl"))
                .Select(c => $"- {c.Say} ({c.Does})").Concat(RoutingExtras.Select(e => $"- {e}")).Distinct());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var decision = await _services.GetRequiredService<IStructuredAIProvider>().AskStructuredAsync<RouteDecision>(
                "You route requests for Guardian, a personal planning assistant. Below are the commands it can run. If the user's message is " +
                "clearly asking for one of them, return Phrase = that command written in its listed form, with names, topics or times " +
                "from the message filled in (e.g. 'prep me for the supplier call', 'capture a task for Kev Hart: confirm the revised date'). If it's " +
                "a general question, a conversation, or doesn't clearly match, return an empty Phrase. Never invent a command.\n\nCommands:\n" + catalogue,
                text).WaitAsync(timeout.Token);
            var phrase = decision?.Phrase?.Trim() ?? "";
            if (phrase.Length == 0 || phrase.Equals(text, StringComparison.OrdinalIgnoreCase)) return null;
            var match = GuardianCommandParser.Parse(phrase);
            if (match.Intent == GuardianCommandIntent.Unknown) return null;
            Think($"Understood as: {phrase}");
            NoteAiRoute(text, phrase);
            return match;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AI routing skipped: {ex.Message}");
            return null;
        }
    }
}
