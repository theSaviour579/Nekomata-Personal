using Dapper;
using Nekomata.Data.Seed;
using Npgsql;

namespace Nekomata.Data.Database;

public sealed class DatabaseInitializer
{
    private readonly NekomataDbContext _db;

    public DatabaseInitializer(NekomataDbContext db)
    {
        _db = db;
    }

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _db.Create();
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(BootstrapSql, cancellationToken: cancellationToken));

        foreach (var migration in DatabaseMigrations.All)
        {
            var applied = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM assistant.schema_migrations WHERE version = @Version);",
                    new { migration.Version },
                    cancellationToken: cancellationToken));

            if (applied)
                continue;

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    migration.Sql,
                    transaction: transaction,
                    cancellationToken: cancellationToken));
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO assistant.schema_migrations (version, description) VALUES (@Version, @Description);",
                    new { migration.Version, migration.Description },
                    transaction,
                    cancellationToken: cancellationToken));
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        await SeedDataAsync(connection, cancellationToken);
    }

    internal const string BootstrapSql = """
        CREATE SCHEMA IF NOT EXISTS assistant;
        CREATE TABLE IF NOT EXISTS assistant.schema_migrations
        (
            version       integer PRIMARY KEY,
            description   text NOT NULL,
            applied_at    timestamp with time zone NOT NULL DEFAULT now()
        );
        """;

    private static async Task SeedDataAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var existing = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM assistant.tasks);",
            cancellationToken: cancellationToken));

        if (existing)
            return;

        const string sql = """
            INSERT INTO assistant.tasks
            (
                title, description, source, status, priority, owner,
                estimated_minutes, estimated_business_value, revenue_impact,
                customer_impact, executive_visibility, automation_potential,
                requires_sql, requires_halo, requires_outlook, requires_focus,
                interruptible, recurring, category, tags
            )
            VALUES
            (
                @Title, @Description, @Source, @Status, @Priority, @Owner,
                @EstimatedMinutes, @EstimatedBusinessValue, @RevenueImpact,
                @CustomerImpact, @ExecutiveVisibility, @AutomationPotential,
                @RequiresSql, @RequiresHalo, @RequiresOutlook, @RequiresFocus,
                @Interruptible, @Recurring, @Category, @Tags
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            TaskSeed.GetTasks(),
            cancellationToken: cancellationToken));
    }
}
