using Nekomata.Core.Analytics;
using Nekomata.Data.Repositories;
using Nekomata.Models.Missions;
using Nekomata.Models.Analytics;
using Nekomata.Models.Planning;
using Xunit;

namespace Nekomata.Tests;

public sealed class MissionAnalyticsServiceTests
{
    [Fact]
    public async Task Deferred_focus_counts_as_delivered_time_without_counting_as_completion()
    {
        var service = new MissionAnalyticsService(new FakeSessions(
            new MissionSession
            {
                Title = "UI/UX focus",
                ActualDurationMinutes = 17,
                BusinessValue = 500,
                Completed = false,
                Cancelled = false
            }));

        var result = await service.GetTodayAsync();

        Assert.Equal(TimeSpan.FromMinutes(17), result.FocusTimeToday);
        Assert.Equal(0, result.MissionsCompletedToday);
        Assert.Equal(0, result.BusinessValueDeliveredToday);
    }

    [Fact]
    public async Task Cancelled_time_is_not_reported_as_delivered_focus()
    {
        var service = new MissionAnalyticsService(new FakeSessions(
            new MissionSession { ActualDurationMinutes = 12, Cancelled = true }));

        var result = await service.GetTodayAsync();

        Assert.Equal(TimeSpan.Zero, result.FocusTimeToday);
    }

    [Fact]
    public async Task CalculatesElapsedWorkdayWageCostAndValueAfterWages()
    {
        var service = new MissionAnalyticsService(new FakeSessions(
            new MissionSession
            {
                ActualDurationMinutes = 90,
                BusinessValue = 250,
                Completed = true
            }), new LabourCostSettings { HourlyLabourRate = 20.31m }, new WorkingDaySettings(),
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 10, 9, 30, 0, TimeSpan.Zero)));

        var result = await service.GetTodayAsync();

        Assert.Equal(30.47m, result.WageCostToday);
        Assert.Equal(219.53m, result.ValueAfterWageCostToday);
        Assert.Equal(20.31m, result.HourlyLabourRate);
        Assert.Equal(8.20m, result.BusinessValueToWageCostRatio);
        Assert.Equal(TimeSpan.FromMinutes(90), result.ElapsedPaidWorkdayToday);
    }

    [Fact]
    public async Task WageCostAccruesWithoutAFocusSessionAndExcludesLunch()
    {
        var service = new MissionAnalyticsService(
            new FakeSessions(),
            new LabourCostSettings { HourlyLabourRate = 20.31m },
            new WorkingDaySettings(),
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 10, 13, 0, 0, TimeSpan.Zero)));

        var result = await service.GetTodayAsync();

        Assert.Equal(TimeSpan.FromHours(4), result.ElapsedPaidWorkdayToday);
        Assert.Equal(81.24m, result.WageCostToday);
        Assert.Equal(-81.24m, result.ValueAfterWageCostToday);
    }

    [Fact]
    public void ElapsedPaidTimeStopsAtTheConfiguredWorkdayEnd()
    {
        var settings = new WorkingDaySettings();

        var elapsed = LabourCostCalculator.GetElapsedPaidWorkday(
            new DateTime(2026, 9, 10, 20, 0, 0), settings);

        Assert.Equal(TimeSpan.FromHours(7.5), elapsed);
    }

    private sealed class FakeSessions(params MissionSession[] sessions) : IMissionSessionRepository
    {
        public Task SaveAsync(MissionSession session) => Task.CompletedTask;
        public Task<List<MissionSession>> GetRecentAsync(int count) => Task.FromResult(sessions.Take(count).ToList());
        public Task<List<MissionSession>> GetTodayAsync() => Task.FromResult(sessions.ToList());
        public Task<List<MissionSession>> GetAllAsync() => Task.FromResult(sessions.ToList());
        public Task<List<MissionSession>> GetBetweenAsync(DateTime from, DateTime to) => Task.FromResult(sessions.ToList());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
