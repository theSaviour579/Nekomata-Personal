using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian;

public class MissionPriorityCalculator
{
    public int Calculate(NekomataTask task)
    {
        double score = 0;

        score += ((double)task.EstimatedBusinessValue / 30000d) * 50d;

        // Business impact (30%)
        score += task.RevenueImpact * 4;
        score += task.CustomerImpact * 2;
        score += task.ExecutiveVisibility * 2;
        score += task.AutomationPotential * 2;

        // Work characteristics (20%)
        if (task.RequiresFocus)
            score += 5;

        if (task.RequiresSql)
            score += 3;

        if (task.Interruptible)
            score -= 5;

        if (task.Recurring)
            score -= 5;

        // Duration penalty
        score -= task.EstimatedMinutes / 60.0;

        return Math.Clamp((int)Math.Round(score), 0, 100);
    }

    public int Calculate(NekomataWorkspace workspace)
    {
        var task = workspace.Tasks
            .OrderByDescending(Calculate)
            .FirstOrDefault();

        return task is null ? 0 : Calculate(task);
    }

    private static int BusinessValueScore(decimal value)
    {
        if (value >= 30000) return 35;
        if (value >= 25000) return 32;
        if (value >= 20000) return 28;
        if (value >= 15000) return 22;
        if (value >= 10000) return 16;
        if (value >= 5000) return 8;

        return 0;
    }

    private static int DurationPenalty(int minutes)
    {
        if (minutes >= 240) return 8;
        if (minutes >= 180) return 5;
        if (minutes >= 120) return 3;

        return 0;
    }
}