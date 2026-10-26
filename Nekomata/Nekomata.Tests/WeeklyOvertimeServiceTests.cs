using System.Text.Json;
using Nekomata.Core.Analytics;
using Nekomata.Core.Missions;
using Nekomata.Data.Repositories;
using Nekomata.Models.Analytics;
using Nekomata.Models.Missions;
using Nekomata.Models.Planning;
using Xunit;

namespace Nekomata.Tests;

public sealed class WeeklyOvertimeServiceTests
{
    [Fact]
    public async Task CountsBeforeLunchAfterAndWeekendFromExactWorkIntervals()
    {
        var intervals = new[]
        {
            new MissionWorkSegment(new DateTime(2026, 9, 7, 7, 30, 0), new DateTime(2026, 9, 7, 8, 0, 0)),
            new MissionWorkSegment(new DateTime(2026, 9, 7, 12, 10, 0), new DateTime(2026, 9, 7, 12, 30, 0)),
            new MissionWorkSegment(new DateTime(2026, 9, 7, 16, 30, 0), new DateTime(2026, 9, 7, 17, 10, 0)),
            new MissionWorkSegment(new DateTime(2026, 9, 12, 9, 0, 0), new DateTime(2026, 9, 12, 10, 0, 0))
        };
        var session = new MissionSession
        {
            StartedAt = intervals[0].Start, FinishedAt = intervals[^1].End,
            ActualDurationMinutes = 150, ActiveIntervalsJson = JsonSerializer.Serialize(intervals)
        };
        var service = Create(session);

        var result = await service.GetCurrentWeekAsync(new DateTime(2026, 9, 12, 18, 0, 0));

        Assert.Equal(150, result.TotalMinutes);
        Assert.Equal(30, result.BeforeHoursMinutes);
        Assert.Equal(20, result.LunchMinutes);
        Assert.Equal(40, result.AfterHoursMinutes);
        Assert.Equal(60, result.WeekendMinutes);
        Assert.Equal(50.78m, result.EquivalentCost);
        Assert.Equal(2, result.Days.Count);
    }

    [Fact]
    public async Task HistoricalSessionUsesRecordedDurationRatherThanCountingPausedElapsedTime()
    {
        var service = Create(new MissionSession
        {
            StartedAt = new DateTime(2026, 9, 7, 16, 0, 0),
            FinishedAt = new DateTime(2026, 9, 7, 18, 0, 0),
            ActualDurationMinutes = 60,
            ActiveIntervalsJson = "[]"
        });

        var result = await service.GetCurrentWeekAsync(new DateTime(2026, 9, 11, 18, 0, 0));

        Assert.Equal(45, result.AfterHoursMinutes);
        Assert.Equal(45, result.TotalMinutes);
    }

    private static WeeklyOvertimeService Create(params MissionSession[] sessions) =>
        new(new FakeSessions(sessions), new WorkingDaySettings(), new LabourCostSettings { HourlyLabourRate = 20.31m });

    private sealed class FakeSessions(IEnumerable<MissionSession> sessions) : IMissionSessionRepository
    {
        private readonly List<MissionSession> _sessions = sessions.ToList();
        public Task SaveAsync(MissionSession session) => Task.CompletedTask;
        public Task<List<MissionSession>> GetRecentAsync(int count) => Task.FromResult(_sessions.Take(count).ToList());
        public Task<List<MissionSession>> GetTodayAsync() => Task.FromResult(_sessions.ToList());
        public Task<List<MissionSession>> GetAllAsync() => Task.FromResult(_sessions.ToList());
        public Task<List<MissionSession>> GetBetweenAsync(DateTime from, DateTime to) =>
            Task.FromResult(_sessions.Where(item => item.FinishedAt >= from && item.FinishedAt < to).ToList());
    }
}
