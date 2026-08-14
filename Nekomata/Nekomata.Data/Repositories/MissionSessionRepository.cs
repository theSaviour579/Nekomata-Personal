using Dapper;
using Nekomata.Data.Database;
using Nekomata.Models.Missions;

namespace Nekomata.Data.Repositories;

public class MissionSessionRepository : IMissionSessionRepository
{
    private readonly NekomataDbContext _database;

    public MissionSessionRepository(
        NekomataDbContext database)
    {
        _database = database;
    }

    public async Task SaveAsync(MissionSession session)
    {
        await using var connection = _database.Create();

        const string sql = """
        INSERT INTO assistant.mission_sessions
        (
            task_id,
            project_id,
            title,
            source_type,
            score,
            business_value,
            estimated_duration_minutes,
            actual_duration_minutes,
            started_at,
            finished_at,
            completed,
            cancelled,
            guardian_decision,
            recommendation_reason
        )
        VALUES
        (
            @TaskId,
            @ProjectId,
            @Title,
            @SourceType,
            @Score,
            @BusinessValue,
            @EstimatedDurationMinutes,
            @ActualDurationMinutes,
            @StartedAt,
            @FinishedAt,
            @Completed,
            @Cancelled,
            @GuardianDecision,
            @RecommendationReason
        );
        """;

        await connection.ExecuteAsync(sql, new
        {
            session.TaskId,
            session.ProjectId,
            session.Title,
            session.SourceType,
            session.Score,
            session.BusinessValue,

            session.EstimatedDurationMinutes,
            session.ActualDurationMinutes,

            session.StartedAt,
            session.FinishedAt,

            session.Completed,
            session.Cancelled,

            session.GuardianDecision,
            session.RecommendationReason
        });
    }

    public async Task<List<MissionSession>> GetRecentAsync(int count)
    {
        await using var connection = _database.Create();

        const string sql = """
        SELECT
            id,
            task_id AS TaskId,
            project_id AS ProjectId,
            title,
            source_type AS SourceType,
            score,
            business_value AS BusinessValue,
            estimated_duration_minutes AS EstimatedDurationMinutes,
            actual_duration_minutes AS ActualDurationMinutes,
            started_at AS StartedAt,
            finished_at AS FinishedAt,
            completed,
            cancelled,
            guardian_decision AS GuardianDecision,
            recommendation_reason AS RecommendationReason
        FROM assistant.mission_sessions
        ORDER BY finished_at DESC
        LIMIT @Count;
        """;

        var sessions = await connection.QueryAsync<MissionSession>(
            sql,
            new { Count = count });

        return sessions.ToList();
    }

    public async Task<List<MissionSession>> GetTodayAsync()
    {
        await using var connection = _database.Create();

        const string sql = """
        SELECT
            id,
            task_id AS TaskId,
            project_id AS ProjectId,
            title,
            source_type AS SourceType,
            score,
            business_value AS BusinessValue,
            estimated_duration_minutes AS EstimatedDurationMinutes,
            actual_duration_minutes AS ActualDurationMinutes,
            started_at AS StartedAt,
            finished_at AS FinishedAt,
            completed,
            cancelled,
            guardian_decision AS GuardianDecision,
            recommendation_reason AS RecommendationReason
        FROM assistant.mission_sessions
        WHERE DATE(finished_at) = CURRENT_DATE
        ORDER BY finished_at DESC;
        """;

        var sessions = await connection.QueryAsync<MissionSession>(sql);

        return sessions.ToList();
    }

    public async Task<List<MissionSession>> GetAllAsync()
    {
        await using var connection = _database.Create();

        const string sql = """
    SELECT
        id,
        task_id AS TaskId,
        project_id AS ProjectId,
        title,
        source_type AS SourceType,
        score,
        business_value AS BusinessValue,
        estimated_duration_minutes AS EstimatedDurationMinutes,
        actual_duration_minutes AS ActualDurationMinutes,
        started_at AS StartedAt,
        finished_at AS FinishedAt,
        completed,
        cancelled,
        guardian_decision AS GuardianDecision,
        recommendation_reason AS RecommendationReason
    FROM assistant.mission_sessions
    ORDER BY finished_at DESC;
    """;

        var sessions =
            await connection.QueryAsync<MissionSession>(sql);

        return sessions.ToList();

    }

    public async Task<List<MissionSession>> GetBetweenAsync(
    DateTime from,
    DateTime to)
    {
        await using var connection = _database.Create();

        const string sql = """
        SELECT
            id,
            task_id AS TaskId,
            project_id AS ProjectId,
            title,
            source_type AS SourceType,
            score,
            business_value AS BusinessValue,
            estimated_duration_minutes AS EstimatedDurationMinutes,
            actual_duration_minutes AS ActualDurationMinutes,
            started_at AS StartedAt,
            finished_at AS FinishedAt,
            completed,
            cancelled,
            guardian_decision AS GuardianDecision,
            recommendation_reason AS RecommendationReason
        FROM assistant.mission_sessions
        WHERE finished_at >= @From
          AND finished_at < @To
        ORDER BY finished_at DESC;
        """;

        var sessions =
            await connection.QueryAsync<MissionSession>(
                sql,
                new
                {
                    From = from,
                    To = to
                });

        return sessions.ToList();
    }
}