using Dapper;
using Nekomata.Data.Database;
using Nekomata.Models.Tasks;

namespace Nekomata.Data.Repositories;

public class TaskRepository : ITaskRepository
{
    private readonly NekomataDbContext _database;

    public TaskRepository(NekomataDbContext database)
    {
        _database = database;
    }

    public async Task<List<NekomataTask>> GetOpenTasksAsync()
    {
        await using var connection = _database.Create();

        const string sql = """
            SELECT
                id AS Id,
                project_id AS ProjectId,
                title AS Title,
                description AS Description,
                source AS Source,
                status AS Status,
                priority AS Priority,
                owner AS Owner,
                suggested_delegate AS SuggestedDelegate,
                business_critical AS BusinessCritical,
                accuracy_sensitive AS AccuracySensitive,
                estimated_minutes AS EstimatedMinutes,
                actual_minutes AS ActualMinutes,
                due_at AS DueAt,
                priority_score AS PriorityScore,
                estimated_business_value AS EstimatedBusinessValue,
                revenue_impact AS RevenueImpact,
                customer_impact AS CustomerImpact,
                executive_visibility AS ExecutiveVisibility,
                automation_potential AS AutomationPotential,
                requires_sql AS RequiresSql,
                requires_halo AS RequiresHalo,
                requires_outlook AS RequiresOutlook,
                requires_focus AS RequiresFocus,
                interruptible AS Interruptible,
                recurring AS Recurring,
                category AS Category,
                tags AS Tags
            FROM assistant.tasks
            WHERE LOWER(status) = 'open'
            ORDER BY
                priority_score DESC,
                due_at NULLS LAST,
                id;
            """;

        var tasks = await connection.QueryAsync<NekomataTask>(sql);

        return tasks.ToList();
    }

    public async Task<NekomataTask?> GetByIdAsync(long id)
    {
        await using var connection = _database.Create();

        const string sql = """
            SELECT
                id AS Id,
                project_id AS ProjectId,
                title AS Title,
                description AS Description,
                source AS Source,
                status AS Status,
                priority AS Priority,
                owner AS Owner,
                suggested_delegate AS SuggestedDelegate,
                business_critical AS BusinessCritical,
                accuracy_sensitive AS AccuracySensitive,
                estimated_minutes AS EstimatedMinutes,
                actual_minutes AS ActualMinutes,
                due_at AS DueAt,
                priority_score AS PriorityScore,
                estimated_business_value AS EstimatedBusinessValue,
                revenue_impact AS RevenueImpact,
                customer_impact AS CustomerImpact,
                executive_visibility AS ExecutiveVisibility,
                automation_potential AS AutomationPotential,
                requires_sql AS RequiresSql,
                requires_halo AS RequiresHalo,
                requires_outlook AS RequiresOutlook,
                requires_focus AS RequiresFocus,
                interruptible AS Interruptible,
                recurring AS Recurring,
                category AS Category,
                tags AS Tags,
            completed_at AS CompletedAt
            FROM assistant.tasks
            WHERE id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<NekomataTask>(
            sql,
            new { Id = id });
    }

    public async Task<long> SaveAsync(NekomataTask task)
    {
        await using var connection = _database.Create();

        if (task.Id == 0)
        {
            const string insert = """
                INSERT INTO assistant.tasks
                (
                    project_id,
                    title,
                    description,
                    source,
                    status,
                    priority,
                    owner,
                    suggested_delegate,
                    business_critical,
                    accuracy_sensitive,
                    estimated_minutes,
                    actual_minutes,
                    due_at,
                    priority_score,
                    estimated_business_value,
                    revenue_impact,
                    customer_impact,
                    executive_visibility,
                    automation_potential,
                    requires_sql,
                    requires_halo,
                    requires_outlook,
                    requires_focus,
                    interruptible,
                    recurring,
                    category,
                    tags
                )
                VALUES
                (
                    @ProjectId,
                    @Title,
                    @Description,
                    @Source,
                    @Status,
                    @Priority,
                    @Owner,
                    @SuggestedDelegate,
                    @BusinessCritical,
                    @AccuracySensitive,
                    @EstimatedMinutes,
                    @ActualMinutes,
                    @DueAt,
                    @PriorityScore,
                    @EstimatedBusinessValue,
                    @RevenueImpact,
                    @CustomerImpact,
                    @ExecutiveVisibility,
                    @AutomationPotential,
                    @RequiresSql,
                    @RequiresHalo,
                    @RequiresOutlook,
                    @RequiresFocus,
                    @Interruptible,
                    @Recurring,
                    @Category,
                    @Tags
                )
                RETURNING id;
                """;

            return await connection.ExecuteScalarAsync<long>(insert, task);
        }

        const string update = """
            UPDATE assistant.tasks
            SET
                project_id = @ProjectId,
                title = @Title,
                description = @Description,
                source = @Source,
                status = @Status,
                priority = @Priority,
                owner = @Owner,
                suggested_delegate = @SuggestedDelegate,
                business_critical = @BusinessCritical,
                accuracy_sensitive = @AccuracySensitive,
                estimated_minutes = @EstimatedMinutes,
                actual_minutes = @ActualMinutes,
                due_at = @DueAt,
                completed_at = @CompletedAt,
                priority_score = @PriorityScore,
                estimated_business_value = @EstimatedBusinessValue,
                revenue_impact = @RevenueImpact,
                customer_impact = @CustomerImpact,
                executive_visibility = @ExecutiveVisibility,
                automation_potential = @AutomationPotential,
                requires_sql = @RequiresSql,
                requires_halo = @RequiresHalo,
                requires_outlook = @RequiresOutlook,
                requires_focus = @RequiresFocus,
                interruptible = @Interruptible,
                recurring = @Recurring,
                category = @Category,
                tags = @Tags,
                updated_at = now()
            WHERE id = @Id;
            """;

        await connection.ExecuteAsync(update, task);

        return task.Id;
    }

    public async Task DeleteAsync(long id)
    {
        await using var connection = _database.Create();

        const string sql = """
            DELETE FROM assistant.tasks
            WHERE id = @Id;
            """;

        await connection.ExecuteAsync(sql, new { Id = id });
    }

    public async Task CompleteAsync(long taskId)
    {
        await using var connection = _database.Create();

        const string sql = """
        UPDATE assistant.tasks
        SET
            status = 'Completed',
            completed_at = now(),
            updated_at = now()
        WHERE id = @TaskId
          AND LOWER(status) <> 'completed';
        """;

        await connection.ExecuteAsync(
            sql,
            new { TaskId = taskId });
    }

    public async Task ReopenAsync(long taskId)
    {
        await using var connection = _database.Create();

        const string sql = """
        UPDATE assistant.tasks
        SET
            status = 'Open',
            completed_at = NULL,
            updated_at = now()
        WHERE id = @TaskId;
        """;

        await connection.ExecuteAsync(
            sql,
            new { TaskId = taskId });
    }

    public async Task<int> CompleteOpenTasksForProjectAsync(long projectId)
    {
        await using var connection = _database.Create();

        const string sql = """
        UPDATE assistant.tasks
        SET
            status = 'Completed',
            completed_at = now(),
            updated_at = now()
        WHERE project_id = @ProjectId
          AND LOWER(status) = 'open';
        """;

        return await connection.ExecuteAsync(
            sql,
            new { ProjectId = projectId });
    }
}
