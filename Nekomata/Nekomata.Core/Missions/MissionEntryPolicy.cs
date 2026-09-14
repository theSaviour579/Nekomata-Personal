namespace Nekomata.Core.Missions;

public enum MissionEntryAction
{
    Start,
    ReturnToActiveMission,
    ResumePausedMission
}

public static class MissionEntryPolicy
{
    public static MissionEntryAction Resolve(bool missionActive, bool missionPaused) =>
        !missionActive
            ? MissionEntryAction.Start
            : missionPaused
                ? MissionEntryAction.ResumePausedMission
                : MissionEntryAction.ReturnToActiveMission;
}
