using Nekomata.Models.Missions;

namespace Nekomata.Models.Guardian;

public class GuardianInsight
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public string Category { get; set; } = "General";

    public string Severity { get; set; } = "Info";

    public string SourceType { get; set; } = "Guardian";

    public DateTime DetectedAt { get; set; } =
        DateTime.Now;

    public bool CanCreateMission { get; set; }

    public MissionCandidate? SuggestedMission { get; set; }
}