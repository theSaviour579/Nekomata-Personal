using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Missions;

public static class ActiveMissionFocusPolicy
{
    public static Mission Resolve(
        bool sessionActive,
        Mission? pinnedMission,
        Mission refreshedMission) =>
        sessionActive && pinnedMission is not null
            ? pinnedMission
            : refreshedMission;

    public static WorkspaceMode ResolveModeAfterCalendarAction(
        bool sessionActive,
        WorkspaceMode inactiveSessionMode = WorkspaceMode.Calendar) =>
        sessionActive
            ? WorkspaceMode.Mission
            : inactiveSessionMode;
}
