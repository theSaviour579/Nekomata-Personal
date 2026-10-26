using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public class GuardianDayReviewServiceTests
{
    [Fact]
    public void Build_ClosesTheDayWithMeasuredWorkAndRollForward()
    {
        var workspace = new NekomataWorkspace
        {
            Capacity = new CapacitySummary { PlannedMinutesToday = 370, ScheduledFocusMinutesToday = 300 },
            Tasks = [new NekomataTask { Status = "Open", DueAt = DateTime.Today }]
        };
        var sessions = new[]
        {
            new MissionSession { Title = "SQL analysis", SourceType = "Task", Completed = true, ActualDurationMinutes = 105, StartedAt = DateTime.Today.AddHours(8) },
            new MissionSession { Title = "Urgent client email", SourceType = "Email", Completed = true, ActualDurationMinutes = 42, StartedAt = DateTime.Today.AddHours(11) }
        };
        var interruptions = new[] { new GuardianInterruption { LostMinutes = 27 } };

        var review = GuardianDayReviewService.Build(workspace, sessions, interruptions);

        Assert.Equal(370, review.PlannedMinutes);
        Assert.Equal(147, review.CompletedMinutes);
        Assert.Equal(42, review.UnplannedMinutes);
        Assert.Equal(27, review.InterruptionMinutes);
        Assert.Equal(1, review.RollForwardCount);
    }
}
