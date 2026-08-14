using Nekomata.Models.Tasks;

namespace Nekomata.Data.Seed;

public static class TaskSeed
{
    public static IReadOnlyList<NekomataTask> GetTasks()
    {
        return
        [
            new NekomataTask
            {
                Title = "Osgo Pricing Review",
                Description = "Review supplier pricing and identify margin opportunities.",
                Source = "Planner",
                Status = "Open",
                Owner = "David",
                EstimatedMinutes = 240,
                EstimatedBusinessValue = 30000,
                RevenueImpact = 5,
                CustomerImpact = 4,
                ExecutiveVisibility = 5,
                AutomationPotential = 3,
                RequiresSql = true,
                RequiresFocus = true,
                Interruptible = false,
                Recurring = false,
                Category = "Pricing",
                Tags = "supplier,pricing,margin"
            },

            new NekomataTask
            {
                Title = "Falling Sales and Pricing",
                Description = "Analyse customers or products showing declining sales and pricing pressure.",
                Source = "Planner",
                Status = "Open",
                Owner = "David",
                EstimatedMinutes = 180,
                EstimatedBusinessValue = 28000,
                RevenueImpact = 5,
                CustomerImpact = 5,
                ExecutiveVisibility = 4,
                AutomationPotential = 5,
                RequiresSql = true,
                RequiresFocus = true,
                Interruptible = false,
                Category = "Analytics",
                Tags = "sales,pricing,sql"
            },

            new NekomataTask
            {
                Title = "Roam Zone Analysis",
                Description = "Analyse Roam Zone performance and commercial opportunities.",
                Source = "Planner",
                Status = "Open",
                Owner = "David",
                EstimatedMinutes = 180,
                EstimatedBusinessValue = 25000,
                RevenueImpact = 4,
                CustomerImpact = 4,
                ExecutiveVisibility = 4,
                AutomationPotential = 5,
                RequiresSql = true,
                RequiresFocus = true,
                Interruptible = false,
                Category = "Analytics",
                Tags = "roam,zones,sales"
            },

            new NekomataTask
            {
                Title = "YoY Spend Deep Dive",
                Description = "Deep dive year-on-year spend trends for management insight.",
                Source = "Planner",
                Status = "Open",
                Owner = "David",
                EstimatedMinutes = 180,
                EstimatedBusinessValue = 22000,
                RevenueImpact = 4,
                CustomerImpact = 3,
                ExecutiveVisibility = 5,
                AutomationPotential = 4,
                RequiresSql = true,
                RequiresFocus = true,
                Interruptible = false,
                Category = "Reporting",
                Tags = "yoy,spend,management"
            },

            new NekomataTask
            {
                Title = "Rebates",
                Description = "Review rebate position and related financial impact.",
                Source = "Planner",
                Status = "Open",
                Owner = "David",
                EstimatedMinutes = 120,
                EstimatedBusinessValue = 20000,
                RevenueImpact = 4,
                CustomerImpact = 2,
                ExecutiveVisibility = 4,
                AutomationPotential = 2,
                RequiresSql = false,
                RequiresFocus = true,
                Interruptible = false,
                Category = "Finance",
                Tags = "rebates,finance"
            },

            new NekomataTask
            {
                Title = "PP Reporting",
                Description = "Complete PP reporting for business review.",
                Source = "Planner",
                Status = "Open",
                Owner = "David",
                EstimatedMinutes = 90,
                EstimatedBusinessValue = 18000,
                RevenueImpact = 3,
                CustomerImpact = 3,
                ExecutiveVisibility = 5,
                AutomationPotential = 5,
                RequiresSql = true,
                RequiresFocus = true,
                Interruptible = true,
                Recurring = true,
                Category = "Reporting",
                Tags = "pp,reporting,weekly"
            },

            new NekomataTask
            {
                Title = "Daily Sales Query Issue",
                Description = "Resolve issue affecting daily sales query visibility.",
                Source = "Planner",
                Status = "Open",
                Owner = "David",
                EstimatedMinutes = 45,
                EstimatedBusinessValue = 16000,
                RevenueImpact = 4,
                CustomerImpact = 4,
                ExecutiveVisibility = 4,
                AutomationPotential = 5,
                RequiresSql = true,
                RequiresFocus = false,
                Interruptible = true,
                Recurring = true,
                Category = "Support",
                Tags = "sales,query,daily"
            }
        ];
    }
}