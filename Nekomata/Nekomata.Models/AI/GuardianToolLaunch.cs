namespace Nekomata.Models.AI;

public class GuardianToolLaunch
{
    public string ToolName { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public bool CanLaunch { get; set; }
}