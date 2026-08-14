using Dapper;
using Nekomata.Data.Database;
using Nekomata.Models.Projects;

namespace Nekomata.Data.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly NekomataDbContext _database;

    public ProjectRepository(NekomataDbContext database)
    {
        _database = database;
    }

    public async Task<List<NekomataProject>> GetAllAsync()
    {
        await using var connection = _database.Create();

        const string sql = """
            SELECT
                id AS Id,
                name AS Name,
                description AS Description,
                status AS Status,
                priority AS Priority,
                progress_percent AS ProgressPercent,
                estimated_remaining_minutes AS EstimatedRemainingMinutes,
                due_at AS DueAt,
                at_risk AS AtRisk,
                next_action AS NextAction,
                estimated_business_value AS EstimatedBusinessValue,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM assistant.projects
            ORDER BY
                at_risk DESC,
                estimated_business_value DESC,
                due_at NULLS LAST,
                name;
            """;

        var projects = await connection.QueryAsync<NekomataProject>(sql);

        return projects.ToList();
    }

    public async Task<NekomataProject?> GetByIdAsync(long id)
    {
        await using var connection = _database.Create();

        const string sql = """
            SELECT
                id AS Id,
                name AS Name,
                description AS Description,
                status AS Status,
                priority AS Priority,
                progress_percent AS ProgressPercent,
                estimated_remaining_minutes AS EstimatedRemainingMinutes,
                due_at AS DueAt,
                at_risk AS AtRisk,
                next_action AS NextAction,
                estimated_business_value AS EstimatedBusinessValue,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM assistant.projects
            WHERE id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<NekomataProject>(
            sql,
            new { Id = id });
    }

    public async Task<long> SaveAsync(NekomataProject project)
    {
        await using var connection = _database.Create();

        if (project.Id == 0)
        {
            const string insert = """
                INSERT INTO assistant.projects
                (
                    name,
                    description,
                    status,
                    priority,
                    progress_percent,
                    estimated_remaining_minutes,
                    due_at,
                    at_risk,
                    next_action,
                    estimated_business_value
                )
                VALUES
                (
                    @Name,
                    @Description,
                    @Status,
                    @Priority,
                    @ProgressPercent,
                    @EstimatedRemainingMinutes,
                    @DueAt,
                    @AtRisk,
                    @NextAction,
                    @EstimatedBusinessValue
                )
                RETURNING id;
                """;

            return await connection.ExecuteScalarAsync<long>(insert, project);
        }

        const string update = """
            UPDATE assistant.projects
            SET
                name = @Name,
                description = @Description,
                status = @Status,
                priority = @Priority,
                progress_percent = @ProgressPercent,
                estimated_remaining_minutes = @EstimatedRemainingMinutes,
                due_at = @DueAt,
                at_risk = @AtRisk,
                next_action = @NextAction,
                estimated_business_value = @EstimatedBusinessValue,
                updated_at = now()
            WHERE id = @Id;
            """;

        await connection.ExecuteAsync(update, project);

        return project.Id;
    }

    public async Task DeleteAsync(long id)
    {
        await using var connection = _database.Create();

        const string sql = """
            DELETE
            FROM assistant.projects
            WHERE id = @Id;
            """;

        await connection.ExecuteAsync(sql, new { Id = id });
    }
}