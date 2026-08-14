using Nekomata.Models.Missions;

namespace Nekomata.Models.Analytics;

public class MissionAnalytics
{
    public int MissionsCompletedToday { get; set; }

    public int MissionsCancelledToday { get; set; }

    public TimeSpan FocusTimeToday { get; set; }

    public decimal BusinessValueDeliveredToday { get; set; }

    public double AverageScoreToday { get; set; }

    public double EstimateAccuracyPercent { get; set; }

    public TimeSpan AverageMissionDuration { get; set; }

    public MissionSession? HighestScoringMission { get; set; }

    public MissionSession? LongestMission { get; set; }
}