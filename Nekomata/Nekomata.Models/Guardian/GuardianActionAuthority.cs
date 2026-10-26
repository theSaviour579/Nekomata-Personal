namespace Nekomata.Models.Guardian;

public enum GuardianActionAuthority { Observe, Recommend, Prepare, AutoExecute, ApprovalRequired, Prohibited }
public enum GuardianActionRisk { Low, Medium, High, Critical }
public enum GuardianActionReversibility { Reversible, Compensatable, Irreversible }
public enum GuardianActionImpact { Informational, Internal, External, Sensitive }

public sealed class GuardianActionAuthorityRequest
{
    public string ActionType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public int Confidence { get; set; }
    public bool IsDeterministic { get; set; }
    public GuardianActionRisk Risk { get; set; } = GuardianActionRisk.Low;
    public GuardianActionReversibility Reversibility { get; set; } = GuardianActionReversibility.Reversible;
    public bool ChangesExternalSystem { get; set; }
    public bool AffectsAnotherPerson { get; set; }
    public bool WaivesCompliance { get; set; }
    public bool IsProhibited { get; set; }
    public bool IsPreAuthorized { get; set; }
    public GuardianActionImpact Impact { get; set; } = GuardianActionImpact.Internal;
    public string UndoDescription { get; set; } = "";
    public List<string> Evidence { get; set; } = [];
    public Guid SourceEventId { get; set; }
    public Guid CorrelationId { get; set; }
    public long? ProjectId { get; set; }
    public long? TaskId { get; set; }
}

public sealed class GuardianActionAuthorityDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ActionType { get; set; } = "";
    public GuardianActionAuthority Authority { get; set; }
    public GuardianActionRisk Risk { get; set; }
    public GuardianActionReversibility Reversibility { get; set; }
    public int Confidence { get; set; }
    public string Reason { get; set; } = "";
    public string PolicyExplanation { get; set; } = "";
    public string UndoDescription { get; set; } = "";
    public List<string> Evidence { get; set; } = [];
    public Guid SourceEventId { get; set; }
    public bool CanExecute => Authority == GuardianActionAuthority.AutoExecute;
    public bool RequiresApproval => Authority == GuardianActionAuthority.ApprovalRequired;
    public bool RequiresHumanJudgement => Authority is GuardianActionAuthority.Recommend or
        GuardianActionAuthority.Prepare or GuardianActionAuthority.ApprovalRequired or GuardianActionAuthority.Prohibited;
    public string BehaviourLabel => Authority switch
    {
        GuardianActionAuthority.Observe => "Surface evidence",
        GuardianActionAuthority.Recommend => "Recommend only",
        GuardianActionAuthority.Prepare => "Prepare for approval",
        GuardianActionAuthority.AutoExecute => "Execute within policy",
        GuardianActionAuthority.ApprovalRequired => "Explicit approval required",
        _ => "Blocked by policy"
    };
}
