using System.IO;
using System.Text.Json;
using Nekomata.Core.Analytics;
using Nekomata.Core.Commitments;
using Nekomata.Core.Missions;
using Nekomata.Core.Planning;
using Nekomata.Data.Local;

namespace Nekomata.UI.Services.LocalStores;

// File-backed stores for the Core learning and value engines. The work edition keeps these in PostgreSQL;
// Nekomata Personal keeps them beside the rest of the local workspace.

public sealed class LocalWorkDebtRepository(LocalDocumentStore store) : IWorkDebtRepository
{
    private const string Name = "work-debt";
    private sealed class Doc { public List<WorkDebtItem> Items { get; set; } = []; }

    private static readonly WorkDebtStatus[] DecidedStatuses =
        [WorkDebtStatus.ImprovementPlanned, WorkDebtStatus.Resolved, WorkDebtStatus.AcceptedCost, WorkDebtStatus.Dismissed];

    public Task UpsertAsync(IReadOnlyCollection<WorkDebtItem> items, CancellationToken cancellationToken = default)
    {
        if (items.Count == 0) return Task.CompletedTask;
        return store.UpdateAsync<Doc>(Name, doc =>
        {
            foreach (var item in items)
            {
                var index = doc.Items.FindIndex(x => x.ClusterKey == item.ClusterKey);
                if (index < 0) { doc.Items.Add(item); continue; }
                var existing = doc.Items[index];
                if (item.UpdatedAt < existing.UpdatedAt) continue;
                // A decision the user has made about this cluster is kept when the evidence is refreshed.
                doc.Items[index] = item with
                {
                    Id = existing.Id,
                    CreatedAt = existing.CreatedAt,
                    Status = DecidedStatuses.Contains(existing.Status) ? existing.Status : item.Status
                };
            }
        });
    }

    public Task<IReadOnlyList<WorkDebtItem>> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        store.ReadAsync<Doc, IReadOnlyList<WorkDebtItem>>(Name, doc => doc.Items
            .Where(x => x.Status is not (WorkDebtStatus.Resolved or WorkDebtStatus.Dismissed))
            .OrderByDescending(x => x.Status == WorkDebtStatus.Actionable)
            .ThenByDescending(x => x.TotalTimeMinutes)
            .ToList());

    public Task SetStatusAsync(Guid id, WorkDebtStatus status, CancellationToken cancellationToken = default) =>
        store.UpdateAsync<Doc>(Name, doc =>
        {
            var index = doc.Items.FindIndex(x => x.Id == id);
            if (index >= 0) doc.Items[index] = doc.Items[index] with { Status = status, UpdatedAt = DateTime.UtcNow };
        });
}

public sealed class LocalCommitmentRepository(LocalDocumentStore store) : ICommitmentRepository
{
    private const string Name = "commitments";
    private sealed class Doc { public List<Commitment> Items { get; set; } = []; }

    private static bool IsClosed(CommitmentStatus status) =>
        status is CommitmentStatus.Completed or CommitmentStatus.Cancelled or CommitmentStatus.Superseded;

    public Task UpsertAsync(IReadOnlyCollection<Commitment> commitments, CancellationToken cancellationToken = default)
    {
        if (commitments.Count == 0) return Task.CompletedTask;
        return store.UpdateAsync<Doc>(Name, doc =>
        {
            foreach (var value in commitments)
            {
                var index = doc.Items.FindIndex(x => x.DeduplicationKey == value.DeduplicationKey);
                if (index < 0) { doc.Items.Add(value); continue; }
                var existing = doc.Items[index];
                var closed = IsClosed(existing.Status);
                doc.Items[index] = existing with
                {
                    DueAt = value.DueAt ?? existing.DueAt,
                    ExpectedBy = value.ExpectedBy ?? existing.ExpectedBy,
                    CompletedAt = existing.CompletedAt ?? value.CompletedAt,
                    Status = closed ? existing.Status : value.Status,
                    Confidence = Math.Max(existing.Confidence, value.Confidence),
                    EvidenceSummary = closed ? existing.EvidenceSummary : value.EvidenceSummary,
                    LastObservedAt = value.LastObservedAt > existing.LastObservedAt ? value.LastObservedAt : existing.LastObservedAt,
                    EscalationLevel = closed ? existing.EscalationLevel : value.EscalationLevel,
                    IsUserConfirmed = existing.IsUserConfirmed || value.IsUserConfirmed
                };
            }
            var cutoff = DateTimeOffset.Now.AddYears(-1);
            doc.Items.RemoveAll(x => IsClosed(x.Status) && x.LastObservedAt < cutoff);
        });
    }

    public Task<IReadOnlyList<Commitment>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        store.ReadAsync<Doc, IReadOnlyList<Commitment>>(Name, doc => doc.Items
            .Where(x => !IsClosed(x.Status))
            .OrderBy(x => x.DueAt is null)
            .ThenBy(x => x.DueAt)
            .ThenByDescending(x => x.LastObservedAt)
            .ToList());
}

/// <summary>Decision feedback, learned preferences and role-planning history.</summary>
public sealed class LocalLearningStateStore(LocalDocumentStore store) : IDecisionLearningStore, IRoleCorrectionStore, IRoleBiasHistoryStore
{
    private const string Name = "learning-state";

    private sealed class Doc
    {
        public bool LegacyImported { get; set; }
        public List<DecisionFeedback> Feedback { get; set; } = [];
        public List<LearnedPreference> Preferences { get; set; } = [];
        public List<RoleCorrectionOutcome> Corrections { get; set; } = [];
        public List<RoleBiasSnapshot> Biases { get; set; } = [];
    }

    private sealed class LegacyDecisionLearning
    {
        public List<DecisionFeedback> Feedback { get; set; } = [];
        public List<LearnedPreference> Preferences { get; set; } = [];
    }

    // Earlier Personal releases kept decision learning in a single JSON file; it is imported once.
    private static string LegacyDecisionLearningPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "decision-learning.json");

    private static void ImportLegacy(Doc doc)
    {
        if (doc.LegacyImported) return;
        doc.LegacyImported = true;
        try
        {
            if (!File.Exists(LegacyDecisionLearningPath)) return;
            var legacy = JsonSerializer.Deserialize<LegacyDecisionLearning>(File.ReadAllText(LegacyDecisionLearningPath));
            if (legacy is null) return;
            doc.Feedback.AddRange(legacy.Feedback);
            doc.Preferences.AddRange(legacy.Preferences);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Legacy decision learning was not imported: {ex.Message}");
        }
    }

    public Task<DecisionLearningState> LoadAsync(CancellationToken cancellationToken = default) =>
        store.UpdateAsync<Doc, DecisionLearningState>(Name, doc =>
        {
            ImportLegacy(doc);
            var now = DateTimeOffset.Now;
            return new DecisionLearningState(
                doc.Feedback.OrderBy(x => x.Day).ToList(),
                doc.Preferences.Where(x => x.Until > now).ToList());
        });

    public Task RecordFeedbackAsync(DecisionFeedback value, string source, CancellationToken cancellationToken = default) =>
        store.UpdateAsync<Doc>(Name, doc =>
        {
            ImportLegacy(doc);
            var day = value.Day.Date;
            if (doc.Feedback.Any(x => x.ObjectiveKey == value.ObjectiveKey && x.Reason == value.Reason && x.Day.Date == day)) return;
            doc.Feedback.Add(new DecisionFeedback { ObjectiveKey = value.ObjectiveKey, Title = value.Title, Reason = value.Reason, Day = day });
        });

    public Task SetPreferenceAsync(LearnedPreference value, string approvedBy, CancellationToken cancellationToken = default) =>
        store.UpdateAsync<Doc>(Name, doc =>
        {
            ImportLegacy(doc);
            var existing = doc.Preferences.FirstOrDefault(x => x.ObjectiveKey == value.ObjectiveKey);
            if (existing is null) doc.Preferences.Add(new LearnedPreference { ObjectiveKey = value.ObjectiveKey, Until = value.Until });
            else if (value.Until > existing.Until) existing.Until = value.Until;
        });

    public Task ForgetAsync(string objectiveKey, CancellationToken cancellationToken = default) =>
        store.UpdateAsync<Doc>(Name, doc =>
        {
            ImportLegacy(doc);
            doc.Preferences.RemoveAll(x => x.ObjectiveKey == objectiveKey);
            doc.Feedback.RemoveAll(x => x.ObjectiveKey == objectiveKey);
        });

    Task<IReadOnlyList<RoleCorrectionOutcome>> IRoleCorrectionStore.LoadAsync(CancellationToken cancellationToken) =>
        store.ReadAsync<Doc, IReadOnlyList<RoleCorrectionOutcome>>(Name, doc => doc.Corrections.OrderBy(x => x.RecommendedAt).ToList());

    public Task UpsertAsync(RoleCorrectionOutcome value, CancellationToken cancellationToken = default) =>
        store.UpdateAsync<Doc>(Name, doc =>
        {
            var index = doc.Corrections.FindIndex(x => x.Id == value.Id);
            if (index < 0) { doc.Corrections.Add(value); return; }
            var existing = doc.Corrections[index];
            var existingUpdated = existing.ObservedAt ?? existing.DecidedAt ?? existing.RecommendedAt;
            var incomingUpdated = value.ObservedAt ?? value.DecidedAt ?? value.RecommendedAt;
            if (incomingUpdated >= existingUpdated) doc.Corrections[index] = value;
        });

    Task<IReadOnlyList<RoleBiasSnapshot>> IRoleBiasHistoryStore.LoadAsync(CancellationToken cancellationToken) =>
        store.ReadAsync<Doc, IReadOnlyList<RoleBiasSnapshot>>(Name, doc => doc.Biases.OrderBy(x => x.WeekStarting).ToList());

    public Task UpsertAsync(IEnumerable<RoleBiasSnapshot> values, CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        return store.UpdateAsync<Doc>(Name, doc =>
        {
            foreach (var value in list)
            {
                doc.Biases.RemoveAll(x => x.WeekStarting.Date == value.WeekStarting.Date && x.BandKey == value.BandKey);
                doc.Biases.Add(value with { WeekStarting = value.WeekStarting.Date });
            }
        });
    }
}
