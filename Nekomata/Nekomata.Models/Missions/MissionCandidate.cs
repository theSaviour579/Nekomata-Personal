using Nekomata.Models.Common;
using Nekomata.Models.Guardian;

namespace Nekomata.Models.Missions;

public class MissionCandidate
{
    public string SourceType { get; set; } = "None";

    public string? SourceRecordId { get; set; }

    public string ExternalUrl { get; set; } = "";

    public long? TaskId { get; set; }

    public long? ProjectId { get; set; }

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public int Score { get; set; }

    public decimal BusinessValue { get; set; }

    public decimal StrategicBusinessValue { get; set; }

    public int EstimatedMinutes { get; set; }
    public int EnteredEstimateMinutes { get; set; }
    public int PredictedActiveMinutes { get; set; }
    public int InterruptionOverheadMinutes { get; set; }
    public string PredictionExplanation { get; set; } = "";

    public DateTime? DueAt { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public int ExternalStatusId { get; set; }

    public bool IsAwaitingExternalResponse { get; set; }

    public bool IsActionable { get; set; } = true;

    public bool IsOnHold { get; set; }

    public string WaitingOwnerLabel => ExternalStatusId switch
    {
        4 => "WITH END USER",
        28 => "ON HOLD",
        31 => "WEB DEVELOPERS",
        32 => "ERP DEVELOPERS",
        33 => "CRM DEVELOPER",
        34 => "SERVER ADMINISTRATOR",
        36 => "PRINTER MSP",
        _ => "OTHER WAITING"
    };

    public int WaitingBusinessDays
    {
        get
        {
            if (LastUpdatedAt is not DateTime updated || updated >= DateTime.Now) return 0;
            var days = 0;
            for (var date = updated.Date.AddDays(1); date <= DateTime.Today; date = date.AddDays(1))
            {
                if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                    days++;
            }
            return days;
        }
    }

    public string LastUpdatedLabel => LastUpdatedAt is DateTime updated
        ? $"Last updated {updated:ddd dd MMM · HH:mm}"
        : "Last update unavailable";

    public string WaitingAgeLabel => WaitingBusinessDays switch
    {
        0 => "Updated today",
        1 => "Waiting 1 working day",
        _ => $"Waiting {WaitingBusinessDays} working days"
    };

    public string WatchlistStateLabel => IsActionable ? "CHASE DUE" : "MONITORING";

    public double Progress { get; set; }

    public bool AtRisk { get; set; }

    public bool RequiresImmediateAttention { get; set; }

    public bool IsUrgent { get; set; }

    public string RecommendationReason { get; set; } = "";
    public string SuggestedOwner { get; set; } = "";
    public string CurrentOwner { get; set; } = "";
    public int AssignmentConfidence { get; set; }
    public string AssignmentReason { get; set; } = "";
    public string CurrentOwnerLabel => string.IsNullOrWhiteSpace(CurrentOwner) ? "Unassigned" : CurrentOwner;
    public string SuggestedOwnerLabel => string.IsNullOrWhiteSpace(SuggestedOwner) ? "No recommendation" : SuggestedOwner;
    public string AssignmentDispositionLabel => string.IsNullOrWhiteSpace(SuggestedOwner)
        ? "NO MATCH"
        : SuggestedOwner.Equals(CurrentOwner, StringComparison.OrdinalIgnoreCase) ? "KEEP CURRENT" : "ALTERNATIVE OWNER";
    public string AssignmentOwnerDisplay => string.IsNullOrWhiteSpace(SuggestedOwner)
        ? "No skill-based recommendation"
        : SuggestedOwner;
    public string AssignmentConfidenceLabel => string.IsNullOrWhiteSpace(SuggestedOwner)
        ? "Add or refine skill mappings"
        : AssignmentConfidence >= 75 ? "Strong evidence"
        : AssignmentConfidence >= 55 ? "Moderate evidence"
        : "Weak evidence";
    public bool CanReassignOwner => !string.IsNullOrWhiteSpace(SuggestedOwner) &&
        !SuggestedOwner.Equals(CurrentOwner, StringComparison.OrdinalIgnoreCase);
    public string DueLabel => DueAt is DateTime due ? $"Due {due:ddd dd MMM · HH:mm}" : "No due date";

    public List<MissionScoreFactor> ScoreFactors { get; set; } = [];

    public int BaseScore { get; set; }

    public List<string> Strengths { get; set; } = [];
    public List<GuardianReason> GuardianReasons { get; set; } = [];

    public List<string> Risks { get; set; } = [];

    public string GuardianDecision { get; set; } = "";

    public int Rank { get; set; }
    public List<Guid> AppliedPolicyIds { get; set; } = [];
    public int RoleAlignmentScoreAdjustment { get; set; }
    public string RoleAlignmentRecommendationSuffix { get; set; } = "";
    public string Priority { get; set; } =
    TaskPriorities.Normal;

    public int Urgency { get; set; }

    public int RiskScore { get; set; }

    public MissionScoreBreakdown
    ScoreBreakdown
    { get; set; }
    = new();

    public IReadOnlyList<MissionScoreGroup>
      GroupedScoreFactors
    {
        get
        {
            System.Diagnostics.Debug.WriteLine(
                $"GroupedScoreFactors: {Title} has {ScoreFactors.Count} factors.");

            return ScoreFactors
                .GroupBy(f => f.Category)
                .Select(group =>
                    new MissionScoreGroup
                    {
                        Category = group.Key,
                        TotalPoints = group.Sum(f => f.Points),
                        Factors = group.ToList()
                    })
                .ToList();
        }
    }
}
