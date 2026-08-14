using Nekomata.Models.AI;
using Nekomata.Models.Guardian;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
namespace Nekomata.Core.Engines;

public class FocusEngine : IFocusEngine
{
    public NekomataWorkspace BuildFocus(NekomataWorkspace workspace)
    {
        foreach (var task in workspace.Tasks)
        {
            var breakdown = CalculateBreakdown(task);

            task.ScoreBreakdown = breakdown;
            task.PriorityScore = breakdown.FinalScore;
        }

        workspace.Tasks = workspace.Tasks
            .Where(t => t.Status.Equals("Open", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.PriorityScore)
            .ThenBy(t => t.DueAt)
            .ToList();

        var topTasks = workspace.Tasks.Take(3).ToList();

        workspace.Focus.TopPriorities = topTasks
            .Select(t => $"{t.Title} — score {t.PriorityScore}")
            .ToList();

        workspace.Focus.PrimaryFocus = topTasks.FirstOrDefault()?.Title
            ?? "No priority tasks found.";

        return workspace;
    }

    private static MissionScoreBreakdown CalculateBreakdown(NekomataTask task)
    {
        double score = 0;

        var commercial = (int)Math.Round(((double)task.EstimatedBusinessValue / 30000d) * 50d);
        var revenue = task.RevenueImpact * 4;
        var customer = task.CustomerImpact * 2;
        var executive = task.ExecutiveVisibility * 2;
        var automation = task.AutomationPotential * 2;
        var focus = task.RequiresFocus ? 5 : 0;
        var sql = task.RequiresSql ? 3 : 0;
        var interruptible = task.Interruptible ? -5 : 0;
        var recurring = task.Recurring ? -5 : 0;
        var duration = -(int)Math.Round(task.EstimatedMinutes / 60.0);
        var positives = new List<string>();

        if (commercial > 40)
            positives.Add("Exceptional commercial opportunity");

        if (task.RevenueImpact >= 4)
            positives.Add("High revenue impact");

        if (task.ExecutiveVisibility >= 4)
            positives.Add("Executive visibility");

        if (task.RequiresFocus)
            positives.Add("Requires uninterrupted focus");

        if (task.AutomationPotential >= 4)
            positives.Add("Strong automation opportunity");

        var negatives = new List<string>();

        if (task.EstimatedMinutes >= 180)
            negatives.Add("Long duration");

        if (task.Interruptible)
            negatives.Add("Can be interrupted");

        if (task.Recurring)
            negatives.Add("Recurring work");

        var recommendation =
    $"Complete {task.Title} before lower-value work.";

        score += commercial;
        score += revenue;
        score += customer;
        score += executive;
        score += automation;
        score += focus;
        score += sql;
        score += interruptible;
        score += recurring;
        score += duration;

        return new MissionScoreBreakdown
        {
            Commercial = commercial,
            RevenueImpact = revenue,
            CustomerImpact = customer,
            ExecutiveVisibility = executive,
            Automation = automation,
            FocusBonus = focus,
            SqlBonus = sql,
            InterruptiblePenalty = interruptible,
            RecurringPenalty = recurring,
            DurationPenalty = duration,
            FinalScore = Math.Clamp((int)Math.Round(score), 0, 100),

            Positives = positives,
            Negatives = negatives,
            Recommendation = recommendation
        };
    }
}