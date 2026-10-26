namespace Nekomata.Models.Guardian;

public enum GuardianInterruptionRoute { Immediate, NaturalBreak, Briefing, Suppress }

public sealed class GuardianInterruptionSignal
{
    public string Source { get; set; } = "Guardian";
    public string Severity { get; set; } = "Info";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string ActionKind { get; set; } = "";
    public bool FocusActive { get; set; }
    public bool CalendarCommitmentActive { get; set; }
    public string CurrentObjective { get; set; } = "";
    public int RemainingFocusMinutes { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public bool ExplicitlyRaisedByUser { get; set; }

    /// <summary>
    /// the user's learned preference for this kind of item ("that wasn't important" / "always tell me about those"),
    /// added to the score. Never applied to demote critical items or incident language.
    /// </summary>
    public int PreferenceAdjustment { get; set; }
}

public sealed record GuardianInterruptionDecision(
    GuardianInterruptionRoute Route,
    int PriorityScore,
    int ConfidencePercent,
    string Explanation,
    IReadOnlyList<string> Evidence);
