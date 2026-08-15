namespace Nekomata.Models.Tasks;

using Nekomata.Models.Guardian;
public class NekomataTask
{
    public long Id { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public string Source { get; set; } = "Manual";
    public string Status { get; set; } = "Open";
    public string Priority { get; set; } = "Normal";

    public string Owner { get; set; } = "";
    public string? SuggestedDelegate { get; set; }

    public bool BusinessCritical { get; set; }
    public bool AccuracySensitive { get; set; }

    public int EstimatedMinutes { get; set; } = 30;
    public DateTime? DueAt { get; set; }

    public int PriorityScore { get; set; }
    public DateTime? StartAt { get; set; }

    public DateTime? FinishAt { get; set; }

    public bool CanDelegate { get; set; }

    public bool IsScheduled { get; set; }

    public string? BlockingReason { get; set; }

    public int ActualMinutes { get; set; }

    public bool Completed { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int ContextSwitchCost { get; set; }

    public string Context { get; set; } = "General";

    // ---------- Guardian Intelligence ----------

    public decimal EstimatedBusinessValue { get; set; }

    public int RevenueImpact { get; set; }          // 0-5

    public int CustomerImpact { get; set; }         // 0-5

    public int ExecutiveVisibility { get; set; }    // 0-5

    public int AutomationPotential { get; set; }    // 0-5

    public bool RequiresSql { get; set; }

    public bool RequiresHalo { get; set; }

    public bool RequiresOutlook { get; set; }

    public bool RequiresFocus { get; set; }

    public bool Interruptible { get; set; }

    public bool Recurring { get; set; }

    public string Category { get; set; } = "";

    public string Tags { get; set; } = "";

    public MissionScoreBreakdown ScoreBreakdown { get; set; } = new();

    public long? ProjectId { get; set; }

    public bool IsCompleted =>
        string.Equals(
            Status,
            "Completed",
            StringComparison.OrdinalIgnoreCase);
}
