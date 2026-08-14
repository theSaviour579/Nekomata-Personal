using Nekomata.Models.Missions;

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
}