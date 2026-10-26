using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IGuardianCalendarEffortRepository
{
    Task UpsertAsync(GuardianCalendarEffort effort);
    Task<List<GuardianCalendarEffort>> GetForThreadAsync(Guid threadId);
}

public sealed class GuardianCalendarEffort
{
    public string CalendarEventId { get; set; } = "";
    public Guid ThreadId { get; set; }
    public long? TaskId { get; set; }
    public string Subject { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public int DurationMinutes { get; set; }
    public string State { get; set; } = "Planned";
    public Guid SourceEventId { get; set; }
}
