using Nekomata.Models.Missions;

namespace Nekomata.Core.Missions;

public interface IMissionSessionService
{
    Task RecordCompletedMissionAsync(
        Mission mission,
        DateTime startedAt,
        TimeSpan elapsed);

    Task RecordDeferredMissionAsync(
        Mission mission,
        DateTime startedAt,
        TimeSpan elapsed);

    Task RecordCancelledMissionAsync(
        Mission mission,
        DateTime startedAt,
        TimeSpan elapsed);
}