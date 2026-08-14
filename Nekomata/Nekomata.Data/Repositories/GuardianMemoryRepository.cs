using Dapper;
using Nekomata.Data.Database;
using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public class GuardianMemoryRepository : IGuardianMemoryRepository
{
    private readonly NekomataDbContext _database;

    public GuardianMemoryRepository(NekomataDbContext database)
    {
        _database = database;
    }

    public async Task<long> AddAsync(GuardianMemory memory)
    {
        await using var connection = _database.Create();

        const string sql = """
            INSERT INTO assistant.guardian_memory
            (
                category,
                importance,
                source,
                summary,
                detail,
                project_id,
                task_id,
                metadata
            )
            VALUES
            (
                @Category,
                @Importance,
                @Source,
                @Summary,
                @Detail,
                @ProjectId,
                @TaskId,
                CAST(@Metadata AS jsonb)
            )
            RETURNING id;
            """;

        return await connection.ExecuteScalarAsync<long>(sql, memory);
    }

    public async Task<List<GuardianMemory>> GetRecentAsync(
        int count = 25)
    {
        await using var connection = _database.Create();

        const string sql = """
            SELECT
                id AS Id,
                created_at AS CreatedAt,
                category AS Category,
                importance AS Importance,
                source AS Source,
                summary AS Summary,
                detail AS Detail,
                project_id AS ProjectId,
                task_id AS TaskId,
                metadata::text AS Metadata
            FROM assistant.guardian_memory
            ORDER BY
                importance DESC,
                created_at DESC
            LIMIT @Count;
            """;

        var memories = await connection.QueryAsync<GuardianMemory>(
            sql,
            new { Count = count });

        return memories.ToList();
    }

    public async Task<List<GuardianMemory>> GetProjectMemoriesAsync(
        long projectId,
        int count = 25)
    {
        await using var connection = _database.Create();

        const string sql = """
            SELECT
                id AS Id,
                created_at AS CreatedAt,
                category AS Category,
                importance AS Importance,
                source AS Source,
                summary AS Summary,
                detail AS Detail,
                project_id AS ProjectId,
                task_id AS TaskId,
                metadata::text AS Metadata
            FROM assistant.guardian_memory
            WHERE project_id = @ProjectId
            ORDER BY
                importance DESC,
                created_at DESC
            LIMIT @Count;
            """;

        var memories = await connection.QueryAsync<GuardianMemory>(
            sql,
            new
            {
                ProjectId = projectId,
                Count = count
            });

        return memories.ToList();
    }

    public async Task<List<GuardianMemory>> SearchAsync(
        string text,
        int count = 25)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        await using var connection = _database.Create();

        const string sql = """
            SELECT
                id AS Id,
                created_at AS CreatedAt,
                category AS Category,
                importance AS Importance,
                source AS Source,
                summary AS Summary,
                detail AS Detail,
                project_id AS ProjectId,
                task_id AS TaskId,
                metadata::text AS Metadata
            FROM assistant.guardian_memory
            WHERE
                summary ILIKE '%' || @Text || '%'
                OR detail ILIKE '%' || @Text || '%'
                OR category ILIKE '%' || @Text || '%'
                OR source ILIKE '%' || @Text || '%'
            ORDER BY
                importance DESC,
                created_at DESC
            LIMIT @Count;
            """;

        var memories = await connection.QueryAsync<GuardianMemory>(
            sql,
            new
            {
                Text = text.Trim(),
                Count = count
            });

        return memories.ToList();
    }
}