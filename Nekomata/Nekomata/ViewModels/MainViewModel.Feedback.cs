using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Models;
using Nekomata.Core.Guardian.Commands;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.UI.ViewModels;

public sealed class AnswerFeedback
{
    public DateTime At { get; set; }
    public DateTime AnswerAt { get; set; }
    public string Subject { get; set; } = "";
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public int Rating { get; set; }
    public string Note { get; set; } = "";
}

public sealed class LearnedRoutes
{
    /// <summary>Wording → command phrase the user confirmed (👍): runs straight away next time, no AI check.</summary>
    public Dictionary<string, string> Confirmed { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Wording → command phrase the user marked wrong (👎): never routed that way again; goes to chat instead.</summary>
    public Dictionary<string, string> Rejected { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

// Feedback learning. Every Guardian answer in the conversation has 👍 and 👎. Ratings are kept (answer-feedback.json) and
// they change behaviour:
//   * a request the AI router interpreted is remembered: 👍 and the same wording runs that command straight away next
//     time; 👎 and it's never routed that way again (it goes to the conversation instead);
//   * "that was wrong: <what's right>" marks the last answer down and keeps the correction as a fact, which every future
//     conversation sees;
//   * Friday afternoon, a short note: how the week's answers were rated, what keeps missing, what was learned.
// "How am I rating you?" shows the same summary on demand.
public partial class MainViewModel
{
    private static string FeedbackPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "answer-feedback.json");
    private static string LearnedRoutesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "learned-routes.json");
    private List<AnswerFeedback>? _feedback;
    private List<AnswerFeedback> Feedback => _feedback ??= LoadJson<List<AnswerFeedback>>(FeedbackPath) ?? [];
    private LearnedRoutes? _learnedRoutes;
    private LearnedRoutes Routes => _learnedRoutes ??= LoadJson<LearnedRoutes>(LearnedRoutesPath) ?? new();
    private readonly Dictionary<string, string> _aiRouteLog = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _feedbackNoteWeek;

    private static string RouteKey(string text) => string.Join(' ', text.Trim().TrimEnd('?', '!', '.').ToLowerInvariant()
        .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Called by the AI router before asking the model: a learned answer for this exact wording, if any.</summary>
    private (bool Known, GuardianCommandMatch? Match) LearnedRoute(string text)
    {
        var key = RouteKey(text);
        if (Routes.Rejected.ContainsKey(key)) return (true, null);
        if (Routes.Confirmed.TryGetValue(key, out var phrase) && GuardianCommandParser.Parse(phrase) is { Intent: not GuardianCommandIntent.Unknown } match)
        {
            Think($"Understood as: {phrase} (you confirmed this before)");
            return (true, match);
        }
        return (false, null);
    }

    private void NoteAiRoute(string text, string phrase) => _aiRouteLog[RouteKey(text)] = phrase;

    [RelayCommand] private void RateGood(GuardianChatTurn? turn) => RateTurn(turn, 1, "");
    [RelayCommand] private void RateBad(GuardianChatTurn? turn) => RateTurn(turn, -1, "");

    private AnswerFeedback? RateTurn(GuardianChatTurn? turn, int rating, string note)
    {
        if (turn is null || turn.IsUser) return null;
        var index = ChatHistory.IndexOf(turn);
        var question = index > 0 ? ChatHistory.Take(index).LastOrDefault(t => t.IsUser)?.Content ?? "" : "";
        var key = RouteKey(question);
        var subject = SubjectFor(question);

        var entry = Feedback.FirstOrDefault(f => f.AnswerAt == turn.CreatedAt && f.Answer == Trim(turn.Content, 400));
        if (entry is null)
        {
            entry = new AnswerFeedback { AnswerAt = turn.CreatedAt, Question = Trim(question, 300), Answer = Trim(turn.Content, 400), Subject = subject };
            Feedback.Add(entry);
        }
        entry.At = DateTime.Now;
        entry.Rating = turn.Rating == rating && note.Length == 0 ? 0 : rating;   // pressing the same thumb again clears it
        if (note.Length > 0) entry.Note = note;
        turn.Rating = entry.Rating;
        if (entry.Rating == 0) Feedback.Remove(entry);
        SaveJson(FeedbackPath, Feedback.Where(f => f.At > DateTime.Now.AddDays(-180)).ToList());

        if (key.Length > 0 && _aiRouteLog.TryGetValue(key, out var phrase))
        {
            if (entry.Rating > 0) { Routes.Confirmed[key] = phrase; Routes.Rejected.Remove(key); }
            else if (entry.Rating < 0) { Routes.Rejected[key] = phrase; Routes.Confirmed.Remove(key); }
            SaveJson(LearnedRoutesPath, Routes);
        }

        Think(entry.Rating switch
        {
            > 0 => _aiRouteLog.ContainsKey(key) ? "Thanks. I'll take that wording the same way next time" : "Thanks, noted",
            < 0 when note.Length == 0 => _aiRouteLog.ContainsKey(key)
                ? "Noted. I won't read it that way again. Say \"that was wrong: …\" to tell me what's right"
                : "Noted. Say \"that was wrong: …\" to tell me what's right",
            < 0 => "Correction kept",
            _ => "Rating cleared"
        });
        return entry;
    }

    private string SubjectFor(string question)
    {
        if (question.Length == 0) return "Guardian";
        var intent = GuardianCommandParser.Parse(question).Intent;
        if (intent == GuardianCommandIntent.Unknown && _aiRouteLog.TryGetValue(RouteKey(question), out var phrase))
            intent = GuardianCommandParser.Parse(phrase).Intent;
        return intent == GuardianCommandIntent.Unknown ? "Conversation" : intent.ToString();
    }

    /// <summary>"That was wrong: …" marks the last answer down and keeps the correction for every future conversation.</summary>
    private async Task ApplyCorrectionAsync(string note)
    {
        note = note.Trim().TrimEnd('.');
        var last = ChatHistory.LastOrDefault(t => !t.IsUser && !t.Content.StartsWith("Thanks, I've kept that", StringComparison.Ordinal));
        if (last is null) { ShowPaletteResponse("There's no answer of mine to correct yet."); return; }
        var entry = RateTurn(last, -1, note.Length > 0 ? note : "(no detail)");
        if (note.Length < 3)
        {
            ShowPaletteResponse("Marked down. If you tell me what was wrong (\"that was wrong: …\") I'll remember the right answer.");
            return;
        }
        try
        {
            var about = entry is { Question.Length: > 0 } ? $" (about \"{Trim(entry.Question, 80)}\")" : "";
            await _services.GetRequiredService<IGuardianMemoryRepository>().AddAsync(new GuardianMemory
            {
                Category = GuardianMemoryCategories.UserFact,
                Importance = 90,
                Source = $"{UserName} (correction)",
                Summary = $"Correction{about}: {note}"
            });
            ShowPaletteResponse($"Thanks, I've kept that: {note}. I'll use it from now on.");
        }
        catch (Exception ex) { ShowPaletteResponse($"Marked down, but I couldn't save the correction: {ex.Message}"); }
    }

    private string FeedbackSummary(DateTime since, out int up, out int down)
    {
        var recent = Feedback.Where(f => f.At >= since).ToList();
        up = recent.Count(f => f.Rating > 0);
        down = recent.Count(f => f.Rating < 0);
        var sb = new StringBuilder();
        var month = Feedback.Where(f => f.At >= DateTime.Now.AddDays(-30)).ToList();
        var missing = month.GroupBy(f => f.Subject)
            .Select(g => (Subject: g.Key, Up: g.Count(f => f.Rating > 0), Down: g.Count(f => f.Rating < 0)))
            .Where(s => s.Down >= 2 && s.Down > s.Up).OrderByDescending(s => s.Down).ToList();
        var good = month.GroupBy(f => f.Subject).Select(g => (Subject: g.Key, Up: g.Count(f => f.Rating > 0)))
            .Where(s => s.Up >= 2).OrderByDescending(s => s.Up).Take(3).ToList();
        var corrections = recent.Count(f => f.Note.Length > 0 && f.Note != "(no detail)");
        sb.AppendLine($"{up} 👍 · {down} 👎 since {since:ddd d MMM}.");
        if (missing.Count > 0)
            sb.AppendLine("Keeps missing (last 30 days): " + string.Join("; ", missing.Take(4).Select(m => $"{Spaced(m.Subject)} ({m.Down} 👎)")) +
                          ". Tell me what you'd want instead, or switch it off in settings.");
        if (good.Count > 0) sb.AppendLine("Working well: " + string.Join(", ", good.Select(g => Spaced(g.Subject))) + ".");
        if (corrections > 0) sb.AppendLine($"Corrections kept as facts: {corrections}.");
        if (Routes.Confirmed.Count + Routes.Rejected.Count > 0)
            sb.AppendLine($"Wordings learned: {Routes.Confirmed.Count} confirmed, {Routes.Rejected.Count} no longer routed.");
        var notes = recent.Where(f => f.Rating < 0 && f.Note.Length > 0 && f.Note != "(no detail)").OrderByDescending(f => f.At).Take(5).ToList();
        if (notes.Count > 0)
        {
            sb.AppendLine().AppendLine("WHAT YOU CORRECTED");
            foreach (var n in notes) sb.AppendLine($"• {Trim(n.Question, 60)} → {n.Note}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Spaced(string subject)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(subject, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    private async Task ShowFeedbackAsync()
    {
        if (Feedback.Count == 0)
        {
            await RespondAndSpeakAsync("You haven't rated any of my answers yet. Use the thumbs on each answer, or say \"that was wrong: …\" and I'll learn from it.");
            return;
        }
        var text = FeedbackSummary(DateTime.Today.AddDays(-7), out var up, out var down);
        ShowPaletteResponse("YOUR FEEDBACK\n\n" + text);
        await SpeakGuardianAsync($"In the last week you gave {up} thumbs up and {down} down.", true);
    }

    // Friday from 15:00, once a week, when there was any feedback.
    private void MaybeWeeklyFeedbackNote(DateTimeOffset now)
    {
        var local = now.LocalDateTime;
        var weekStart = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        if (local.DayOfWeek != DayOfWeek.Friday || local.Hour < 15 || _feedbackNoteWeek == weekStart) return;
        _feedbackNoteWeek = weekStart;
        if (!Feedback.Any(f => f.At >= weekStart)) return;
        var text = FeedbackSummary(weekStart, out var up, out var down);
        RaiseAttention($"feedback:{weekStart:yyyyMMdd}", "Guardian", "Info", $"Your feedback this week: {up} 👍 · {down} 👎",
            text.Replace("\n", " ").Replace("WHAT YOU CORRECTED", "Corrected:"), "show_feedback");
    }
}
