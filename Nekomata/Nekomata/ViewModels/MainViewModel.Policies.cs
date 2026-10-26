using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Core.Guardian.Learning;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private async Task BeginPolicyObservationAsync(Mission mission)
    {
        var counterfactual = false;
        if (mission.AppliedPolicyIds.Count == 0)
        {
            var workType = GuardianOutcomeEngine.Classify(mission.Title, mission.SourceType);
            var exceptionPolicies = Workspace.Policies.Where(policy => policy.IsActive &&
                ((policy.Action == "prefer_analysis_before_11" && workType == "Analysis" && DateTime.Now.Hour >= 11) ||
                 (policy.Action == "avoid_deep_afternoon" && DateTime.Now.Hour >= 13 && mission.PredictedActiveMinutes >= 90)))
                .Select(x => x.Id).ToList();
            if (exceptionPolicies.Count > 0)
            {
                mission.AppliedPolicyIds.AddRange(exceptionPolicies);
                counterfactual = true;
            }
        }
        if (mission.AppliedPolicyIds.Count > 0)
            await _services.GetRequiredService<GuardianPolicyAccountabilityService>().RecordAppliedAsync(mission, counterfactual);
    }

    private Task CompletePolicyObservationAsync(Mission mission, string outcome, int strategicMinutes) =>
        mission.AppliedPolicyIds.Count == 0 ? Task.CompletedTask :
            _services.GetRequiredService<GuardianPolicyAccountabilityService>().RecordOutcomeAsync(mission, outcome, strategicMinutes);

    [RelayCommand]
    private async Task SuspendPolicyAsync(GuardianPolicy? policy)
    {
        if (policy is null || !policy.IsActive) return;
        await _services.GetRequiredService<GuardianPolicyAccountabilityService>().SetLifecycleAsync(policy.Id, "Retired");
        policy.Lifecycle = "Retired";
        OnPropertyChanged(nameof(Workspace));
        GuardianResponse = $"{policy.Name} suspended. Its evidence remains available, but it will no longer influence planning.";
        GuardianPanelExpanded = true;
    }

    [RelayCommand]
    private async Task ActivateCandidatePolicyAsync(GuardianPolicy? policy)
    {
        if(policy is null||!policy.IsCandidate) return;
        var promoted=await _services.GetRequiredService<LearnedWorkflowPromotionService>()
            .PromoteAsync(policy,"User approved the learned workflow candidate after reviewing its independent-thread evidence.",DateTimeOffset.Now);
        if(!promoted) return;
        policy.Lifecycle="Active";
        OnPropertyChanged(nameof(Workspace));
        GuardianResponse=$"{policy.Name} activated. Future applications are governed and will be monitored against outcomes.";
        GuardianPanelExpanded=true;
    }

    [RelayCommand]
    private void ViewPolicyEvidence(GuardianPolicy? policy)
    {
        if (policy is null) return;
        var effectivenessHeading = policy.Lifecycle == "Candidate" ? "Activation status" : "Policy effectiveness";
        GuardianResponse = $"{policy.Name}\n{policy.Lifecycle.ToUpperInvariant()} · {policy.ConfidencePercent}% confidence · n={policy.EvidenceCount}\n\nObservational evidence\n{policy.Statement}\n\n{effectivenessHeading}\n{policy.AccountabilitySummary}\nCounterfactual exceptions retained: {policy.CounterfactualCount}\nEvidence trend: {policy.EvidenceTrend}";
        GuardianPanelExpanded = true;
    }
}
