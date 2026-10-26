using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.Data.Local;

// Local (file-backed) implementations of Guardian's record stores for Nekomata Personal. They follow the same rules as
// the work edition's database repositories (upsert keys, "first decision wins", ordering), so the Core services behave
// identically without a database server.

public sealed class LocalGuardianInterruptionRepository(LocalDocumentStore store) : IGuardianInterruptionRepository
{
    private const string Name = "guardian-interruptions";
    private sealed class Doc { public List<GuardianInterruption> Items { get; set; } = []; }

    public Task<long> AddAsync(GuardianInterruption interruption) => store.UpdateAsync<Doc, long>(Name, doc =>
    {
        interruption.Id = doc.Items.Select(x => x.Id).DefaultIfEmpty().Max() + 1;
        doc.Items.Add(interruption);
        if (doc.Items.Count > 5000) doc.Items.RemoveRange(0, doc.Items.Count - 5000);
        return interruption.Id;
    });

    public Task<List<GuardianInterruption>> GetTodayAsync() => store.ReadAsync<Doc, List<GuardianInterruption>>(Name, doc =>
        doc.Items.Where(x => x.InterruptedAt.LocalDateTime.Date == DateTime.Today).OrderByDescending(x => x.InterruptedAt).ToList());

    public Task<List<GuardianInterruption>> GetAllAsync() => store.ReadAsync<Doc, List<GuardianInterruption>>(Name, doc =>
        doc.Items.OrderByDescending(x => x.InterruptedAt).Take(1000).ToList());

    public Task MarkResumedAsync(long id) => store.UpdateAsync<Doc>(Name, doc =>
    {
        var item = doc.Items.FirstOrDefault(x => x.Id == id);
        if (item is not null) item.ResumedAt = DateTimeOffset.Now;
    });
}

public sealed class LocalGuardianPolicyRepository(LocalDocumentStore store) : IGuardianPolicyRepository
{
    private const string Name = "guardian-policies";
    private sealed class Application
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PolicyId { get; set; }
        public long? TaskId { get; set; }
        public long? ProjectId { get; set; }
        public bool Counterfactual { get; set; }
        public string Outcome { get; set; } = "Pending";
        public int StrategicMinutes { get; set; }
        public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset? CompletedAt { get; set; }
    }
    private sealed class Doc
    {
        public List<GuardianPolicy> Policies { get; set; } = [];
        public List<Application> Applications { get; set; } = [];
    }

    public Task<List<GuardianPolicy>> GetAllAsync() => store.ReadAsync<Doc, List<GuardianPolicy>>(Name, doc =>
    {
        foreach (var policy in doc.Policies)
        {
            var applied = doc.Applications.Where(x => x.PolicyId == policy.Id).ToList();
            var real = applied.Where(x => !x.Counterfactual).ToList();
            policy.AppliedCount = real.Count;
            policy.SuccessfulCount = real.Count(x => x.Outcome == "Successful");
            policy.NeutralCount = real.Count(x => x.Outcome == "Neutral");
            policy.AdverseCount = real.Count(x => x.Outcome == "Adverse");
            policy.CounterfactualCount = applied.Count(x => x.Counterfactual);
            policy.StrategicMinutesGained = real.Sum(x => x.StrategicMinutes);
            policy.EvidenceTrend = policy.AdverseCount > policy.SuccessfulCount ? "↓ weakening"
                : policy.SuccessfulCount > policy.AdverseCount ? "↑ strengthening" : "→ stable";
        }
        return doc.Policies
            .OrderByDescending(x => x.Lifecycle == "Active")
            .ThenByDescending(x => x.ConfidencePercent)
            .ToList();
    });

    public Task<GuardianPolicy> UpsertEvidenceAsync(GuardianPolicy policy) => store.UpdateAsync<Doc, GuardianPolicy>(Name, doc =>
    {
        var now = DateTimeOffset.Now;
        var existing = doc.Policies.FirstOrDefault(x => x.PolicyKey == policy.PolicyKey);
        if (existing is null)
        {
            if (policy.Id == Guid.Empty) policy.Id = Guid.NewGuid();
            policy.FirstObservedAt = now;
            policy.UpdatedAt = now;
            policy.ActivatedAt = policy.Lifecycle == "Active" ? now : null;
            doc.Policies.Add(policy);
            return policy;
        }

        existing.Name = policy.Name;
        existing.Statement = policy.Statement;
        existing.Action = policy.Action;
        existing.WorkType = policy.WorkType;
        existing.ConfidencePercent = policy.ConfidencePercent;
        existing.EvidenceCount = policy.EvidenceCount;
        existing.UpdatedAt = now;
        existing.Lifecycle = existing.Lifecycle switch
        {
            "Retired" or "Superseded" => existing.Lifecycle,
            "Active" when policy.ConfidencePercent < 65 => "Challenged",
            "Candidate" or "Challenged" when policy.Lifecycle == "Active" => "Active",
            _ => existing.Lifecycle
        };
        if (existing.ActivatedAt is null && policy.Lifecycle == "Active") existing.ActivatedAt = now;
        return existing;
    });

    public Task SetLifecycleAsync(Guid id, string lifecycle) => store.UpdateAsync<Doc>(Name, doc =>
    {
        var policy = doc.Policies.FirstOrDefault(x => x.Id == id);
        if (policy is null) return;
        policy.Lifecycle = lifecycle;
        policy.UpdatedAt = DateTimeOffset.Now;
    });

    public Task AddApplicationsAsync(IEnumerable<Guid> policyIds, long? taskId, long? projectId, bool counterfactual) =>
        store.UpdateAsync<Doc>(Name, doc =>
        {
            foreach (var id in policyIds.Distinct())
                doc.Applications.Add(new Application { PolicyId = id, TaskId = taskId, ProjectId = projectId, Counterfactual = counterfactual });
        });

    public Task<bool> HasApplicationAsync(Guid policyId, long? taskId) => store.ReadAsync<Doc, bool>(Name, doc =>
        doc.Applications.Any(x => x.PolicyId == policyId && x.TaskId == taskId));

    public Task CompleteApplicationsAsync(IEnumerable<Guid> policyIds, long? taskId, long? projectId, string outcome, int strategicMinutes) =>
        store.UpdateAsync<Doc>(Name, doc =>
        {
            foreach (var id in policyIds.Distinct())
            {
                var application = doc.Applications
                    .Where(x => x.PolicyId == id && x.TaskId == taskId && x.ProjectId == projectId && x.Outcome == "Pending")
                    .OrderByDescending(x => x.AppliedAt)
                    .FirstOrDefault();
                if (application is null) continue;
                application.Outcome = outcome;
                application.StrategicMinutes = strategicMinutes;
                application.CompletedAt = DateTimeOffset.Now;
            }
        });
}

public sealed class LocalGuardianDecisionOutcomeRepository(LocalDocumentStore store) : IGuardianDecisionOutcomeRepository
{
    private const string Name = "guardian-decision-outcomes";
    private sealed class Doc { public List<GuardianDecisionOutcome> Items { get; set; } = []; }

    public Task UpsertAsync(GuardianDecisionOutcome value) => store.UpdateAsync<Doc>(Name, doc =>
    {
        var existing = doc.Items.FirstOrDefault(x => x.Id == value.Id);
        if (existing is null)
        {
            doc.Items.Add(value);
            if (doc.Items.Count > 5000) doc.Items.RemoveRange(0, doc.Items.Count - 5000);
            return;
        }

        // The first real decision and the first observed outcome win; later observations never rewrite history.
        if (existing.Decision == GuardianDecisionDisposition.Proposed)
        {
            existing.Decision = value.Decision;
            existing.DecisionReason = value.DecisionReason;
        }
        if (existing.Outcome == GuardianOutcomeDisposition.Pending)
        {
            existing.Outcome = value.Outcome;
            existing.OutcomeEvidence = value.OutcomeEvidence;
        }
        existing.OutcomeMinutes ??= value.OutcomeMinutes;
        existing.DecidedAt ??= value.DecidedAt;
        existing.ObservedAt ??= value.ObservedAt;
    });

    public Task<IReadOnlyList<GuardianDecisionOutcome>> GetPendingAsync(int count = 200) =>
        store.ReadAsync<Doc, IReadOnlyList<GuardianDecisionOutcome>>(Name, doc => doc.Items
            .Where(x => x.Outcome == GuardianOutcomeDisposition.Pending)
            .OrderBy(x => x.RecommendedAt)
            .Take(Math.Clamp(count, 1, 1000))
            .ToList());

    public Task<IReadOnlyList<GuardianDecisionOutcome>> GetRecentAsync(int count = 200) =>
        store.ReadAsync<Doc, IReadOnlyList<GuardianDecisionOutcome>>(Name, doc => doc.Items
            .OrderByDescending(x => x.RecommendedAt)
            .Take(Math.Clamp(count, 1, 1000))
            .ToList());
}

public sealed class LocalGuardianRelationshipRepository(LocalDocumentStore store) : IGuardianRelationshipRepository
{
    private const string Name = "guardian-relationships";
    private sealed class Preference
    {
        public string ContactAddress { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool Excluded { get; set; }
        public string Note { get; set; } = "";
    }
    private sealed class Doc
    {
        public List<GuardianRelationshipObservation> Observations { get; set; } = [];
        public List<Preference> Preferences { get; set; } = [];
    }

    private static string Key(string? address) => (address ?? "").Trim().ToLowerInvariant();

    public Task UpsertObservationAsync(GuardianRelationshipObservation observation) => store.UpdateAsync<Doc>(Name, doc =>
    {
        var address = Key(observation.ContactAddress);
        if (doc.Preferences.Any(x => x.ContactAddress == address && x.Excluded)) return;
        var existing = doc.Observations.FirstOrDefault(x =>
            Key(x.ContactAddress) == address && x.ConversationId == observation.ConversationId);
        if (existing is null) { doc.Observations.Add(observation); return; }
        if (observation.EvidenceQuality < existing.EvidenceQuality) return;
        var index = doc.Observations.IndexOf(existing);
        observation.Id = existing.Id;
        doc.Observations[index] = observation;
    });

    public Task<GuardianRelationshipProfile?> GetProfileAsync(string contactAddress) =>
        store.ReadAsync<Doc, GuardianRelationshipProfile?>(Name, doc =>
            Profiles(doc).FirstOrDefault(x => Key(x.ContactAddress) == Key(contactAddress)));

    public Task<IReadOnlyList<GuardianRelationshipProfile>> GetProfilesAsync(int count = 100) =>
        store.ReadAsync<Doc, IReadOnlyList<GuardianRelationshipProfile>>(Name, doc => Profiles(doc)
            .OrderByDescending(x => x.LastMessageAt ?? DateTimeOffset.MinValue)
            .Take(Math.Clamp(count, 1, 500))
            .ToList());

    public Task SetExcludedAsync(string contactAddress, string displayName, bool excluded, string note = "") =>
        store.UpdateAsync<Doc>(Name, doc =>
        {
            var address = Key(contactAddress);
            var preference = doc.Preferences.FirstOrDefault(x => x.ContactAddress == address);
            if (preference is null) doc.Preferences.Add(preference = new Preference { ContactAddress = address });
            preference.DisplayName = displayName;
            preference.Excluded = excluded;
            preference.Note = note;
        });

    private static IEnumerable<GuardianRelationshipProfile> Profiles(Doc doc) => doc.Observations
        .GroupBy(x => (Address: Key(x.ContactAddress), x.Channel))
        .Select(group =>
        {
            var preference = doc.Preferences.FirstOrDefault(x => x.ContactAddress == group.Key.Address);
            return new GuardianRelationshipProfile
            {
                ContactAddress = group.Key.Address,
                Channel = group.Key.Channel,
                ContactName = group.OrderByDescending(x => x.ObservedAt).First().ContactName,
                IsExcluded = preference?.Excluded ?? false,
                PreferenceNote = preference?.Note ?? "",
                ConversationCount = group.Count(),
                MessageCount = group.Sum(x => x.MessageCount),
                SubstantiveReplyCount = group.Sum(x => x.SubstantiveReplyCount),
                AcknowledgementCount = group.Sum(x => x.AcknowledgementCount),
                ResponseCount = group.Sum(x => x.ResponseCount),
                TotalResponseMinutes = group.Sum(x => x.TotalResponseMinutes),
                ChaseResponseCount = group.Sum(x => x.ChaseResponseCount),
                PromiseCount = group.Sum(x => x.PromiseCount),
                DetailedMessageCount = group.Sum(x => x.DetailedMessageCount),
                ConciseMessageCount = group.Sum(x => x.ConciseMessageCount),
                LastMessageAt = group.Max(x => x.LastMessageAt)
            };
        });
}

public sealed class LocalGuardianOpenLoopRepository(LocalDocumentStore store) : IGuardianOpenLoopRepository
{
    private const string Name = "guardian-open-loops";
    private sealed class Doc { public List<GuardianOpenLoop> Items { get; set; } = []; }

    public Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop openLoop) => store.UpdateAsync<Doc, GuardianOpenLoop>(Name, doc =>
    {
        var existing = string.IsNullOrEmpty(openLoop.SourceExternalId)
            ? null
            : doc.Items.FirstOrDefault(x => x.Source == openLoop.Source && x.SourceExternalId == openLoop.SourceExternalId);
        if (existing is null)
        {
            doc.Items.Add(openLoop);
            Trim(doc);
            return openLoop;
        }

        existing.UpdatedAt = openLoop.UpdatedAt;
        existing.Type = openLoop.Type;
        existing.Title = openLoop.Title;
        existing.Detail = openLoop.Detail;
        existing.WaitingOn = openLoop.WaitingOn;
        existing.DueAt = openLoop.DueAt;
        existing.PersonId = openLoop.PersonId;
        existing.TaskId = openLoop.TaskId;
        existing.ProjectId = openLoop.ProjectId;
        existing.EmailId = openLoop.EmailId;
        existing.TeamsMessageId = openLoop.TeamsMessageId;
        existing.CalendarEventId = openLoop.CalendarEventId;
        existing.WaitingOnAddress = openLoop.WaitingOnAddress;
        existing.ExpectedEvidenceKind = openLoop.ExpectedEvidenceKind;
        openLoop.Id = existing.Id;
        return openLoop;
    });

    public Task<List<GuardianOpenLoop>> GetOpenAsync(int count = 200) => Query(x => x.Status == OpenLoopStatus.Open, count);

    public Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count = 200) =>
        Query(x => x.Status == OpenLoopStatus.Open && x.Type == OpenLoopType.AwaitingThem, count);

    public Task<bool> ResolveAsync(Guid id, GuardianLoopEvidence evidence) => store.UpdateAsync<Doc, bool>(Name, doc =>
    {
        var loop = doc.Items.FirstOrDefault(x => x.Id == id && x.Status == OpenLoopStatus.Open);
        if (loop is null) return false;
        loop.Status = OpenLoopStatus.Resolved;
        loop.UpdatedAt = evidence.OccurredAt;
        loop.ResolvedAt = evidence.OccurredAt;
        loop.ResolutionEventId = evidence.EventId;
        loop.ResolutionSummary = evidence.Summary;
        return true;
    });

    private Task<List<GuardianOpenLoop>> Query(Func<GuardianOpenLoop, bool> predicate, int count) =>
        store.ReadAsync<Doc, List<GuardianOpenLoop>>(Name, doc => doc.Items
            .Where(predicate)
            .OrderBy(x => x.DueAt is null)
            .ThenBy(x => x.DueAt)
            .ThenBy(x => x.CreatedAt)
            .Take(Math.Clamp(count, 1, 1000))
            .ToList());

    private static void Trim(Doc doc)
    {
        // Resolved history older than a year is not needed for any decision.
        var cutoff = DateTimeOffset.Now.AddYears(-1);
        doc.Items.RemoveAll(x => x.Status != OpenLoopStatus.Open && x.UpdatedAt < cutoff);
    }
}

public sealed class LocalGuardianSignalCheckpointRepository(LocalDocumentStore store) : IGuardianSignalCheckpointRepository
{
    private const string Name = "guardian-signal-checkpoints";
    private sealed class Doc { public Dictionary<string, DateTimeOffset> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase); }

    public Task<DateTimeOffset?> GetAsync(string source) => store.ReadAsync<Doc, DateTimeOffset?>(Name, doc =>
        doc.Items.TryGetValue(source, out var value) ? value : null);

    public Task SetAsync(string source, DateTimeOffset observedThrough) => store.UpdateAsync<Doc>(Name, doc =>
    {
        if (!doc.Items.TryGetValue(source, out var current) || observedThrough > current)
            doc.Items[source] = observedThrough;
    });
}

public sealed class LocalGuardianChaseRepository(LocalDocumentStore store) : IGuardianChaseRepository
{
    private const string Name = "guardian-chases";
    private sealed class Doc { public List<GuardianChaseAttempt> Items { get; set; } = []; }

    public Task<GuardianChaseAttempt?> GetPreparedAsync(Guid openLoopId) => store.ReadAsync<Doc, GuardianChaseAttempt?>(Name, doc =>
        doc.Items.Where(x => x.OpenLoopId == openLoopId && x.Status == GuardianChaseStatus.Prepared)
            .OrderByDescending(x => x.AttemptNumber).FirstOrDefault());

    public Task<int> GetAttemptCountAsync(Guid openLoopId) => store.ReadAsync<Doc, int>(Name, doc =>
        doc.Items.Count(x => x.OpenLoopId == openLoopId));

    public Task AddAsync(GuardianChaseAttempt attempt) => store.UpdateAsync<Doc>(Name, doc => doc.Items.Add(attempt));

    public Task<List<GuardianChaseAttempt>> GetByStatusAsync(GuardianChaseStatus status) =>
        store.ReadAsync<Doc, List<GuardianChaseAttempt>>(Name, doc =>
            doc.Items.Where(x => x.Status == status).OrderBy(x => x.PreparedAt).ToList());

    public Task<List<GuardianChaseAttempt>> GetAttemptsAsync(Guid openLoopId) =>
        store.ReadAsync<Doc, List<GuardianChaseAttempt>>(Name, doc =>
            doc.Items.Where(x => x.OpenLoopId == openLoopId).OrderBy(x => x.AttemptNumber).ToList());

    public Task<bool> ApproveAsync(Guid id, DateTimeOffset approvedAt) => store.UpdateAsync<Doc, bool>(Name, doc =>
    {
        var item = doc.Items.FirstOrDefault(x => x.Id == id && x.Status == GuardianChaseStatus.Prepared);
        if (item is null) return false;
        item.Status = GuardianChaseStatus.Approved;
        item.ApprovedAt = approvedAt;
        return true;
    });

    public Task RecordSendFailureAsync(Guid id, DateTimeOffset attemptedAt, string error) => store.UpdateAsync<Doc>(Name, doc =>
    {
        var item = doc.Items.FirstOrDefault(x => x.Id == id && x.Status == GuardianChaseStatus.Approved);
        if (item is null) return;
        item.LastSendAttemptAt = attemptedAt;
        item.LastSendError = error;
        item.SendAttemptCount++;
    });

    public Task<bool> MarkSentAsync(Guid id, DateTimeOffset sentAt, DateTimeOffset nextChaseDueAt) => store.UpdateAsync<Doc, bool>(Name, doc =>
    {
        var item = doc.Items.FirstOrDefault(x => x.Id == id && x.Status == GuardianChaseStatus.Approved);
        if (item is null) return false;
        item.Status = GuardianChaseStatus.Sent;
        item.SentAt = sentAt;
        item.LastSendAttemptAt = sentAt;
        item.LastSendError = "";
        item.SendAttemptCount++;
        item.NextChaseDueAt = nextChaseDueAt;
        return true;
    });

    public Task<bool> CancelAsync(Guid id, DateTimeOffset cancelledAt) => store.UpdateAsync<Doc, bool>(Name, doc =>
    {
        var item = doc.Items.FirstOrDefault(x => x.Id == id &&
            (x.Status == GuardianChaseStatus.Prepared || x.Status == GuardianChaseStatus.Approved));
        if (item is null) return false;
        item.Status = GuardianChaseStatus.Cancelled;
        item.LastSendError = "";
        return true;
    });
}

public sealed class LocalGuardianWorkThreadRepository(LocalDocumentStore store) : IGuardianWorkThreadRepository
{
    private const string Name = "guardian-work-threads";
    private sealed class Doc
    {
        public List<GuardianWorkThread> Threads { get; set; } = [];
        public List<GuardianWorkThreadLink> Links { get; set; } = [];
        public List<GuardianCorrelationEvidence> Evidence { get; set; } = [];
    }

    public Task<GuardianWorkThread> GetOrCreateForProjectAsync(long projectId, string title, Guid sourceEventId) =>
        store.UpdateAsync<Doc, GuardianWorkThread>(Name, doc =>
        {
            var thread = doc.Threads.FirstOrDefault(x => x.PrimaryProjectId == projectId);
            if (thread is null)
            {
                thread = new GuardianWorkThread { Id = Guid.NewGuid(), Title = title, Status = "Active", PrimaryProjectId = projectId };
                doc.Threads.Add(thread);
            }
            else
            {
                thread.Title = title;
                thread.UpdatedAt = DateTimeOffset.UtcNow;
            }
            return Copy(thread, []);
        });

    public Task<GuardianWorkThreadLink> UpsertLinkAsync(GuardianWorkThreadLink link) =>
        store.UpdateAsync<Doc, GuardianWorkThreadLink>(Name, doc =>
        {
            var existing = doc.Links.FirstOrDefault(x =>
                x.ThreadId == link.ThreadId && x.EntityType == link.EntityType && x.EntityKey == link.EntityKey);
            if (existing is null)
            {
                doc.Links.Add(link);
                return link;
            }

            existing.RelationshipType = link.RelationshipType;
            existing.Confidence = Math.Max(existing.Confidence, link.Confidence);
            if (existing.Status != WorkThreadLinkStatus.Confirmed)
            {
                existing.Authority = link.Authority;
                existing.LinkMethod = link.LinkMethod;
                existing.Status = link.Status;
            }
            link.Id = existing.Id;
            return link;
        });

    public Task AddEvidenceAsync(GuardianCorrelationEvidence evidence) => store.UpdateAsync<Doc>(Name, doc =>
    {
        if (!doc.Evidence.Any(x => x.LinkId == evidence.LinkId && x.EvidenceType == evidence.EvidenceType && x.Value == evidence.Value))
            doc.Evidence.Add(evidence);
    });

    public Task ClearEvidenceAsync(Guid linkId) => store.UpdateAsync<Doc>(Name, doc => doc.Evidence.RemoveAll(x => x.LinkId == linkId));

    public Task<GuardianWorkThreadLink?> FindLinkAsync(string entityType, string entityKey, WorkThreadLinkStatus? status = null) =>
        store.ReadAsync<Doc, GuardianWorkThreadLink?>(Name, doc => doc.Links
            .Where(x => x.EntityType == entityType && x.EntityKey == entityKey && (status is null || x.Status == status))
            .OrderByDescending(x => x.Confidence)
            .FirstOrDefault());

    public Task<List<GuardianWorkThread>> GetActiveAsync() => store.ReadAsync<Doc, List<GuardianWorkThread>>(Name, doc => doc.Threads
        .Where(x => x.Status == "Active")
        .OrderByDescending(x => x.UpdatedAt)
        .Select(x => Copy(x, doc.Links.Where(link => link.ThreadId == x.Id).ToList()))
        .ToList());

    public Task<List<GuardianCorrelationEvidence>> GetEvidenceAsync(Guid linkId) =>
        store.ReadAsync<Doc, List<GuardianCorrelationEvidence>>(Name, doc => doc.Evidence.Where(x => x.LinkId == linkId).ToList());

    public Task UpdateEvaluationAsync(Guid linkId, int confidence, WorkThreadLinkStatus status, string linkMethod) =>
        store.UpdateAsync<Doc>(Name, doc =>
        {
            var link = doc.Links.FirstOrDefault(x => x.Id == linkId && x.Authority != WorkThreadLinkAuthority.Explicit);
            if (link is null) return;
            link.Confidence = Math.Clamp(confidence, 0, 100);
            if (link.Status != WorkThreadLinkStatus.Rejected) link.Status = status;
            link.LinkMethod = linkMethod;
        });

    private static GuardianWorkThread Copy(GuardianWorkThread thread, List<GuardianWorkThreadLink> links) => new()
    {
        Id = thread.Id, Title = thread.Title, Status = thread.Status, PrimaryProjectId = thread.PrimaryProjectId,
        CreatedAt = thread.CreatedAt, UpdatedAt = thread.UpdatedAt, Links = links
    };
}

public sealed class LocalGuardianCalendarEffortRepository(LocalDocumentStore store) : IGuardianCalendarEffortRepository
{
    private const string Name = "guardian-calendar-effort";
    private sealed class Doc { public List<GuardianCalendarEffort> Items { get; set; } = []; }

    public Task UpsertAsync(GuardianCalendarEffort effort) => store.UpdateAsync<Doc>(Name, doc =>
    {
        doc.Items.RemoveAll(x => x.CalendarEventId == effort.CalendarEventId);
        doc.Items.Add(effort);
    });

    public Task<List<GuardianCalendarEffort>> GetForThreadAsync(Guid threadId) =>
        store.ReadAsync<Doc, List<GuardianCalendarEffort>>(Name, doc => doc.Items.Where(x => x.ThreadId == threadId).ToList());
}

public sealed class LocalLearnedWorkflowRepository(LocalDocumentStore store) : ILearnedWorkflowRepository
{
    private const string Name = "guardian-learned-workflows";
    private sealed class Doc
    {
        public List<WorkflowObservation> Observations { get; set; } = [];
        public List<LearnedWorkflowPattern> Patterns { get; set; } = [];
        public List<PolicyPromotionDecision> Decisions { get; set; } = [];
    }

    public Task UpsertObservationAsync(WorkflowObservation observation) => store.UpdateAsync<Doc>(Name, doc =>
    {
        var existing = doc.Observations.FirstOrDefault(x => x.WorkThreadId == observation.WorkThreadId &&
            x.TriggerType == observation.TriggerType && x.ResultWorkType == observation.ResultWorkType);
        if (existing is null) { doc.Observations.Add(observation); return; }
        existing.Observed = observation.Observed;
        existing.Evidence = observation.Evidence;
        existing.SourceEventId = observation.SourceEventId;
        existing.ObservedAt = observation.ObservedAt;
    });

    public Task<List<WorkflowObservation>> GetObservationsAsync() =>
        store.ReadAsync<Doc, List<WorkflowObservation>>(Name, doc => doc.Observations.ToList());

    public Task<LearnedWorkflowPattern> UpsertPatternAsync(LearnedWorkflowPattern pattern) =>
        store.UpdateAsync<Doc, LearnedWorkflowPattern>(Name, doc =>
        {
            var existing = doc.Patterns.FirstOrDefault(x => x.TriggerType == pattern.TriggerType && x.ResultWorkType == pattern.ResultWorkType);
            if (existing is null) { doc.Patterns.Add(pattern); return pattern; }
            existing.ComparableThreadCount = pattern.ComparableThreadCount;
            existing.SupportThreadCount = pattern.SupportThreadCount;
            existing.ExceptionThreadCount = pattern.ExceptionThreadCount;
            existing.ConfidencePercent = pattern.ConfidencePercent;
            if (existing.Lifecycle is not ("Promoted" or "Rejected")) existing.Lifecycle = pattern.Lifecycle;
            existing.GuardianPolicyId ??= pattern.GuardianPolicyId;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            return existing;
        });

    public Task AddPromotionDecisionAsync(PolicyPromotionDecision decision) => store.UpdateAsync<Doc>(Name, doc =>
    {
        if (!doc.Decisions.Any(x => x.PatternId == decision.PatternId && x.Decision == decision.Decision && x.SourceEventId == decision.SourceEventId))
            doc.Decisions.Add(decision);
    });

    public Task<LearnedWorkflowPattern?> FindByPolicyIdAsync(Guid policyId) =>
        store.ReadAsync<Doc, LearnedWorkflowPattern?>(Name, doc => doc.Patterns.FirstOrDefault(x => x.GuardianPolicyId == policyId));

    public Task MarkPromotedAsync(Guid patternId, Guid policyId) => store.UpdateAsync<Doc>(Name, doc =>
    {
        var pattern = doc.Patterns.FirstOrDefault(x => x.Id == patternId);
        if (pattern is null) return;
        pattern.Lifecycle = "Promoted";
        pattern.GuardianPolicyId = policyId;
        pattern.UpdatedAt = DateTimeOffset.UtcNow;
    });
}

public sealed class LocalContextSignalRepository(LocalDocumentStore store) : IContextSignalRepository
{
    private const string Name = "guardian-context-signals";
    private sealed class Doc { public List<ContextSignal> Items { get; set; } = []; }

    public Task<ContextSignal> UpsertAsync(ContextSignal signal) => store.UpdateAsync<Doc, ContextSignal>(Name, doc =>
    {
        var existing = doc.Items.FirstOrDefault(x => x.Source == signal.Source && x.ExternalId == signal.ExternalId);
        if (existing is null)
        {
            if (signal.Id == Guid.Empty) signal.Id = Guid.NewGuid();
            doc.Items.Add(signal);
            Trim(doc);
            return signal;
        }

        existing.ConversationId = signal.ConversationId;
        existing.Title = signal.Title;
        existing.Body = signal.Body;
        existing.Person = signal.Person;
        existing.Owner = signal.Owner;
        existing.Status = signal.Status;
        existing.DueAt = signal.DueAt;
        if (signal.ObservedAt > existing.ObservedAt) existing.ObservedAt = signal.ObservedAt;
        existing.SourceEventId = signal.SourceEventId;
        signal.Id = existing.Id;
        return signal;
    });

    public Task<List<ContextSignal>> GetCurrentAsync(int count = 1000) => store.ReadAsync<Doc, List<ContextSignal>>(Name, doc => doc.Items
        .Where(x => x.Disposition != ContextSignalDisposition.Superseded)
        .OrderByDescending(x => x.ObservedAt)
        .Take(count)
        .ToList());

    public Task<List<ContextSignal>> SearchAsync(IReadOnlyList<string> words, int count = 200)
    {
        var terms = words.Select(x => x.Trim()).Where(x => x.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        if (terms.Count == 0) return Task.FromResult(new List<ContextSignal>());
        return store.ReadAsync<Doc, List<ContextSignal>>(Name, doc => doc.Items
            .Where(x => terms.Any(term =>
                x.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Body.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.ObservedAt)
            .Take(count)
            .ToList());
    }

    public Task ResolveAsync(Guid id, ContextSignalDisposition disposition, Guid? threadId, long? taskId, string resolution) =>
        store.UpdateAsync<Doc>(Name, doc =>
        {
            var item = doc.Items.FirstOrDefault(x => x.Id == id);
            if (item is null) return;
            item.Disposition = disposition;
            item.WorkThreadId = threadId;
            item.ExistingTaskId = taskId;
            item.Resolution = resolution;
        });

    private static void Trim(Doc doc)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-180);
        doc.Items.RemoveAll(x => x.ObservedAt < cutoff);
    }
}
