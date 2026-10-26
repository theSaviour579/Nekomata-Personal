using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.Core.Workspace;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.UI.Services;
using Nekomata.Core.Guardian.Outcomes;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nekomata.UI.ViewModels;

public partial class GuardianActivityViewModel : ObservableObject
{
    private readonly IGuardianAuditRepository _audit;
    private readonly GuardianUndoService _undo;
    private readonly IWorkspaceCoordinator _workspace;
    private readonly IWorkdayEventRepository _journal;

    public ObservableCollection<GuardianAuditEntry> Entries { get; } = [];
    public ObservableCollection<WorkdayEvent> TodayEvents { get; } = [];
    public ObservableCollection<WorkdayEvent> WeekEvents { get; } = [];
    public ObservableCollection<WorkdayEvent> TimelineEvents { get; } = [];
    public ObservableCollection<GuardianJournalInsight> Insights { get; } = [];
    public ObservableCollection<ProductFrictionItem> FrictionItems { get; } = [];

    [ObservableProperty] private bool busy;
    [ObservableProperty] private string status = "Loading Guardian activity…";
    [ObservableProperty] private int selectedTabIndex;
    [ObservableProperty] private int frictionWorkingDays;
    [ObservableProperty] private string frictionStatus = "Building the Product Friction evidence base…";
    public bool IsFrictionReady => FrictionWorkingDays >= 10;

    public GuardianActivityViewModel(IGuardianAuditRepository audit, GuardianUndoService undo,
        IWorkspaceCoordinator workspace, IWorkdayEventRepository journal)
    {
        _audit = audit;
        _undo = undo;
        _workspace = workspace;
        _journal = journal;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Busy = true;
        try
        {
            Entries.Clear();
            foreach (var entry in await _audit.GetRecentAsync()) Entries.Add(entry);
            var now = DateTimeOffset.Now;
            var today = await _journal.GetBetweenAsync(new DateTimeOffset(DateTime.Today), new DateTimeOffset(DateTime.Today.AddDays(1)));
            var weekStart = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
            var week = await _journal.GetBetweenAsync(new DateTimeOffset(weekStart), new DateTimeOffset(weekStart.AddDays(7)));
            var timeline = await _journal.GetBetweenAsync(now.AddDays(-31), now.AddDays(1), 1000);
            TodayEvents.Clear(); foreach (var entry in today.OrderBy(x => x.OccurredAt)) TodayEvents.Add(entry);
            WeekEvents.Clear(); foreach (var entry in week.OrderByDescending(x => x.OccurredAt)) WeekEvents.Add(entry);
            TimelineEvents.Clear(); foreach (var entry in timeline) TimelineEvents.Add(entry);
            Insights.Clear(); foreach (var insight in WorkdayJournalService.BuildInsights(timeline)) Insights.Add(insight);
            BuildProductFriction(timeline);
            Status = $"{TodayEvents.Count} meaningful event{(TodayEvents.Count == 1 ? "" : "s")} today · {WeekEvents.Count} this week · {Entries.Count} reversible Guardian action{(Entries.Count == 1 ? "" : "s")}.";
        }
        catch (Exception ex) { Status = $"Activity unavailable: {ex.Message}"; }
        finally { Busy = false; }
    }

    private void BuildProductFriction(IReadOnlyCollection<WorkdayEvent> events)
    {
        FrictionWorkingDays = events.Where(x => x.EventType == "DayStarted")
            .Select(x => x.OccurredAt.LocalDateTime.Date).Distinct().Count();
        OnPropertyChanged(nameof(IsFrictionReady));
        var evidence = events.Select(ToFrictionEvidence).Where(x => x is not null).Select(x => x!).ToList();
        var ranked = evidence.GroupBy(x => x.Area, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProductFrictionItem
            {
                Area = group.Key,
                Frequency = group.Count(),
                MinutesLost = group.Sum(x => x.Minutes),
                AverageAnnoyance = group.Average(x => x.Annoyance),
                LatestOccurrence = group.Max(x => x.OccurredAt),
                Example = group.OrderByDescending(x => x.OccurredAt).First().Description
            })
            .OrderByDescending(x => x.Score).ToList();
        FrictionItems.Clear();
        if (IsFrictionReady) foreach (var item in ranked) FrictionItems.Add(item);
        FrictionStatus = IsFrictionReady
            ? $"Ranked {ranked.Count} friction area{(ranked.Count == 1 ? "" : "s")} from {FrictionWorkingDays} working days · {evidence.Sum(x => x.Minutes)} minutes recorded lost."
            : $"Evidence building: {FrictionWorkingDays}/10 working days · {evidence.Count} friction signal{(evidence.Count == 1 ? "" : "s")} captured. Rankings unlock at 10 days to avoid chasing noise.";
    }

    private static FrictionEvidence? ToFrictionEvidence(WorkdayEvent entry)
    {
        var isFriction = entry.EventType is "PlanDisrupted" or "ProductFriction" ||
                         entry.Outcome.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                         entry.Outcome.Contains("adverse", StringComparison.OrdinalIgnoreCase);
        if (!isFriction) return null;
        var area = entry.EventType == "PlanDisrupted" ? "Interruptions" : entry.Category.DefaultIfBlank(entry.Title);
        var minutes = ReadMetadataNumber(entry.Metadata, "minutesLost") ??
                      ParseMinutes(entry.Narrative) ?? (entry.EventType == "PlanDisrupted" ? 5 : 1);
        var annoyance = ReadMetadataNumber(entry.Metadata, "annoyance") ?? Math.Clamp((entry.Significance - 35) / 12, 1, 5);
        return new(area, Math.Max(1, minutes), Math.Clamp(annoyance, 1, 5), entry.OccurredAt, entry.Narrative);
    }

    private static int? ReadMetadataNumber(string metadata, string property)
    {
        try
        {
            using var json = JsonDocument.Parse(metadata);
            return json.RootElement.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : null;
        }
        catch { return null; }
    }

    private static int? ParseMinutes(string narrative)
    {
        var match = Regex.Match(narrative, @"(?<minutes>\d+)\s+minute", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["minutes"].Value, out var minutes) ? minutes : null;
    }

    private sealed record FrictionEvidence(string Area, int Minutes, int Annoyance,
        DateTimeOffset OccurredAt, string Description);

    [RelayCommand]
    private async Task UndoBatchAsync(GuardianAuditEntry entry)
    {
        Busy = true;
        try
        {
            var result = await _undo.UndoBatchAsync(entry.BatchId);
            Status = result.Message;
            if (result.Success) await _workspace.RefreshAsync();
            await LoadAsync();
            Status = result.Message;
        }
        catch (Exception ex) { Status = $"Undo failed safely: {ex.Message}"; }
        finally { Busy = false; }
    }
}

public sealed class ProductFrictionItem
{
    public string Area { get; init; } = "";
    public int Frequency { get; init; }
    public int MinutesLost { get; init; }
    public double AverageAnnoyance { get; init; }
    public DateTimeOffset LatestOccurrence { get; init; }
    public string Example { get; init; } = "";
    public double Score => Frequency * (MinutesLost / (double)Math.Max(1, Frequency)) * AverageAnnoyance;
    public string ScoreLabel => $"{Score:0} friction points";
    public string EvidenceLabel => $"{Frequency} occurrence{(Frequency == 1 ? "" : "s")} · {MinutesLost}m lost · annoyance {AverageAnnoyance:0.0}/5";
}

file static class ProductFrictionStringExtensions
{
    public static string DefaultIfBlank(this string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
