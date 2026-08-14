using Dapper;
using Nekomata.Data.Database;
using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public sealed class GuardianAuditRepository : IGuardianAuditRepository
{
    private readonly NekomataDbContext _database;

    public GuardianAuditRepository(NekomataDbContext database) => _database = database;

    public async Task AddBatchAsync(IEnumerable<GuardianAuditEntry> entries)
    {
        await using var connection = _database.Create();
        const string sql = """
            INSERT INTO assistant.guardian_audit
                (batch_id, operation, entity_type, entity_id, external_id, title, description,
                 reason, confidence, before_state, after_state, reversible, irreversible_reason,
                 status, applied_at)
            VALUES
                (@BatchId, @Operation, @EntityType, @EntityId, @ExternalId, @Title, @Description,
                 @Reason, @Confidence, CAST(@BeforeState AS jsonb), CAST(@AfterState AS jsonb),
                 @Reversible, @IrreversibleReason, @Status, @AppliedAt);
            """;
        await connection.ExecuteAsync(sql, entries);
    }

    public async Task<List<GuardianAuditEntry>> GetRecentAsync(int count = 100)
    {
        await using var connection = _database.Create();
        const string sql = """
            SELECT id AS Id, batch_id AS BatchId, operation AS Operation, entity_type AS EntityType,
                   entity_id AS EntityId, external_id AS ExternalId, title AS Title,
                   description AS Description, reason AS Reason, confidence AS Confidence,
                   before_state::text AS BeforeState, after_state::text AS AfterState,
                   reversible AS Reversible, irreversible_reason AS IrreversibleReason,
                   status AS Status, applied_at AS AppliedAt, undone_at AS UndoneAt,
                   undo_message AS UndoMessage
            FROM assistant.guardian_audit
            ORDER BY applied_at DESC, id DESC LIMIT @Count;
            """;
        return (await connection.QueryAsync<GuardianAuditEntry>(sql, new { Count = Math.Clamp(count, 1, 500) })).ToList();
    }

    public async Task<List<GuardianAuditEntry>> GetBatchAsync(Guid batchId)
    {
        await using var connection = _database.Create();
        const string sql = """
            SELECT id AS Id, batch_id AS BatchId, operation AS Operation, entity_type AS EntityType,
                   entity_id AS EntityId, external_id AS ExternalId, title AS Title,
                   description AS Description, reason AS Reason, confidence AS Confidence,
                   before_state::text AS BeforeState, after_state::text AS AfterState,
                   reversible AS Reversible, irreversible_reason AS IrreversibleReason,
                   status AS Status, applied_at AS AppliedAt, undone_at AS UndoneAt,
                   undo_message AS UndoMessage
            FROM assistant.guardian_audit WHERE batch_id = @BatchId ORDER BY id DESC;
            """;
        return (await connection.QueryAsync<GuardianAuditEntry>(sql, new { BatchId = batchId })).ToList();
    }

    public Task MarkBatchUndoneAsync(Guid batchId, string message) => SetBatchStatusAsync(batchId, "Undone", message, true);
    public Task MarkBatchConflictAsync(Guid batchId, string message) => SetBatchStatusAsync(batchId, "Conflict", message, false);

    private async Task SetBatchStatusAsync(Guid batchId, string status, string message, bool undone)
    {
        await using var connection = _database.Create();
        const string sql = """
            UPDATE assistant.guardian_audit
            SET status = @Status, undo_message = @Message,
                undone_at = CASE WHEN @Undone THEN now() ELSE undone_at END
            WHERE batch_id = @BatchId;
            """;
        await connection.ExecuteAsync(sql, new { BatchId = batchId, Status = status, Message = message, Undone = undone });
    }
}
