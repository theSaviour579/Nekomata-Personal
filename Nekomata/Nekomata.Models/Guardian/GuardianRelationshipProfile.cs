namespace Nekomata.Models.Guardian;

public sealed class GuardianRelationshipObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ContactAddress { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Channel { get; set; } = "Email";
    public string ConversationId { get; set; } = "";
    public int EvidenceQuality { get; set; } = 50;
    public int MessageCount { get; set; }
    public int SubstantiveReplyCount { get; set; }
    public int AcknowledgementCount { get; set; }
    public int ResponseCount { get; set; }
    public long TotalResponseMinutes { get; set; }
    public int ChaseResponseCount { get; set; }
    public int PromiseCount { get; set; }
    public int DetailedMessageCount { get; set; }
    public int ConciseMessageCount { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GuardianRelationshipProfile
{
    public string ContactAddress { get; init; } = "";
    public string ContactName { get; init; } = "";
    public string Channel { get; init; } = "Email";
    public bool IsExcluded { get; init; }
    public string PreferenceNote { get; init; } = "";
    public int ConversationCount { get; init; }
    public int MessageCount { get; init; }
    public int SubstantiveReplyCount { get; init; }
    public int AcknowledgementCount { get; init; }
    public int ResponseCount { get; init; }
    public long TotalResponseMinutes { get; init; }
    public int ChaseResponseCount { get; init; }
    public int PromiseCount { get; init; }
    public int DetailedMessageCount { get; init; }
    public int ConciseMessageCount { get; init; }
    public DateTimeOffset? LastMessageAt { get; init; }

    public string DisplayLabel => $"{(string.IsNullOrWhiteSpace(ContactName) ? ContactAddress : ContactName)} · {Channel}" +
                                  (IsExcluded ? " · EXCLUDED" : "");

    public bool HasEnoughEvidence => ConversationCount >= 2 && ResponseCount >= 3;
    public double AverageResponseMinutes => ResponseCount == 0 ? 0 : (double)TotalResponseMinutes / ResponseCount;
    public int Confidence => HasEnoughEvidence
        ? Math.Clamp(35 + ConversationCount * 8 + ResponseCount * 4, 0, 90)
        : Math.Clamp(ConversationCount * 12 + ResponseCount * 5, 0, 49);
    public int RecommendedWorkingDayWait => !HasEnoughEvidence
        ? 2
        : Math.Clamp((int)Math.Ceiling(AverageResponseMinutes / 1440d) + 1, 2, 5);
    public string CommunicationStyle => DetailedMessageCount > ConciseMessageCount * 2
        ? "detailed"
        : ConciseMessageCount > DetailedMessageCount * 2
            ? "concise"
            : "mixed";

    public string EvidenceSummary
    {
        get
        {
            if (!HasEnoughEvidence)
                return $"Relationship learning: {ConversationCount} conversation(s), {ResponseCount} measured reply/replies; normal chase timing remains in use.";

            var typical = AverageResponseMinutes < 24 * 60
                ? $"{Math.Max(1, (int)Math.Round(AverageResponseMinutes / 60d))} hours"
                : $"{AverageResponseMinutes / 1440d:0.#} days";
            return $"Relationship evidence ({Confidence}% confidence): {ResponseCount} measured replies across {ConversationCount} conversations, average response about {typical}; communication style is {CommunicationStyle}. Allow {RecommendedWorkingDayWait} working days before a routine chase.";
        }
    }
}

public sealed record GuardianRelationshipDelayInsight(
    string ContactAddress,
    string ContactName,
    string ConversationId,
    string Subject,
    DateTimeOffset LastSentAt,
    DateTimeOffset ExpectedBy,
    int Confidence);

public sealed record GuardianRelationshipObservationSummary(
    int ConversationsObserved,
    IReadOnlyList<GuardianRelationshipDelayInsight> UnusualDelays);
