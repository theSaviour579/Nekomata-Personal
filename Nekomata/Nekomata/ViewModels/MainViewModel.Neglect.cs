using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Models.Actions;
using Nekomata.Core.Commitments;
using Nekomata.Core.Guardian;
using Nekomata.Core.Guardian.Autopilot;
using Nekomata.Core.Guardian.Neglect;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Planning;
using Nekomata.UI.Controls.Common;

namespace Nekomata.UI.ViewModels;

// "What am I not doing?" / "What's slipping?" Built from tasks (overdue, rolled forward in the daily wrap-up, top-value
// work untouched for a week), promises the user made (in sent email or Teams) that are due within two days or overdue by
// up to a week with no time booked, active projects with no activity for two weeks (one line for all of them), and
// emails from people asking him something that he hasn't answered.
// It doesn't just list: it offers to book time for the promises and slipping tasks, then to put stalled projects on
// hold, one "do it" at a time. On Mondays a short version follows the morning briefing.
public partial class MainViewModel
{
    private const int NeglectSpokenItems = 2;

    private sealed record NeglectReport(IReadOnlyList<NeglectItem> Items, int OldPromises, IReadOnlyList<Commitment> Closed);

    private async Task ShowNeglectAsync()
    {
        GuardianPresenceState = GuardianPresenceState.Thinking;
        try
        {
            var report = await DetectNeglectReportAsync();
            var items = report.Items;
            var kept = report.Closed.Count == 0 ? ""
                : report.Closed.Count == 1 ? $"Good news first: you already sent what you promised for \"{PromiseAction(report.Closed[0].Summary)}\", so I've ticked it off. "
                : $"Good news first: you'd already kept {report.Closed.Count} promises, so I've ticked them off. ";
            if (items.Count == 0)
            {
                var old = report.OldPromises > 0
                    ? $" There {(report.OldPromises == 1 ? "is one older promise" : $"are {report.OldPromises} older promises")} more than a week past due that I can't confirm were done; say what do I owe to check."
                    : "";
                await RespondAndSpeakAsync(kept + "Nothing's slipping. No overdue or repeatedly deferred tasks, no unbooked promises, no stalled projects and no one waiting on a reply." + old);
                return;
            }

            var offers = BuildNeglectOffers(items);
            ShowPaletteResponse((kept.Length > 0 ? "✓ " + kept.Trim() + Environment.NewLine + Environment.NewLine : "") +
                                FormatNeglectForScreen(items, report.OldPromises, offers));
            var spoken = kept + FormatNeglectForSpeech(items, intro: true);
            if (offers.Count > 0)
            {
                SetPendingAutonomyAction(offers[0].Description, () => RunNeglectOfferAsync(offers, 0));
                spoken += " " + offers[0].Question + " Say do it.";
            }
            await SpeakGuardianAsync(spoken, true);
        }
        catch (Exception ex)
        {
            ShowPaletteResponse($"I couldn't work that out just now: {ex.Message}");
        }
        finally
        {
            if (GuardianPresenceState == GuardianPresenceState.Thinking) GuardianPresenceState = GuardianPresenceState.Idle;
        }
    }

    private sealed record NeglectOffer(string Description, string Question, string ScreenLine, Func<Task<string>> Apply);

    private List<NeglectOffer> BuildNeglectOffers(IReadOnlyList<NeglectItem> items)
    {
        var offers = new List<NeglectOffer>();
        var bookable = items.Where(i => i.Kind == NeglectKind.UnbookedPromise ||
                                        i.Kind is NeglectKind.SlippingTask or NeglectKind.AvoidedWork && i.TaskId is not null).ToList();
        if (bookable.Count > 0)
        {
            var promises = bookable.Count(i => i.Kind == NeglectKind.UnbookedPromise);
            var tasks = bookable.Count - promises;
            var what = string.Join(" and ", new[]
            {
                promises == 0 ? "" : promises == 1 ? "the promise" : $"the {promises} promises",
                tasks == 0 ? "" : tasks == 1 ? "the slipping task" : $"the {tasks} slipping tasks"
            }.Where(s => s.Length > 0));
            offers.Add(new($"book time for {what}", $"I can book time for {what} now.",
                $"book time for {what} before {(bookable.Count == 1 ? "it's" : "they're")} due", () => BookNeglectedWorkAsync(bookable)));
        }

        var stale = items.FirstOrDefault(i => i.Kind == NeglectKind.StaleProject && i.ProjectIds.Count > 0);
        if (stale is not null)
        {
            var them = stale.ProjectIds.Count == 1 ? stale.ProjectNames[0] : $"the {stale.ProjectIds.Count} stalled projects";
            offers.Add(new($"put {them} on hold", $"Want me to put {them} on hold, so they stop counting as live work?",
                $"put {them} on hold", () => HoldStalledProjectsAsync(stale)));
        }
        return offers;
    }

    // Runs one offer, then lines up the next one behind its own "do it", so nothing consequential happens unasked.
    private async Task RunNeglectOfferAsync(IReadOnlyList<NeglectOffer> offers, int index)
    {
        string said;
        try { said = await offers[index].Apply(); }
        catch (Exception ex) { said = $"That didn't work: {ex.Message}"; }

        if (index + 1 < offers.Count)
        {
            var next = offers[index + 1];
            SetPendingAutonomyAction(next.Description, () => RunNeglectOfferAsync(offers, index + 1));
            said += " " + next.Question + " Say do it.";
        }
        ShowPaletteResponse(said);
        await SpeakGuardianAsync(said, true);
    }

    // A focus block for each promise and slipping task, in the first free slot before it's due (or before the end of
    // today, or the next workday after hours, when it's already overdue). A promise without a task gets one too.
    private async Task<string> BookNeglectedWorkAsync(IReadOnlyList<NeglectItem> items)
    {
        Think($"Finding time for {items.Count} thing{(items.Count == 1 ? "" : "s")} that {(items.Count == 1 ? "is" : "are")} slipping");
        var now = DateTimeOffset.Now;
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        var day = now.LocalDateTime.Date;
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || now.LocalDateTime > settings.GetEnd(day).AddMinutes(-30))
            day = WrapUpPlanningNextWorkday(day);
        var soonest = new DateTimeOffset(settings.GetEnd(day));

        var response = new GuardianActionResponse { ActionType = "neglect_booking", Message = "Time booked for work that was slipping.", Confidence = 90 };
        var claimed = new List<AutopilotEvent>();
        var booked = new List<(string Title, DateTimeOffset Start)>();
        var unbooked = new List<string>();
        foreach (var item in items)
        {
            var task = item.TaskId is long id ? Workspace.Tasks.FirstOrDefault(t => t.Id == id) : null;
            var title = task?.Title ?? PromiseAction(item.Title);
            var minutes = Math.Clamp(task?.EstimatedMinutes ?? 30, 30, 90);
            var due = item.Promise?.DueAt ?? (task?.DueAt is { } d ? new DateTimeOffset(d) : (DateTimeOffset?)null);
            var by = due is { } dd && dd > soonest ? dd : soonest;
            var slot = await FindPromiseSlotAsync(now, by, TimeSpan.FromMinutes(minutes), claimed);
            if (slot is not { } s) { unbooked.Add(title); continue; }

            long entityId;
            if (task is not null) entityId = task.Id;
            else
            {
                var to = item.Promise?.Counterparty is { Length: > 0 } who ? who : "someone";
                response.Tasks.Add(new ProposedTask
                {
                    Title = title,
                    Description = $"Promised to {to}{(item.Promise?.Source is { Length: > 0 } src ? $" ({src})" : "")}: \"{item.Title}\". Added by Guardian from what's slipping.",
                    Priority = "High",
                    EstimatedMinutes = minutes,
                    DueAt = (due ?? by).LocalDateTime,
                    RequiresFocus = minutes >= 30
                });
                entityId = -response.Tasks.Count;
            }
            claimed.Add(new AutopilotEvent($"claimed-{booked.Count}", title, s.Start, s.End, false, true));
            booked.Add((title, s.Start));
            response.Changes.Add(new GuardianChange
            {
                EntityType = "Calendar", Property = "CreateFocusBlock", EntityId = entityId,
                NewValue = $"{s.Start:O}|{s.End:O}|{title}", Reason = $"Slipping: {item.Why}", Confidence = 90
            });
        }

        if (booked.Count == 0)
            return unbooked.Count == 0 ? "Nothing needed booking." : $"I couldn't find a free slot for {SpokenList(unbooked)} before {(unbooked.Count == 1 ? "it's" : "they're")} due. Your calendar is full; something will have to move.";

        var result = await _guardianApplyService.ApplyAsync(response);
        if (!result.Success) return "I couldn't book that: " + string.Join(" ", result.Messages);
        Workspace = await _workspaceCoordinator.RefreshAsync();
        _autopilotLastReplan = DateTimeOffset.MinValue;
        _ = RefreshCalendarAwareObjectiveAsync();
        if (SelectedCalendarDate.Date == DateTime.Today) _ = RefreshCalendarAsync();

        var described = booked.Select(b => $"{SpokenTitle(b.Title)} {(b.Start.LocalDateTime.Date == DateTime.Today ? "" : b.Start.LocalDateTime.Date == DateTime.Today.AddDays(1) ? "tomorrow " : $"{b.Start.LocalDateTime:dddd} ")}at {b.Start.LocalDateTime:HH:mm}");
        var message = $"Booked: {SpokenList(described)}.";
        if (unbooked.Count > 0) message += $" No room for {SpokenList(unbooked.Select(SpokenTitle))}.";
        RecordDone(message);
        return message + " Say undo to take them out.";
    }

    private async Task<string> HoldStalledProjectsAsync(NeglectItem item)
    {
        var repository = _services.GetRequiredService<IProjectRepository>();
        var held = new List<string>();
        foreach (var id in item.ProjectIds)
        {
            var project = await repository.GetByIdAsync(id);
            if (project is null || !project.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)) continue;
            project.Status = "On Hold";
            project.NextAction = string.IsNullOrWhiteSpace(project.NextAction) ? "Paused by Guardian: no activity for weeks. Reactivate with a next action." : project.NextAction;
            await repository.SaveAsync(project);
            if (Workspace.Projects.FirstOrDefault(p => p.Id == id) is { } live)
            {
                live.Status = project.Status;
                live.NextAction = project.NextAction;
            }
            held.Add(project.Name);
            await RecordWorkdayEventAsync(new()
            {
                EventType = "ProjectStateChanged", Category = "Project", Title = $"{project.Name} put on hold",
                Narrative = "The user accepted Guardian's offer to pause a project with no activity for weeks.",
                ProjectId = project.Id, Outcome = "On Hold", UserDecision = "Accepted", Significance = 50
            });
        }
        if (held.Count == 0) return "Those projects had already changed, so I left them alone.";
        var message = held.Count == 1 ? $"{held[0]} is on hold." : $"{held.Count} projects are on hold: {SpokenList(held)}.";
        RecordDone(message);
        return message + " Set any of them back to Active from Projects when it moves again.";
    }

    private async Task SpeakMorningBriefingWithWeeklyAsync()
    {
        await SpeakMorningBriefAsync();
        await SpeakWeeklyNeglectAsync();
        await OfferToPlanTodayAsync();
    }

    /// <summary>The Monday-morning version: short, only if there is something worth saying.</summary>
    private async Task SpeakWeeklyNeglectAsync()
    {
        if (DateTime.Today.DayOfWeek != DayOfWeek.Monday || !_autonomy.IsOn(Nekomata.Core.Guardian.Autonomy.AutonomyAction.WeeklySlipping)) return;
        try
        {
            var items = await DetectNeglectAsync();
            if (items.Count == 0) return;
            await WaitForAsync(() => _guardianSpeakingDepth == 0, TimeSpan.FromMinutes(2));
            await SpeakGuardianAsync("One more thing for the week. " + FormatNeglectForSpeech(items, intro: false) +
                                     " Say what's slipping for the full list.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Weekly neglect summary failed safely: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<NeglectItem>> DetectNeglectAsync() => (await DetectNeglectReportAsync()).Items;

    private async Task<NeglectReport> DetectNeglectReportAsync()
    {
        Think("Looking for what's slipping: deferred tasks, unbooked promises, stalled projects, unanswered emails");
        var now = DateTime.Now;
        var openTasks = Workspace.Tasks.Where(t => !t.IsCompleted && !t.Completed).DistinctBy(t => t.Id).ToList();

        var sessions = await SafeAsync(() => _services.GetRequiredService<IMissionSessionRepository>()
            .GetBetweenAsync(now.AddDays(-30), now.AddDays(1)), []);
        var lastWorkedByTask = sessions.Where(s => s.TaskId is not null)
            .GroupBy(s => s.TaskId!.Value).ToDictionary(g => g.Key, g => g.Max(s => s.FinishedAt > s.StartedAt ? s.FinishedAt : s.StartedAt));
        var lastWorkedByProject = sessions.Where(s => s.ProjectId is not null)
            .GroupBy(s => s.ProjectId!.Value).ToDictionary(g => g.Key, g => g.Max(s => s.FinishedAt > s.StartedAt ? s.FinishedAt : s.StartedAt));
        // A project also moved if one of its tasks was finished, or email/Teams about it was linked to it.
        foreach (var task in Workspace.Tasks.Where(t => t.ProjectId is not null && t.CompletedAt is not null))
            Later(lastWorkedByProject, task.ProjectId!.Value, task.CompletedAt!.Value);
        foreach (var thread in Workspace.WorkThreads ?? [])
        {
            if (thread.PrimaryProjectId is not long pid) continue;
            var linked = thread.Links.Where(l => l.Confidence >= 70 && l.Status != Nekomata.Models.Guardian.WorkThreadLinkStatus.Rejected)
                .Select(l => l.CreatedAt.LocalDateTime).DefaultIfEmpty(DateTime.MinValue).Max();
            if (linked > DateTime.MinValue) Later(lastWorkedByProject, pid, linked);
        }
        var rollForwards = CountRollForwards(now.Date, 15);

        var tasks = openTasks.Select(t => new NeglectTask(t.Id, t.Title, t.DueAt, t.PriorityScore,
            t.IsScheduled && t.StartAt?.Date >= now.Date,
            rollForwards.GetValueOrDefault(t.Id),
            lastWorkedByTask.TryGetValue(t.Id, out var worked) ? worked : null)).ToList();

        var projects = Workspace.Projects.Where(p => !IsProjectMarkedFine(p.Id)).Select(p => new NeglectProject(p.Id, p.Name, p.Status, p.UpdatedAt,
            lastWorkedByProject.TryGetValue(p.Id, out var worked) ? worked : null, p.AtRisk, p.DueAt)).ToList();

        await SafeAsync(async () => { await RefreshCommitmentSummaryAsync(); return true; }, false);
        var email = _services.GetRequiredService<IEmailService>();
        var me = await SafeAsync(() => email.GetMailboxAddressAsync(), "");
        var sentForPromises = await SafeAsync(() => email.GetRecentSentMessagesAsync(100), Array.Empty<EmailMessage>() as IReadOnlyList<EmailMessage>);
        var closed = await SafeAsync(() => CloseDeliveredPromisesAsync(ActiveCommitments.Where(IsRealPromise).ToList(), sentForPromises), new List<Commitment>());
        var bookedTaskIds = _autopilotToday.Where(e => e.IsNekomataManaged && e.End > DateTimeOffset.Now)
            .Select(e => ResolveCalendarTaskId(e)).OfType<long>().ToHashSet();
        var promises = ActiveCommitments
            .Where(IsRealPromise)
            .Where(c => !IsTaughtNotAPromise(c.Summary))
            .Select(c => new NeglectPromise(c.Summary, IsSelf(c.Counterparty, me) ? "" : PersonFirstName(c.Counterparty), c.DueAt,
                c.TaskId is long id && (bookedTaskIds.Contains(id) ||
                                        openTasks.Any(t => t.Id == id && t.IsScheduled && t.StartAt?.Date >= now.Date)),
                PromiseSource(c.SourceType), c.Id))
            .DistinctBy(p => p.Summary.Trim().ToLowerInvariant())
            .ToList();
        var oldPromises = promises.Count(p => !p.HasTimeBooked && p.DueAt is { } due && (now - due.LocalDateTime).TotalHours > NeglectDetector.StalePromiseHours);

        var replies = await FindRepliesOwedAsync(now);
        var items = NeglectDetector.Detect(now, tasks, projects, promises, replies);
        // Carry the linked task through so booking time reuses it rather than adding a duplicate.
        items = items.Select(i => i.Promise is { } p && ActiveCommitments.FirstOrDefault(c => c.Summary == p.Summary && c.TaskId is not null) is { } linked
            && openTasks.Any(t => t.Id == linked.TaskId) ? i with { TaskId = linked.TaskId } : i).ToList();
        _lastNeglectItems = items;
        return new NeglectReport(items, oldPromises, closed);

        static void Later(Dictionary<long, DateTime> map, long key, DateTime value)
        {
            if (!map.TryGetValue(key, out var current) || value > current) map[key] = value;
        }
    }

    // Something the user actually said he'd do: explicit wording (not "we can" or "we should"), in something he wrote.
    // Calendar items and calls hold other people's words, and a counterparty of the user themselves is a note to self.
    private static bool IsRealPromise(Commitment c) =>
        c.Direction == CommitmentDirection.OwedByMe &&
        c.Status != CommitmentStatus.WaitingExternal &&
        (c.Confidence >= 70 || c.IsUserConfirmed) &&
        !c.SourceType.Contains("Calendar", StringComparison.OrdinalIgnoreCase) &&
        !c.SourceType.Contains("Call", StringComparison.OrdinalIgnoreCase);

    private static string PromiseSource(string sourceType) =>
        sourceType.Contains("Teams", StringComparison.OrdinalIgnoreCase) ? "Teams" :
        sourceType.Contains("Mail", StringComparison.OrdinalIgnoreCase) || sourceType.Contains("Outlook", StringComparison.OrdinalIgnoreCase) ? "email" : "";

    private static bool IsSelf(string? counterparty, string mailbox)
    {
        var value = (counterparty ?? "").Trim();
        if (value.Length == 0) return true;
        if (mailbox.Length > 0 && value.Equals(mailbox, StringComparison.OrdinalIgnoreCase)) return true;
        var local = mailbox.Split('@')[0].Split('.', '_', '-').Where(s => s.Length > 0).ToList();
        var first = local.FirstOrDefault() ?? "david";
        var full = string.Join(' ', local);
        return value.Equals(first, StringComparison.OrdinalIgnoreCase) ||
               (full.Length > 0 && value.Equals(full, StringComparison.OrdinalIgnoreCase)) ||
               (local.Count > 1 && value.Equals($"{local[^1]}, {first}", StringComparison.OrdinalIgnoreCase));
    }

    // "Alex.Jenkins@onecom.co.uk" -> "Alex"; "Wells, Sam" -> "Sam"; "Sam Wells" -> "Sam".
    private static string PersonFirstName(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Contains('@'))
        {
            var local = text.Split('@')[0].Split('.', '_', '-').FirstOrDefault(s => s.Length > 0) ?? text;
            return char.ToUpperInvariant(local[0]) + local[1..].ToLowerInvariant();
        }
        return FirstName(text);
    }

    // "I'll send the data tomorrow" -> "Send the data tomorrow".
    private static string PromiseAction(string summary)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(summary.Trim(), @"^(?:i|we)\s*(?:will|'ll|’ll|shall|promise to|commit to)\s+", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text.Length == 0 ? summary : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string PromiseSpoken(string summary)
    {
        var action = PromiseAction(summary);
        return action.Length == 0 ? summary : char.ToLowerInvariant(action[0]) + action[1..];
    }

    // How many recent daily wrap-ups chose "Continue next workday" or "Defer until" for each task.
    private static Dictionary<long, int> CountRollForwards(DateTime today, int days)
    {
        var counts = new Dictionary<long, int>();
        for (var i = 1; i <= days; i++)
        {
            var path = WrapUpPath(today.AddDays(-i));
            if (!File.Exists(path)) continue;
            try
            {
                var draft = System.Text.Json.JsonSerializer.Deserialize<WrapUpDraft>(File.ReadAllText(path));
                foreach (var objective in draft?.Objectives ?? [])
                    if (objective.TaskId is long id && objective.Decision is "Continue next workday" or "Defer until")
                        counts[id] = counts.GetValueOrDefault(id) + 1;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Wrap-up {path} unreadable: {ex.Message}"); }
        }
        return counts;
    }

    // A person (not automated mail) asked the user something directly, 2-14 days ago, and nothing has gone back on that thread.
    private async Task<List<NeglectReply>> FindRepliesOwedAsync(DateTime now)
    {
        var email = _services.GetRequiredService<IEmailService>();
        var inbox = await SafeAsync(() => email.GetRecentInboxAsync(150), Array.Empty<EmailMessage>() as IReadOnlyList<EmailMessage>);
        var sent = await SafeAsync(() => email.GetRecentSentMessagesAsync(100), Array.Empty<EmailMessage>() as IReadOnlyList<EmailMessage>);
        var me = await SafeAsync(() => email.GetMailboxAddressAsync(), "");
        var lastSentByThread = sent.Where(m => m.ConversationId.Length > 0)
            .GroupBy(m => m.ConversationId).ToDictionary(g => g.Key, g => g.Max(m => m.ReceivedAt));

        return inbox
            .Where(m => !InboxTriageText.IsLikelyNoise(m.SenderAddress, m.SenderName, m.Subject))
            .Where(m => (now - m.ReceivedAt.LocalDateTime).TotalDays is >= 2 and <= 14)
            .Where(m => me.Length == 0 || m.ToRecipientAddresses.Any(a => a.Equals(me, StringComparison.OrdinalIgnoreCase)))
            .Where(m => m.Subject.Contains('?') || m.BodyPreview.Contains('?'))
            .GroupBy(m => m.ConversationId.Length > 0 ? m.ConversationId : m.Id)
            .Select(g => g.OrderByDescending(m => m.ReceivedAt).First())
            .Where(m => !(lastSentByThread.TryGetValue(m.ConversationId, out var replied) && replied > m.ReceivedAt))
            .OrderBy(m => m.ReceivedAt)
            .Take(5)
            .Select(m => new NeglectReply(FirstName(m.SenderName), m.Subject, m.ReceivedAt))
            .ToList();
    }

    private static string FormatNeglectForScreen(IReadOnlyList<NeglectItem> items, int oldPromises, IReadOnlyList<NeglectOffer> offers)
    {
        static string Label(NeglectKind kind) => kind switch
        {
            NeglectKind.SlippingTask => "Slipping",
            NeglectKind.AvoidedWork => "Not started",
            NeglectKind.UnbookedPromise => "Promise",
            NeglectKind.StaleProject => "Stalled",
            _ => "Reply owed"
        };
        static string Line(NeglectItem i)
        {
            var title = i.Kind == NeglectKind.UnbookedPromise ? PromiseAction(i.Title) : i.Title;
            var next = i.Kind switch
            {
                NeglectKind.SlippingTask or NeglectKind.AvoidedWork => i.Suggestion,
                NeglectKind.ReplyOwed => i.Suggestion,
                _ => ""
            };
            return $"• {Label(i.Kind)} · {title} — {i.Why}{(next.Length > 0 ? $" → {next}" : "")}";
        }

        var lines = new List<string> { items.Count == 1 ? "One thing is slipping:" : $"{items.Count} things are slipping:" };
        lines.AddRange(items.Select(Line));
        if (oldPromises > 0)
            lines.Add($"Plus {oldPromises} older promise{(oldPromises == 1 ? "" : "s")} more than a week past due that I can't confirm were done; say what do I owe to check them.");
        if (offers.Count > 0)
        {
            lines.Add("");
            lines.Add("I can " + string.Join(", then ", offers.Select(o => o.ScreenLine)) + ". Say do it to start.");
        }
        lines.Add("Wrong? Say \"that's not a promise\", \"that's done\" or \"that project's fine\", and I'll remember.");
        return string.Join(Environment.NewLine, lines);
    }

    // Spoken: the count, the top two in plain words, and the rest as a number. The detail is on screen.
    private static string FormatNeglectForSpeech(IReadOnlyList<NeglectItem> items, bool intro)
    {
        static string Say(NeglectItem i) => i.Kind switch
        {
            NeglectKind.SlippingTask => $"{SpokenTitle(i.Title)} has been {i.Why.TrimEnd('.').ToLowerInvariant()}.",
            NeglectKind.AvoidedWork => $"{SpokenTitle(i.Title)} is high value and {(i.Why.Contains("untouched") ? i.Why[i.Why.IndexOf("untouched", StringComparison.Ordinal)..].TrimEnd('.') : "not started")}.",
            NeglectKind.UnbookedPromise => i.Promise is { } p
                ? $"You told {(p.Counterparty.Length > 0 ? p.Counterparty : "someone")} you'd {PromiseSpoken(i.Title)}{(p.DueAt is { } d && d.LocalDateTime < DateTime.Now ? ", and that's now overdue" : "")}, with nothing booked."
                : $"You promised to {PromiseSpoken(i.Title)}, with nothing booked.",
            NeglectKind.StaleProject => i.ProjectIds.Count > 1
                ? $"{i.ProjectIds.Count} projects haven't moved in weeks."
                : $"The {SpokenTitle(i.Title)} project hasn't moved in {i.Why.Split(' ').SkipWhile(w => !int.TryParse(w, out _)).FirstOrDefault() ?? "a few"} days.",
            _ => $"{i.Title.Split(':')[0]} is waiting on your reply about {SpokenTitle(i.Title[(i.Title.IndexOf(':') + 1)..].Trim())}."
        };

        var top = items.Take(NeglectSpokenItems).ToList();
        var spoken = top.Select(Say).ToList();
        var rest = items.Skip(NeglectSpokenItems).ToList();
        // A collapsed project line is worth naming even when it's not in the top two.
        if (rest.FirstOrDefault(i => i.Kind == NeglectKind.StaleProject) is { } projects)
        {
            spoken.Add(projects.ProjectIds.Count > 1
                ? $"And {projects.ProjectIds.Count} projects haven't moved in weeks."
                : $"And {SpokenTitle(projects.Title)} hasn't moved in weeks.");
            rest.Remove(projects);
        }
        var head = intro ? (items.Count == 1 ? "One thing. " : $"{NumberWord(items.Count)} things. ") : "";
        var tail = rest.Count > 0 ? $" {(rest.Count == 1 ? "One more" : $"{NumberWord(rest.Count)} more")} on screen." : "";
        return head + string.Join(" ", spoken) + tail;
    }

    private static string NumberWord(int n) => n switch
    {
        2 => "Two", 3 => "Three", 4 => "Four", 5 => "Five", 6 => "Six", 7 => "Seven", 8 => "Eight", 9 => "Nine", 10 => "Ten", _ => n.ToString()
    };
}
