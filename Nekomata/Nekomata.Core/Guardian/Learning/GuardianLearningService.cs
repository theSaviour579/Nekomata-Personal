using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Learning;

public class GuardianLearningService
    : IGuardianLearningService
{
    public GuardianOverrideRecord RecordOverride(
        Mission originalMission,
        Mission selectedMission,
        NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(originalMission);
        ArgumentNullException.ThrowIfNull(selectedMission);
        ArgumentNullException.ThrowIfNull(workspace);

        var record =
            new GuardianOverrideRecord
            {
                OriginalMissionTitle =
                    originalMission.Title,

                OriginalMissionScore =
                    originalMission.Score,

                OriginalBusinessValue =
                    originalMission.BusinessValue,

                OriginalEstimatedMinutes =
                    (int)originalMission.EstimatedDuration.TotalMinutes,

                SelectedMissionTitle =
                    selectedMission.Title,

                SelectedMissionScore =
                    selectedMission.Score,

                SelectedBusinessValue =
                    selectedMission.BusinessValue,

                SelectedEstimatedMinutes =
                    (int)selectedMission.EstimatedDuration.TotalMinutes,

                InferredPreference =
                    InferPreference(
                        originalMission,
                        selectedMission),

                Notes =
                    "Manual mission override."
            };

        return record;
    }

    private static string InferPreference(
        Mission originalMission,
        Mission selectedMission)
    {
        if (selectedMission.BusinessValue >
            originalMission.BusinessValue)
        {
            return "Preferred higher business value.";
        }

        if (selectedMission.Score >
            originalMission.Score)
        {
            return "Preferred higher priority score.";
        }

        if (selectedMission.EstimatedDuration <
            originalMission.EstimatedDuration)
        {
            return "Preferred shorter mission.";
        }

        return "No clear preference detected.";
    }
}