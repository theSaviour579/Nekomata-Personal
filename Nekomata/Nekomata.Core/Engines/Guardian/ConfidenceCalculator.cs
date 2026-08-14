using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian;

public class ConfidenceCalculator
{
    public int Calculate(NekomataWorkspace workspace)
    {
        int confidence = 100;

        if (workspace.Capacity.IsOverCapacity)
            confidence -= 10;

        if (workspace.CurrentMission.BusinessValue > 25000)
            confidence += 5;

        return Math.Clamp(confidence, 0, 100);
    }
}