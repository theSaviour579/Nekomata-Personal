using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian;

public class RiskCalculator
{
    public RiskAssessment Calculate(NekomataWorkspace workspace)
    {
        var risk = new RiskAssessment();

        if (workspace.CurrentMission.BusinessValue >= 25000)
        {
            risk.Level = "High";
            risk.Colour = "#FF5E7E";
            risk.Score = 90;
        }
        else if (workspace.CurrentMission.BusinessValue >= 10000)
        {
            risk.Level = "Medium";
            risk.Colour = "#FFC857";
            risk.Score = 60;
        }
        else
        {
            risk.Level = "Low";
            risk.Colour = "#00E59B";
            risk.Score = 20;
        }

        return risk;
    }
}