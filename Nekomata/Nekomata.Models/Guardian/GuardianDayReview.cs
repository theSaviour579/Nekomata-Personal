namespace Nekomata.Models.Guardian;

public sealed class GuardianDayReview
{
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int PlannedMinutes { get; set; }
    public int CompletedMinutes { get; set; }
    public int InterruptionCount { get; set; }
    public int InterruptionMinutes { get; set; }
    public int UnplannedMinutes { get; set; }
    public int FocusBlocksProtected { get; set; }
    public int FocusBlocksPlanned { get; set; }
    public int ObjectivesCompleted { get; set; }
    public int ObjectivesPlanned { get; set; }
    public int RollForwardCount { get; set; }
    public string LearnedSummary { get; set; } = "Not enough evidence for a new personal correction today.";
    public string TomorrowSummary { get; set; } = "Guardian will preserve unfinished commitments for tomorrow.";
    public TimeSpan? StrongestWindowStart { get; set; }
    public string PlannedLabel => Format(PlannedMinutes);
    public string CompletedLabel => Format(CompletedMinutes);
    private static string Format(int minutes) => $"{minutes / 60}h {minutes % 60:00}m";
}
