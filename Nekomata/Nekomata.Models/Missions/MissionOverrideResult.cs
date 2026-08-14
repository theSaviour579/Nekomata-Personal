namespace Nekomata.Models.Missions;

public class MissionOverrideResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public Mission? PreviousMission { get; set; }

    public Mission? CurrentMission { get; set; }
}