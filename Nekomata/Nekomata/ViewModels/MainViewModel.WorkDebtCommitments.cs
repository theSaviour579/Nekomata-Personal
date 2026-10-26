using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Analytics;
using Nekomata.Core.Commitments;
using Nekomata.Data.Repositories;
using Nekomata.Models.Analytics;
using Nekomata.Models.Tasks;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private IReadOnlyList<Commitment> activeCommitments = [];
    [ObservableProperty] private string commitmentSummary = "Commitments have not been reconciled yet.";

    public int YouOweCount => ActiveCommitments.Count(x => IsRealPromise(x) && !IsTaughtNotAPromise(x.Summary));
    public int WaitingOnOthersCount => ActiveCommitments.Count(IsRealExpectation);

    // Something someone told the user they'd do: firm wording, in their email or Teams message, in the last month.
    // Ticket text, invitations and anything older than that are left out of the count.
    private static bool IsRealExpectation(Commitment c) =>
        c.Direction == CommitmentDirection.OwedToMe &&
        (c.Confidence >= 70 || c.IsUserConfirmed) &&
        !c.SourceType.Contains("Calendar", StringComparison.OrdinalIgnoreCase) &&
        !c.SourceType.Contains("Call", StringComparison.OrdinalIgnoreCase) &&
        (c.DueAt ?? c.CreatedAt) > DateTimeOffset.Now.AddDays(-30);
    public int DueSoonCommitmentCount => ActiveCommitments.Count(x => x.Status == CommitmentStatus.DueSoon);
    public int OverdueCommitmentCount => ActiveCommitments.Count(x => x.Status == CommitmentStatus.Overdue);

    private async Task RefreshWorkDebtAsync()
    {
        var now=DateTime.Now;
        var summary=await _services.GetRequiredService<ExecutiveValueEngine>().BuildAsync(now.AddDays(-60),now);
        var analysis=_services.GetRequiredService<WorkDebtEngine>().Analyse(summary.Episodes,
            _services.GetRequiredService<LabourCostSettings>().HourlyLabourRate,now);
        var repository = _services.GetRequiredService<IWorkDebtRepository>();
        await repository.UpsertAsync(analysis.Items);
        var persisted = (await repository.GetCurrentAsync()).ToDictionary(x => x.ClusterKey, StringComparer.OrdinalIgnoreCase);
        WorkDebt = new(analysis.Items.Select(item =>
            persisted.TryGetValue(item.ClusterKey, out var saved) && saved.Status == WorkDebtStatus.ImprovementPlanned
                ? item with { Status = WorkDebtStatus.ImprovementPlanned }
                : item).ToList());
    }

    [RelayCommand]
    private async Task ReviewWorkDebtAsync(WorkDebtItem? item)
    {
        if (item is null) return;
        var proposal = WorkDebtImprovementProposalBuilder.Build(item,
            _services.GetRequiredService<LabourCostSettings>().HourlyLabourRate);
        if (proposal is null)
        {
            var explanation = $"{item.Title}\n\n{item.EvidenceSummary}\n\n{item.Recommendation}\n\n" +
                              "Guardian will continue observing this pattern. No task or calendar change has been made.";
            ShowPaletteResponse(explanation);
            return;
        }

        var approval = MessageBox.Show(
            $"{proposal.Description}\n\nCreate this improvement task? It will not be scheduled or delegated automatically.",
            "Review work-debt improvement", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (approval != MessageBoxResult.Yes) return;

        var task = new NekomataTask
        {
            Title = proposal.Title,
            Description = proposal.Description,
            Source = "Guardian Work Debt",
            Status = "Open",
            Priority = item.StrategicRelevance.Equals("High", StringComparison.OrdinalIgnoreCase) ? "High" : "Normal",
            Owner = UserName,
            EstimatedMinutes = proposal.EstimatedMinutes,
            OriginalEstimatedMinutes = proposal.EstimatedMinutes,
            EstimateConfidencePercent = proposal.Confidence,
            EstimateLearningExplanation = "Estimated from the observed recurring-work episodes.",
            CanDelegate = true,
            RequiresFocus = true,
            Interruptible = true,
            Recurring = false,
            AutomationPotential = 5,
            Category = "Process improvement",
            Tags = $"Guardian,WorkDebt,{proposal.ClusterKey}"
        };
        var taskId = await _services.GetRequiredService<ITaskRepository>().SaveAsync(task);
        await _services.GetRequiredService<IWorkDebtRepository>()
            .SetStatusAsync(item.Id, WorkDebtStatus.ImprovementPlanned);
        WorkDebt = new(WorkDebt.Items.Select(x => x.Id == item.Id
            ? x with { Status = WorkDebtStatus.ImprovementPlanned }
            : x).ToList());
        await _workspaceCoordinator.RefreshAsync();
        ShowPaletteResponse($"Improvement task #{taskId} created: {proposal.Title}. Nothing has been scheduled or delegated.");
    }

    private async Task RefreshCommitmentSummaryAsync()
    {
        try
        {
            ActiveCommitments=await _services.GetRequiredService<ICommitmentRepository>().GetActiveAsync();
            CommitmentSummary=$"You owe {YouOweCount} · Waiting on others {WaitingOnOthersCount} · Due soon {DueSoonCommitmentCount} · Overdue {OverdueCommitmentCount}";
            OnPropertyChanged(nameof(YouOweCount)); OnPropertyChanged(nameof(WaitingOnOthersCount));
            OnPropertyChanged(nameof(DueSoonCommitmentCount)); OnPropertyChanged(nameof(OverdueCommitmentCount));
            foreach(var value in ActiveCommitments.Where(CommitmentEngine.RequiresAttention))
                RaiseAttention($"commitment:{value.Id}","Commitment",value.Status==CommitmentStatus.Overdue?"Warning":"Info",
                    $"Commitment {value.Status}: {value.Summary}",
                    $"You appear to owe {value.Counterparty}. {value.EvidenceSummary}","open_email",value.SourceReference,
                    expiresAt:value.DueAt);
        }
        catch(Exception ex){CommitmentSummary="Commitment reconciliation unavailable: "+ex.Message;}
    }

    private async Task ShowWorkDebtCommandAsync()
    {
        await RefreshWorkDebtAsync();
        var items=WorkDebt.Items.Take(8).ToList();
        var response=items.Count==0?"No repeated operational pattern currently has enough evidence to qualify as Work Debt.":
            $"Work Debt · {WorkDebt.TotalMinutes} observed minutes · {WorkDebt.OvertimeMinutes} outside working hours\n"+
            string.Join("\n",items.Select(x=>$"• {x.Status} · {x.Title} — {x.TotalTimeMinutes}m across {x.EpisodeCount} episodes · {x.Confidence}% confidence\n  {x.Recommendation}"));
        ShowPaletteResponse(response); await SpeakGuardianAsync(response,true);
    }

    private async Task ShowCommitmentCommandAsync(Nekomata.Core.Guardian.Commands.GuardianCommandIntent intent)
    {
        await RefreshCommitmentSummaryAsync();
        var now=DateTimeOffset.Now;
        IEnumerable<Commitment> selected=intent switch
        {
            Nekomata.Core.Guardian.Commands.GuardianCommandIntent.ShowIOwe=>ActiveCommitments.Where(x=>x.Direction==CommitmentDirection.OwedByMe),
            Nekomata.Core.Guardian.Commands.GuardianCommandIntent.ShowOwedToMe=>ActiveCommitments.Where(x=>x.Direction==CommitmentDirection.OwedToMe),
            Nekomata.Core.Guardian.Commands.GuardianCommandIntent.ShowAtRiskCommitments=>ActiveCommitments.Where(x=>x.Status is CommitmentStatus.DueSoon or CommitmentStatus.Overdue),
            Nekomata.Core.Guardian.Commands.GuardianCommandIntent.ShowWaitingCommitments=>ActiveCommitments.Where(x=>x.Status==CommitmentStatus.WaitingExternal),
            Nekomata.Core.Guardian.Commands.GuardianCommandIntent.ShowPromisedThisWeek=>ActiveCommitments.Where(x=>x.Direction==CommitmentDirection.OwedByMe&&x.CreatedAt>=now.AddDays(-7)),
            Nekomata.Core.Guardian.Commands.GuardianCommandIntent.ShowCallCommitments=>ActiveCommitments.Where(x=>x.SourceType.Contains("call",StringComparison.OrdinalIgnoreCase)||x.SourceType.Contains("goto",StringComparison.OrdinalIgnoreCase)),
            _=>ActiveCommitments
        };
        var values=selected.Take(20).ToList();
        var response=values.Count==0?"No matching active commitments were found.":string.Join("\n",values.Select(x=>
            $"• {x.Status} · {x.Summary} · {(x.DueAt is null?"no explicit due date":x.DueAt.Value.LocalDateTime.ToString("ddd dd MMM HH:mm"))} · {x.Confidence}% confidence\n  Evidence: {x.EvidenceSummary}"));
        ShowPaletteResponse(response);await SpeakGuardianAsync(response,true);
    }
}
