using Nekomata.Data.Repositories;
using Nekomata.Models.Analytics;
using Nekomata.Models.Missions;
using Nekomata.Models.Planning;

namespace Nekomata.Core.Analytics;

public class MissionAnalyticsService
    : IMissionAnalyticsService
{
    private readonly IMissionSessionRepository _repository;
    private readonly LabourCostSettings _costing;
    private readonly WorkingDaySettings _workingDay;
    private readonly TimeProvider _timeProvider;

    public MissionAnalyticsService(
        IMissionSessionRepository repository,
        LabourCostSettings? costing = null,
        WorkingDaySettings? workingDay = null,
        TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _costing = costing ?? new LabourCostSettings();
        _workingDay = workingDay ?? new WorkingDaySettings();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MissionAnalytics> GetTodayAsync()
    {
        var sessions =
            await _repository.GetTodayAsync();

        var completed =
            sessions
                .Where(x => x.Completed)
                .ToList();

        // A deferred session still represents focus already delivered. Completion
        // controls outcome/value metrics, but must not erase elapsed working time.
        var focused = sessions
            .Where(x => !x.Cancelled)
            .ToList();

        double estimateAccuracy = 100;

        if (completed.Any())
        {
            estimateAccuracy =
                completed.Average(session =>
                {
                    if (session.EstimatedDurationMinutes <= 0)
                        return 100;

                    var difference =
                        Math.Abs(
                            session.ActualDurationMinutes -
                            session.EstimatedDurationMinutes);

                    var accuracy =
                        100 -
                        ((double)difference /
                         session.EstimatedDurationMinutes * 100);

                    return Math.Clamp(
                        accuracy,
                        0,
                        100);
                });
        }

        var focusedMinutes = focused.Sum(x => Math.Max(0, x.ActualDurationMinutes));
        var deliveredValue = completed.Sum(x => x.BusinessValue);

        var analytics = new MissionAnalytics
        {
            MissionsCompletedToday =
                completed.Count,

            MissionsCancelledToday =
                sessions.Count(x => x.Cancelled),

            FocusTimeToday =
                TimeSpan.FromMinutes(
                    focusedMinutes),

            BusinessValueDeliveredToday =
                deliveredValue,

            AverageScoreToday =
                completed.Any()
                    ? completed.Average(x => x.Score)
                    : 0,

            EstimateAccuracyPercent =
                estimateAccuracy,

            AverageMissionDuration =
                completed.Any()
                    ? TimeSpan.FromMinutes(
                        completed.Average(x =>
                            x.ActualDurationMinutes))
                    : TimeSpan.Zero,

            HighestScoringMission =
                completed
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault(),

            LongestMission =
                completed
                    .OrderByDescending(x =>
                        x.ActualDurationMinutes)
                    .FirstOrDefault()
        };

        LabourCostCalculator.ApplyElapsedWorkdayCost(
            analytics, _timeProvider.GetLocalNow().DateTime, _workingDay, _costing);
        return analytics;
    }
}
