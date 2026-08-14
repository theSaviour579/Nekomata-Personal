using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.AI;

public class GuardianToolRouter
    : IGuardianToolRouter
{
    public GuardianToolLaunch? Route(
        string request)
    {
        if (string.IsNullOrWhiteSpace(request))
            return null;

        request = request.ToLowerInvariant();

        // Meeting planning

        if (request.Contains("meeting") ||
            request.Contains("minutes") ||
            request.Contains("action points"))
        {
            return new GuardianToolLaunch
            {
                ToolName = "MeetingPlanner",
                DisplayName = "Meeting Planner",
                Description =
                    "Convert meeting notes into projects and tasks.",
                CanLaunch = true
            };
        }

        // Mission comparison

        if (request.Contains("priority") ||
            request.Contains("should i work") ||
            request.Contains("what next"))
        {
            return new GuardianToolLaunch
            {
                ToolName = "MissionAnalysis",
                DisplayName = "Mission Analysis",
                Description =
                    "Compare today's mission with alternatives.",
                CanLaunch = true
            };
        }

        return null;
    }
}