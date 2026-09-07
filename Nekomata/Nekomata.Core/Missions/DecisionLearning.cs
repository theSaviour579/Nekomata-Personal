namespace Nekomata.Core.Missions;

public sealed class DecisionFeedback
{
    public string ObjectiveKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime Day { get; set; }
}
public sealed class LearnedPreference
{
    public string ObjectiveKey { get; set; } = "";
    public DateTimeOffset Until { get; set; }
}
public static class DecisionLearning
{
    public static bool CanPropose(IEnumerable<DecisionFeedback> feedback, string key, DateTime today) => feedback
        .Where(x => x.ObjectiveKey == key && x.Reason == "Wrong priority" && x.Day.Date <= today.Date && x.Day.Date >= today.Date.AddDays(-30))
        .Select(x => x.Day.Date).Distinct().Count() >= 3;
    public static int Penalty(IEnumerable<LearnedPreference> preferences, string key, bool urgent, DateTimeOffset now) =>
        !urgent && preferences.Any(x => x.ObjectiveKey == key && x.Until > now) ? 25 : 0;
}
