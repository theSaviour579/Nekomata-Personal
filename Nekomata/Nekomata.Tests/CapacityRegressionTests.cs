using Xunit;
using Nekomata.Core.Engines;
using Nekomata.Models.Planning;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;

namespace Nekomata.Tests;

public sealed class CapacityRegressionTests
{
    [Fact]
    public void Utilisation_includes_calendar_bookings()
    {
        var capacity = new CapacitySummary
        {
            WorkingMinutesToday = 450,
            ScheduledMinutesToday = 390,
            PlannedMinutesToday = 0
        };

        Assert.Equal(86.67, capacity.UtilisationPercent, 2);
    }

    [Fact]
    public void Utilisation_combines_calendar_and_unscheduled_work()
    {
        var capacity = new CapacitySummary
        {
            WorkingMinutesToday = 450,
            ScheduledMinutesToday = 300,
            PlannedMinutesToday = 90
        };

        Assert.Equal(86.67, capacity.UtilisationPercent, 2);
    }

    [Fact]
    public void Utilisation_is_capped_at_one_hundred_percent()
    {
        var capacity = new CapacitySummary
        {
            WorkingMinutesToday = 450,
            ScheduledMinutesToday = 420,
            PlannedMinutesToday = 120
        };

        Assert.Equal(100, capacity.UtilisationPercent);
    }

    [Fact]
    public void Scheduled_focus_is_removed_from_unscheduled_task_load()
    {
        var settings = new WorkingDaySettings
        {
            StartTime = TimeSpan.Zero,
            EndTime = new TimeSpan(23, 59, 0),
            IncludeLunchBreak = false
        };
        var workspace = new NekomataWorkspace
        {
            Tasks =
            [
                new NekomataTask
                {
                    Id = 42,
                    Title = "Daily Activity Report",
                    DueAt = DateTime.Today,
                    EstimatedMinutes = 90,
                    Status = "Open"
                }
            ],
            Capacity = new CapacitySummary
            {
                ScheduledMinutesToday = 90,
                ScheduledMinutesRemaining = 90,
                ScheduledFocusMinutesToday = 90
            }
        };

        new CapacityEngine(settings).Calculate(workspace);

        Assert.Equal(0, workspace.Capacity.PlannedMinutesToday);
    }

    [Fact]
    public void Unscheduled_due_work_remains_in_planned_minutes()
    {
        var settings = new WorkingDaySettings
        {
            StartTime = TimeSpan.Zero,
            EndTime = new TimeSpan(23, 59, 0),
            IncludeLunchBreak = false
        };
        var workspace = new NekomataWorkspace
        {
            Tasks =
            [
                new NekomataTask
                {
                    Id = 42,
                    Title = "Daily Activity Report",
                    DueAt = DateTime.Today,
                    EstimatedMinutes = 90,
                    Status = "Open"
                }
            ]
        };

        new CapacityEngine(settings).Calculate(workspace);

        Assert.Equal(90, workspace.Capacity.PlannedMinutesToday);
    }
}