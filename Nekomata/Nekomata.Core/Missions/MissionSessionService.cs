using Nekomata.Data.Repositories;
using Nekomata.Models.Missions;
using System.Text.Json;

namespace Nekomata.Core.Missions;

public class MissionSessionService : IMissionSessionService
{
    private readonly IMissionSessionRepository _repository;

    public MissionSessionService(
        IMissionSessionRepository repository)
    {
        _repository = repository;
    }

    public async Task RecordCompletedMissionAsync(
        Mission mission,
        DateTime startedAt,
        TimeSpan elapsed,
        IReadOnlyList<MissionWorkSegment>? activeSegments = null)
    {
        await _repository.SaveAsync(new MissionSession
        {
            TaskId = mission.TaskId,
            ProjectId = mission.ProjectId,

            Title = mission.Title,
            SourceType = mission.SourceType,

            Score = mission.Score,
            BusinessValue = mission.BusinessValue,

            EstimatedDurationMinutes =
    (int)mission.EstimatedDuration.TotalMinutes,

            ActualDurationMinutes =
    (int)elapsed.TotalMinutes,

            StartedAt = startedAt,
            FinishedAt = DateTime.Now,

            Completed = true,
            Cancelled = false,

            GuardianDecision = mission.Decision.Summary,
            RecommendationReason = mission.RecommendationReason,
            ActiveIntervalsJson = SerializeSegments(activeSegments)
        });
    }

    public async Task RecordDeferredMissionAsync(
        Mission mission,
        DateTime startedAt,
        TimeSpan elapsed,
        IReadOnlyList<MissionWorkSegment>? activeSegments = null)
    {
        await _repository.SaveAsync(new MissionSession
        {
            TaskId = mission.TaskId,
            ProjectId = mission.ProjectId,
            Title = mission.Title,
            SourceType = mission.SourceType,
            Score = mission.Score,
            BusinessValue = mission.BusinessValue,
            EstimatedDurationMinutes =
                (int)Math.Ceiling(mission.EstimatedDuration.TotalMinutes),
            ActualDurationMinutes =
                Math.Max(0, (int)Math.Ceiling(elapsed.TotalMinutes)),
            StartedAt = startedAt,
            FinishedAt = DateTime.Now,
            Completed = false,
            Cancelled = false,
            GuardianDecision = mission.Decision.Summary,
            RecommendationReason = mission.RecommendationReason,
            ActiveIntervalsJson = SerializeSegments(activeSegments)
        });
    }
    public async Task RecordCancelledMissionAsync(
        Mission mission,
        DateTime startedAt,
        TimeSpan elapsed,
        IReadOnlyList<MissionWorkSegment>? activeSegments = null)
    {
        await _repository.SaveAsync(new MissionSession
        {
            TaskId = mission.TaskId,
            ProjectId = mission.ProjectId,

            Title = mission.Title,
            SourceType = mission.SourceType,

            Score = mission.Score,
            BusinessValue = mission.BusinessValue,

            EstimatedDurationMinutes =
    (int)mission.EstimatedDuration.TotalMinutes,

            ActualDurationMinutes =
    (int)elapsed.TotalMinutes,

            StartedAt = startedAt,
            FinishedAt = DateTime.Now,

            Completed = false,
            Cancelled = true,

            GuardianDecision = mission.Decision.Summary,
            RecommendationReason = mission.RecommendationReason,
            ActiveIntervalsJson = SerializeSegments(activeSegments)
        });
    }

    private static string SerializeSegments(IReadOnlyList<MissionWorkSegment>? segments) =>
        segments is { Count: > 0 } ? JsonSerializer.Serialize(segments) : "[]";
}
