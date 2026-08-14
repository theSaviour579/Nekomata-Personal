using Nekomata.Integrations.MicrosoftGraph.Calendar;

namespace Nekomata.UI.Services;

public sealed class CalendarUndoService
{
    private readonly ICalendarService _calendar;
    private readonly List<Entry> _pending = [];
    private List<Entry> _lastBatch = [];

    public CalendarUndoService(ICalendarService calendar) => _calendar = calendar;

    public bool CanUndo => _lastBatch.Count > 0;
    public DateTime? LastPlanDate => _lastBatch.FirstOrDefault()?.Start.Date;

    public void BeginBatch() => _pending.Clear();

    public void RecordCreated(string eventId, string title, DateTimeOffset start)
    {
        if (!string.IsNullOrWhiteSpace(eventId))
            _pending.Add(new Entry(eventId, title, start));
    }

    public void CommitBatch()
    {
        if (_pending.Count > 0)
            _lastBatch = _pending.ToList();
        _pending.Clear();
    }

    public async Task<string> UndoLastAsync()
    {
        if (_lastBatch.Count == 0)
            return "There is no applied calendar plan to undo.";

        var removed = 0;
        var failures = new List<string>();
        foreach (var entry in _lastBatch.AsEnumerable().Reverse())
        {
            try
            {
                await _calendar.DeleteFocusEventAsync(entry.EventId);
                removed++;
            }
            catch (Exception ex)
            {
                failures.Add($"{entry.Title}: {ex.Message}");
            }
        }

        if (failures.Count == 0)
            _lastBatch.Clear();
        else
            _lastBatch = _lastBatch.Where(entry => failures.Any(failure => failure.StartsWith(entry.Title, StringComparison.Ordinal))).ToList();

        var summary = $"Removed {removed} focus block{(removed == 1 ? string.Empty : "s")} from the last Guardian plan.";
        return failures.Count == 0 ? summary : summary + " Could not remove: " + string.Join("; ", failures);
    }

    private sealed record Entry(string EventId, string Title, DateTimeOffset Start);
}
