using System.Collections.Generic;

namespace Nekomata.Models.Guardian;

public class GuardianState
{
    public int MissionScore { get; set; }

    public int Confidence { get; set; }

    public string Summary { get; set; } = "";

    public string Recommendation { get; set; } = "";

    public string RiskLevel { get; set; } = "";

    public decimal EstimatedValue { get; set; }

    public TimeSpan EstimatedDuration { get; set; }

    public DateTime? StartBefore { get; set; }

    public List<string> Reasons { get; set; } = [];

    public List<GuardianAdvice> Advice { get; set; } = [];
}