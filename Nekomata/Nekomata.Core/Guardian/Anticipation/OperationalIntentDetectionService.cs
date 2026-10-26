using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Anticipation;

public sealed partial class OperationalIntentDetectionService(
    IContextSignalRepository signals,
    Nekomata.Core.Personalization.IUserIdentity? userIdentity = null)
{
    public async Task<IReadOnlyList<DetectedOperationalIntent>> DetectAsync(DateTimeOffset now)
    {
        var results = new List<DetectedOperationalIntent>();
        foreach (var signal in (await signals.GetCurrentAsync()).Where(x =>
                     x.Source == "Microsoft Teams" && x.Kind == ContextSignalKind.Conversation &&
                     x.ObservedAt >= now.AddDays(-7) && !IsUser(x.Person)))
        {
            var text = $"{signal.Title} {signal.Body}";
            if (!MeetingRequest().IsMatch(text)) continue;
            var duration = ParseDuration(text);
            var window = ParseWindow(text);
            var confidence = 75 + (signal.WorkThreadId is null ? 0 : 15) + (duration == 30 ? 5 : 0) +
                             (window == "Unspecified" ? 0 : 5);
            results.Add(new DetectedOperationalIntent
            {
                Id = StableId($"schedule:{signal.Id}"), SignalId = signal.Id, Type = OperationalIntentType.ScheduleMeeting,
                Person = signal.Person, DurationMinutes = duration, RequestedWindow = window,
                TargetConversationId = signal.ConversationId,
                WorkThreadId = signal.WorkThreadId, Confidence = Math.Min(100, confidence), ObservedAt = signal.ObservedAt,
                EvidenceSummary = $"Teams request from {signal.Person}; duration {duration} minutes; window {window}; " +
                                  (signal.WorkThreadId is null ? "Work Thread unresolved" : "confirmed Work Thread context")
            });
        }
        return results.OrderByDescending(x => x.ObservedAt).ToList();
    }

    private bool IsUser(string person) => userIdentity?.Is(person) == true;
    private static int ParseDuration(string text)
    {
        if (text.Contains("half an hour", StringComparison.OrdinalIgnoreCase) || text.Contains("half hour", StringComparison.OrdinalIgnoreCase)) return 30;
        var match = Duration().Match(text);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var minutes)) return Math.Clamp(minutes, 5, 240);
        if (text.Contains("an hour", StringComparison.OrdinalIgnoreCase) || text.Contains("one hour", StringComparison.OrdinalIgnoreCase)) return 60;
        return 30;
    }

    private static string ParseWindow(string text)
    {
        foreach (var value in new[] { "tomorrow morning", "tomorrow afternoon", "tomorrow", "this morning", "this afternoon", "today" })
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase)) return char.ToUpperInvariant(value[0]) + value[1..];
        return "Unspecified";
    }

    private static Guid StableId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]);

    [GeneratedRegex(@"\b(?:have|got|spare|find|book|arrange|schedule|meet|meeting|catch up|go through|discuss)\b.{0,80}\b(?:minutes?|mins?|hour|time|tomorrow|today|morning|afternoon)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MeetingRequest();
    [GeneratedRegex(@"\b(\d{1,3})\s*(?:minutes?|mins?|m)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Duration();
}
