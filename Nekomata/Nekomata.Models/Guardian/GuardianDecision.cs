namespace Nekomata.Models.Guardian;

public class GuardianDecision
{
    // Guardian's overall recommendation
    public string Headline { get; set; } = "";

    public string Recommendation { get; set; } = "";

    public string Summary { get; set; } = "";

    public int Confidence { get; set; }

    // Why Guardian made this decision
    public List<GuardianReason> Reasons { get; } = [];

    public List<DecisionRisk> Risks { get; } = [];

    public List<GuardianOpportunity> Opportunities { get; } = [];

    // Why other missions lost
    public List<GuardianRejectedMission> RejectedMissions { get; } = [];
}