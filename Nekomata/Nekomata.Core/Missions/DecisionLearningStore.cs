namespace Nekomata.Core.Missions;

public sealed record DecisionLearningState(
    IReadOnlyList<DecisionFeedback> Feedback,
    IReadOnlyList<LearnedPreference> Preferences);

public interface IDecisionLearningStore
{
    Task<DecisionLearningState> LoadAsync(CancellationToken cancellationToken = default);
    Task RecordFeedbackAsync(DecisionFeedback value, string source, CancellationToken cancellationToken = default);
    Task SetPreferenceAsync(LearnedPreference value, string approvedBy, CancellationToken cancellationToken = default);
    Task ForgetAsync(string objectiveKey, CancellationToken cancellationToken = default);
}
