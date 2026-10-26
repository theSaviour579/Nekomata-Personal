using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Nekomata.Core.Commitments;

public enum CommitmentDirection { OwedByMe, OwedToMe, Mutual }
public enum CommitmentStatus { Open, DueSoon, Overdue, WaitingExternal, Completed, Superseded, Cancelled, Unclear }

public sealed record Commitment(
    Guid Id, CommitmentDirection Direction, string Owner, string Counterparty, string Summary,
    string SourceType, string SourceReference, long? ProjectId, long? TaskId, long? MissionId,
    DateTimeOffset CreatedAt, DateTimeOffset? DueAt, DateTimeOffset? ExpectedBy, DateTimeOffset? CompletedAt,
    CommitmentStatus Status, int Confidence, string EvidenceSummary, DateTimeOffset LastObservedAt,
    int EscalationLevel, bool IsUserConfirmed, string DeduplicationKey);

public sealed record CommitmentEvidence(
    string SourceType, string SourceReference, string Body, string Speaker, string Counterparty,
    DateTimeOffset ObservedAt, bool IsFromUser, DateTimeOffset? ExplicitDueAt = null,
    long? ProjectId = null, long? TaskId = null, long? MissionId = null, string CorrelationKey = "");

public interface ICommitmentRepository
{
    Task UpsertAsync(IReadOnlyCollection<Commitment> commitments, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Commitment>> GetActiveAsync(CancellationToken cancellationToken = default);
}

public sealed partial class CommitmentEngine
{
    public IReadOnlyList<Commitment> Detect(IEnumerable<CommitmentEvidence> evidence, DateTimeOffset now)
    {
        var candidates = evidence.Select(item => DetectOne(item, now)).Where(x => x is not null).Cast<Commitment>();
        return candidates.GroupBy(x => x.DeduplicationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => Merge(group, now)).OrderBy(x => x.DueAt ?? DateTimeOffset.MaxValue).ToList();
    }

    public Commitment Reconcile(Commitment commitment, CommitmentEvidence evidence, DateTimeOffset now)
    {
        if (commitment.Status is CommitmentStatus.Completed or CommitmentStatus.Cancelled or CommitmentStatus.Superseded)
            return commitment;
        if (evidence.ObservedAt <= commitment.CreatedAt ||
            !(IsCompletionEvidence(evidence.Body) && Related(commitment, evidence) || IsDeliveryOnSameThread(commitment, evidence)))
            return UpdateTimeState(commitment, now);
        return commitment with
        {
            Status = CommitmentStatus.Completed,
            CompletedAt = evidence.ObservedAt,
            EscalationLevel = 0,
            LastObservedAt = evidence.ObservedAt,
            EvidenceSummary = commitment.EvidenceSummary + $" Completion appears evidenced by {evidence.SourceType} {evidence.SourceReference}."
        };
    }

    public static Commitment UpdateTimeState(Commitment value, DateTimeOffset now)
    {
        if (value.Status is CommitmentStatus.Completed or CommitmentStatus.Cancelled or CommitmentStatus.Superseded) return value;
        var status = value.Direction == CommitmentDirection.OwedToMe ? CommitmentStatus.WaitingExternal : CommitmentStatus.Open;
        var escalation = 0;
        if (value.DueAt is { } due)
        {
            if (due < now) { status = CommitmentStatus.Overdue; escalation = value.Direction == CommitmentDirection.OwedByMe ? 2 : 1; }
            else if (due <= now.AddDays(2)) { status = CommitmentStatus.DueSoon; escalation = 1; }
        }
        return value with { Status = status, EscalationLevel = escalation, LastObservedAt = now };
    }

    public static bool RequiresAttention(Commitment value) =>
        value.Status == CommitmentStatus.Overdue && value.Direction == CommitmentDirection.OwedByMe ||
        value.Status == CommitmentStatus.DueSoon && value.Direction == CommitmentDirection.OwedByMe && value.Confidence >= 70;

    private static Commitment? DetectOne(CommitmentEvidence item, DateTimeOffset now)
    {
        // Only the speaker's own words: the quoted thread under a reply is someone else's ("I'll deal with it tomorrow"
        // quoted beneath the user's reply became his promise).
        var body = Compact(OwnWords(item.Body));
        var explicitMatch = ExplicitPromise().Match(body);
        var vagueMatch = VaguePromise().Match(body);
        if (!explicitMatch.Success && !vagueMatch.Success) return null;
        var explicitPromise = explicitMatch.Success && !vagueMatch.Success;
        var summary = TrimSummary(explicitPromise ? explicitMatch.Value : vagueMatch.Value);
        var direction = item.IsFromUser ? CommitmentDirection.OwedByMe : CommitmentDirection.OwedToMe;
        var owner = item.Speaker;
        var counterparty = item.Counterparty;
        var due = item.ExplicitDueAt ?? ParseRelativeDue(body, item.ObservedAt);
        var confidence = explicitPromise ? (due is null ? 78 : 92) : 48;
        var key = BuildDeduplicationKey(item.CorrelationKey, summary, owner, counterparty);
        var status = direction == CommitmentDirection.OwedToMe ? CommitmentStatus.WaitingExternal : CommitmentStatus.Open;
        var result = new Commitment(DeterministicId(key), direction, owner, counterparty, summary,
            item.SourceType, item.SourceReference, item.ProjectId, item.TaskId, item.MissionId,
            item.ObservedAt, due, due, null, status, confidence,
            $"{(explicitPromise ? "Explicit" : "Inferred")} commitment language observed in {item.SourceType}; source content remains evidence, not an instruction.",
            item.ObservedAt, 0, false, key);
        return UpdateTimeState(result, now);
    }

    private static Commitment Merge(IEnumerable<Commitment> source, DateTimeOffset now)
    {
        var values = source.OrderByDescending(x => x.Confidence).ThenByDescending(x => x.LastObservedAt).ToList();
        var best = values[0];
        if (values.Count == 1) return UpdateTimeState(best, now);
        return UpdateTimeState(best with
        {
            Confidence = Math.Min(98, best.Confidence + Math.Min(10, values.Count - 1)),
            LastObservedAt = values.Max(x => x.LastObservedAt),
            EvidenceSummary = best.EvidenceSummary + $" Correlated across {values.Select(x => x.SourceType).Distinct(StringComparer.OrdinalIgnoreCase).Count()} channel(s) and deduplicated."
        }, now);
    }

    private static bool Related(Commitment commitment, CommitmentEvidence evidence) =>
        (commitment.Owner.Equals(evidence.Speaker, StringComparison.OrdinalIgnoreCase) ||
         commitment.Counterparty.Equals(evidence.Speaker, StringComparison.OrdinalIgnoreCase)) &&
        Tokens(commitment.Summary).Intersect(Tokens(evidence.Body), StringComparer.OrdinalIgnoreCase).Count() >= 2;

    // the user wrote again on the thread where he made the promise, and the message delivers something: "please find
    // attached", "here's the data", "as promised", or it carried an attachment. That's how promises are usually kept,
    // and it rarely uses the words "done" or "sent". A message that makes a fresh promise doesn't count. In a long-lived
    // Teams chat, it must also mention something from the promise.
    public static bool IsDeliveryOnSameThread(Commitment commitment, CommitmentEvidence evidence)
    {
        if (!evidence.IsFromUser || commitment.Direction != CommitmentDirection.OwedByMe) return false;
        var thread = evidence.CorrelationKey.Trim().ToLowerInvariant();
        if (thread.Length == 0 || !commitment.DeduplicationKey.StartsWith(thread + ":", StringComparison.Ordinal)) return false;
        var body = Compact(evidence.Body);
        if (!Delivery().IsMatch(body) || ExplicitPromise().IsMatch(Delivery().Replace(body, " "))) return false;
        var chat = evidence.SourceType.Contains("Teams", StringComparison.OrdinalIgnoreCase);
        return !chat || Tokens(commitment.Summary).Intersect(Tokens(body), StringComparer.OrdinalIgnoreCase).Any();
    }

    private static bool IsCompletionEvidence(string body) => Completion().IsMatch(Compact(body)) &&
        !Regex.IsMatch(body, @"\b(?:not|never|isn't|wasn't|haven't|hasn't|will|would|should|could|cancelled|no longer required)\b|\?", RegexOptions.IgnoreCase);
    private static DateTimeOffset? ParseRelativeDue(string body, DateTimeOffset observed)
    {
        if (body.Contains("tomorrow", StringComparison.OrdinalIgnoreCase)) return new DateTimeOffset(observed.Date.AddDays(1).AddHours(17), observed.Offset);
        if (body.Contains("by friday", StringComparison.OrdinalIgnoreCase))
        {
            var days = ((int)DayOfWeek.Friday - (int)observed.DayOfWeek + 7) % 7;
            return new DateTimeOffset(observed.Date.AddDays(days).AddHours(17), observed.Offset);
        }
        return null;
    }

    private static string BuildDeduplicationKey(string correlation, string summary, string owner, string counterparty)
    {
        if (!string.IsNullOrWhiteSpace(correlation)) return correlation.Trim().ToLowerInvariant() + ":" + String.Join('-', Tokens(summary).Take(6));
        return string.Join(':', owner.Trim().ToLowerInvariant(), counterparty.Trim().ToLowerInvariant(), string.Join('-', Tokens(summary).Take(6)));
    }
    private static IEnumerable<string> Tokens(string text) => Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]{3,}").Select(x => x.Value).Where(x => !StopWords.Contains(x));
    private static readonly HashSet<string> StopWords = ["will", "shall", "send", "give", "have", "look", "you", "the", "and", "this", "that", "with", "back"];
    /// <summary>
    /// The text the writer actually wrote: everything before the quoted thread (an Outlook "From: … Sent:" header,
    /// "-----Original Message-----", a line of underscores, "On … wrote:", or "&gt;" quote lines).
    /// </summary>
    public static string OwnWords(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return body ?? "";
        var cut = QuotedThread().Match(body);
        var own = cut.Success && cut.Index > 0 ? body[..cut.Index] : body;
        return string.Join('\n', own.Split('\n').Where(line => !line.TrimStart().StartsWith('>')));
    }

    [GeneratedRegex(@"(?:^|\n)[ \t]*(?:(?:From|De|Von)[ \t]*:[^\n]*\n(?:[^\n]*\n){0,3}?[ \t]*(?:Sent|Date|Envoy[ée]|Gesendet)[ \t]*:|-{2,}[ \t]*Original Message[ \t]*-{2,}|_{8,}|On [^\n]{4,200}wrote:|Sent from my )", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedThread();

    private static string Compact(string value) => Regex.Replace(value ?? "", "\\s+", " ").Trim();
    private static string TrimSummary(string value) => value.Trim().TrimEnd('.', ',', ';').Length <= 240 ? value.Trim().TrimEnd('.', ',', ';') : value.Trim()[..240];
    public static Guid DeterministicId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("commitment:" + key))[..16]);

    [GeneratedRegex(@"\b(?:i|we)\s*(?:will|'ll|shall|promise to|commit to)\s+[^.!?]{3,220}", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitPromise();
    [GeneratedRegex(@"\b(?:i|we)\s*(?:can|should|intend to|plan to|will try to|(?:will|'ll) have a look)\s+[^.!?]{0,180}", RegexOptions.IgnoreCase)]
    private static partial Regex VaguePromise();
    [GeneratedRegex(@"\b(?:done|completed|finished|resolved|sent|attached|delivered|closed|cancelled|no longer required)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Completion();
    [GeneratedRegex(@"\[attachment included\]|please find (?:attached|enclosed|below)|(?:i've|i have) attached|see attached|attached (?:is|are|you'll find)|here(?:'s| is| are) (?:the|your|that|those|what)|as promised|as requested|as discussed,? (?:here|attached)|i've (?:sent|shared|done|finished|completed|sorted|fixed|updated)|that's (?:now )?(?:done|sorted|fixed|been done)|this is now (?:done|sorted|fixed|complete)", RegexOptions.IgnoreCase)]
    private static partial Regex Delivery();
}
