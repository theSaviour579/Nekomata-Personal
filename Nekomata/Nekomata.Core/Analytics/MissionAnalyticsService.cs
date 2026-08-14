using Nekomata.Data.Repositories;
using Nekomata.Models.Analytics;
using Nekomata.Models.Missions;

namespace Nekomata.Core.Analytics;

public class MissionAnalyticsService
    : IMissionAnalyticsService
{
    private readonly IMissionSessionRepository _repository;

    public MissionAnalyticsService(
        IMissionSessionRepository repository)
    {
        _repository = repository;
    }

    public async Task<MissionAnalytics> GetTodayAsync()
    {
        var sessions =
            await _repository.GetTodayAsync();

        var completed =
            sessions
                .Where(x => x.Completed)
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

        return new MissionAnalytics
        {
            MissionsCompletedToday =
                completed.Count,

            MissionsCancelledToday =
                sessions.Count(x => x.Cancelled),

            FocusTimeToday =
                TimeSpan.FromMinutes(
                    completed.Sum(x =>
                        x.ActualDurationMinutes)),

            BusinessValueDeliveredToday =
                completed.Sum(x =>
                    x.BusinessValue),

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
    }
}