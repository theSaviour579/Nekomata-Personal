namespace Nekomata.Models.Guardian;

public sealed class GuardianPolicy
{
    public Guid Id { get; set; }
    public string PolicyKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Statement { get; set; } = "";
    public string Action { get; set; } = "";
    public string WorkType { get; set; } = "";
    public string Lifecycle { get; set; } = "Candidate";
    public int ConfidencePercent { get; set; }
    public int EvidenceCount { get; set; }
    public DateTimeOffset FirstObservedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public int AppliedCount { get; set; }
    public int SuccessfulCount { get; set; }
    public int NeutralCount { get; set; }
    public int AdverseCount { get; set; }
    public int CounterfactualCount { get; set; }
    public int StrategicMinutesGained { get; set; }
    public string EvidenceTrend { get; set; } = "→ stable";
    public bool IsActive => Lifecycle == "Active";
    public bool IsCandidate => Lifecycle == "Candidate";
    public bool IsLearnedCandidate => IsCandidate && PolicyKey.StartsWith("learned:", StringComparison.OrdinalIgnoreCase);
    public string EffectivenessSummary => $"Since activation: applied to {AppliedCount} decisions · {SuccessfulCount} successful · {NeutralCount} neutral · {AdverseCount} adverse · {StrategicMinutesGained / 60}h {StrategicMinutesGained % 60:00}m strategic work completed under policy";
    public string AccountabilitySummary => Lifecycle switch
    {
        "Candidate" => $"Pre-activation evidence only · {EvidenceCount} observations · no planning influence yet",
        "Retired" or "Superseded" => $"Historical effectiveness: {AppliedCount} applications · {SuccessfulCount} successful · {NeutralCount} neutral · {AdverseCount} adverse",
        _ => EffectivenessSummary
    };
}
