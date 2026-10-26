namespace Nekomata.Core.GuardianRecords;

// Guardian's own records, kept by the desktop as JSON files under %LocalAppData%\Nekomata Personal.
// Plain classes; default System.Text.Json naming.

public sealed class ObjectiveEvidence
{
    public DateTime At { get; set; }
    public string Text { get; set; } = "";
    public string Source { get; set; } = "";
}

public sealed class Objective
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    /// <summary>How success is measured ("two courses finished by June").</summary>
    public string Measure { get; set; } = "";
    public DateTime? Due { get; set; }
    /// <summary>The user's own estimate, 0-100.</summary>
    public int Progress { get; set; }
    public bool Active { get; set; } = true;
    public List<ObjectiveEvidence> Evidence { get; set; } = [];
    public DateTime Created { get; set; } = DateTime.Today;
}

public sealed class PlaceSettings
{
    public List<string> Office { get; set; } = [];
    public List<string> Home { get; set; } = [];
    public List<string> OfficeReminders { get; set; } = [];
}
