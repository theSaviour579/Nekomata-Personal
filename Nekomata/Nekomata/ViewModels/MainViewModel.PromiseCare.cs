using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Commitments;
using Nekomata.Core.Guardian.Neglect;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

// Keeping the promise list true without the user tidying it.
//
// Teaching: "that's not a promise" cancels the commitment and remembers the wording, so anything phrased much the same
// is never counted again; "that project's fine" stops a project being called stalled for 30 days; "that's done" closes
// a promise. The same three are buttons on the home screen.
//
// Closing on its own: when the user writes again on the email thread where he made a promise and that message delivers
// something (an attachment, "please find attached", "here's the...", "as promised"), the promise is marked kept.
//
// Reviewing: "what do I owe" walks through the open promises one at a time. Each answer is done, drop it, book it or
// skip; anything else ends the review and is handled normally. Spoken, the microphone stays open between items.
public partial class MainViewModel
{
    private sealed class NeglectCorrectionStore
    {
        public List<string> NotPromises { get; set; } = [];
        public Dictionary<long, DateTime> ProjectsFineUntil { get; set; } = [];
    }

    private static readonly TimeSpan ProjectFineFor = TimeSpan.FromDays(30);
    private NeglectCorrectionStore? _neglectCorrectionStore;
    private IReadOnlyList<NeglectItem> _lastNeglectItems = [];
    private DateTime _lastHeardAt = DateTime.MinValue;

    private static string NeglectCorrectionsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "neglect-corrections.json");

    private NeglectCorrectionStore NeglectCorrections
    {
        get
        {
            if (_neglectCorrectionStore is not null) return _neglectCorrectionStore;
            try
            {
                if (File.Exists(NeglectCorrectionsPath))
                    _neglectCorrectionStore = JsonSerializer.Deserialize<NeglectCorrectionStore>(File.ReadAllText(NeglectCorrectionsPath));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Neglect corrections unreadable: {ex.Message}"); }
            return _neglectCorrectionStore ??= new NeglectCorrectionStore();
        }
    }

    private void SaveNeglectCorrections()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(NeglectCorrectionsPath)!);
            File.WriteAllText(NeglectCorrectionsPath, JsonSerializer.Serialize(NeglectCorrections));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Neglect corrections not saved: {ex.Message}"); }
    }

    // Worded much like something the user has said wasn't a promise.
    private bool IsTaughtNotAPromise(string summary)
    {
        var words = SignificantWords(summary);
        if (words.Count == 0) return false;
        return NeglectCorrections.NotPromises.Any(taught =>
        {
            if (taught.Equals(summary.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            var other = SignificantWords(taught);
            return Math.Min(words.Count, other.Count) >= 2 && Overlap(words, other) >= 0.75;
        });
    }

    private bool IsProjectMarkedFine(long projectId) =>
        NeglectCorrections.ProjectsFineUntil.TryGetValue(projectId, out var until) && until > DateTime.Now;

    // ---------- Corrections ----------

    private async Task ApplyNeglectCorrectionAsync(string kind, string target)
    {
        if (_promiseReview is not null && kind is "promise" or "done")
        {
            await HandlePromiseReviewAnswerAsync(kind == "done" ? ReviewAnswer.Done : ReviewAnswer.NotAPromise);
            return;
        }
        if (_lastNeglectItems.Count == 0) await SafeAsync(async () => { await DetectNeglectReportAsync(); return true; }, false);

        if (kind == "project")
        {
            await RespondAndSpeakAsync(MarkProjectsFine(target));
            return;
        }

        var promises = _lastNeglectItems.Where(i => i.Promise is not null).ToList();
        var matched = target.Length == 0 ? promises : promises.Where(i => PromiseMatchesTarget(i.Promise!, target)).ToList();
        if (matched.Count == 0)
        {
            await RespondAndSpeakAsync(promises.Count == 0
                ? "There's no promise on the slipping list right now."
                : $"I couldn't tell which one you meant. {PromiseChoices(promises)}");
            return;
        }
        if (matched.Count > 1)
        {
            await RespondAndSpeakAsync($"Which one? {PromiseChoices(matched)}");
            return;
        }

        var promise = matched[0].Promise!;
        var said = kind == "done" ? await MarkPromiseKeptAsync(promise) : await MarkNotAPromiseAsync(promise);
        await RespondAndSpeakAsync(said);
    }

    private static bool PromiseMatchesTarget(NeglectPromise promise, string target)
    {
        var t = target.Trim().ToLowerInvariant();
        if (promise.Counterparty.Length > 0 && promise.Counterparty.StartsWith(t, StringComparison.OrdinalIgnoreCase)) return true;
        var words = SignificantWords(t);
        return words.Count > 0 && words.All(w => SignificantWords(promise.Summary).Contains(w));
    }

    private static string PromiseChoices(IReadOnlyList<NeglectItem> items)
    {
        var named = items.Select(i => i.Promise!.Counterparty.Length > 0 ? $"{i.Promise!.Counterparty}'s" : $"\"{PromiseAction(i.Title)}\"").Distinct().ToList();
        return $"Say, for example, {named[0]} isn't a promise. The options are {SpokenList(named)}.";
    }

    private async Task<string> MarkNotAPromiseAsync(NeglectPromise promise)
    {
        if (!NeglectCorrections.NotPromises.Contains(promise.Summary.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            NeglectCorrections.NotPromises.Add(promise.Summary.Trim());
            SaveNeglectCorrections();
        }
        await SetCommitmentStatusAsync(promise.CommitmentId, CommitmentStatus.Cancelled, "The user said this wasn't a promise.");
        ForgetSlippingPromise(promise);
        return $"Got it. \"{PromiseAction(promise.Summary)}\" isn't a promise, and I won't count anything worded like it again.";
    }

    private async Task<string> MarkPromiseKeptAsync(NeglectPromise promise)
    {
        await SetCommitmentStatusAsync(promise.CommitmentId, CommitmentStatus.Completed, "The user confirmed it was done.");
        ForgetSlippingPromise(promise);
        var text = $"Kept: {PromiseAction(promise.Summary)}{(promise.Counterparty.Length > 0 ? $" for {promise.Counterparty}" : "")}.";
        RecordDone(text);
        return $"Ticked off. {text}";
    }

    private string MarkProjectsFine(string target)
    {
        var stale = _lastNeglectItems.FirstOrDefault(i => i.Kind == NeglectKind.StaleProject);
        var ids = new List<(long Id, string Name)>();
        if (target.Length == 0 && stale is not null)
            ids.AddRange(stale.ProjectIds.Zip(stale.ProjectNames));
        else if (target.Length > 0)
            ids.AddRange(Workspace.Projects.Where(p => p.Name.Contains(target, StringComparison.OrdinalIgnoreCase)).Select(p => (p.Id, p.Name)));
        if (ids.Count == 0)
            return target.Length == 0 ? "There's no stalled project on the list right now." : $"I couldn't find a project called {target}.";

        var until = DateTime.Now.Add(ProjectFineFor);
        foreach (var (id, _) in ids) NeglectCorrections.ProjectsFineUntil[id] = until;
        foreach (var expired in NeglectCorrections.ProjectsFineUntil.Where(p => p.Value < DateTime.Now).Select(p => p.Key).ToList())
            NeglectCorrections.ProjectsFineUntil.Remove(expired);
        SaveNeglectCorrections();
        _lastNeglectItems = _lastNeglectItems.Where(i => i.Kind != NeglectKind.StaleProject || i.ProjectIds.Except(ids.Select(x => x.Id)).Any()).ToList();
        _homeSlipping = _lastNeglectItems;
        RefreshHomeSlipping();
        return ids.Count == 1
            ? $"Understood, {ids[0].Name} is fine. I won't call it stalled for the next month."
            : $"Understood, those {ids.Count} projects are fine. I won't call them stalled for the next month.";
    }

    private async Task SetCommitmentStatusAsync(Guid? id, CommitmentStatus status, string note)
    {
        if (id is not Guid commitmentId) return;
        var commitment = ActiveCommitments.FirstOrDefault(c => c.Id == commitmentId);
        if (commitment is null) return;
        var now = DateTimeOffset.Now;
        var updated = commitment with
        {
            Status = status,
            CompletedAt = status == CommitmentStatus.Completed ? now : commitment.CompletedAt,
            IsUserConfirmed = commitment.IsUserConfirmed || status == CommitmentStatus.Completed,
            EscalationLevel = 0,
            LastObservedAt = now,
            EvidenceSummary = commitment.EvidenceSummary + " " + note
        };
        await _services.GetRequiredService<ICommitmentRepository>().UpsertAsync([updated]);
        ActiveCommitments = ActiveCommitments.Where(c => c.Id != commitmentId).ToList();
    }

    private void ForgetSlippingPromise(NeglectPromise promise)
    {
        _lastNeglectItems = _lastNeglectItems.Where(i => i.Promise is null || !(i.Promise.Summary == promise.Summary && i.Promise.Counterparty == promise.Counterparty)).ToList();
        _homeSlipping = _lastNeglectItems;
        RefreshHomeSlipping();
    }

    // ---------- Closing promises that were kept ----------

    // Email promises only: the sent message that made the promise is found by id, and anything the user sent later on the
    // same conversation that delivers something closes it. Returns what was closed, so it can be mentioned.
    private async Task<List<Commitment>> CloseDeliveredPromisesAsync(IReadOnlyList<Commitment> candidates, IReadOnlyList<EmailMessage> sent)
    {
        var closed = new List<Commitment>();
        if (sent.Count == 0) return closed;
        var byId = sent.Where(m => m.Id.Length > 0).GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var c in candidates.Where(c => c.Direction == CommitmentDirection.OwedByMe && c.SourceReference.Length > 0))
        {
            if (!byId.TryGetValue(c.SourceReference, out var original) || original.ConversationId.Length == 0) continue;
            var thread = c.DeduplicationKey.Contains(':') ? c.DeduplicationKey[..c.DeduplicationKey.IndexOf(':')] : original.ConversationId.ToLowerInvariant();
            var delivered = sent
                .Where(m => m.ConversationId == original.ConversationId && m.ReceivedAt > original.ReceivedAt)
                .OrderBy(m => m.ReceivedAt)
                .FirstOrDefault(m => CommitmentEngine.IsDeliveryOnSameThread(c, new CommitmentEvidence(
                    "Microsoft Outlook", m.Id, $"{m.Subject}. {m.BodyPreview}{(m.HasAttachments ? "\n[Attachment included]" : "")}",
                    UserName, "", m.ReceivedAt, true, CorrelationKey: thread)));
            if (delivered is null) continue;
            await SetCommitmentStatusAsync(c.Id, CommitmentStatus.Completed,
                $"Delivered on the same email thread {delivered.ReceivedAt.LocalDateTime:ddd d MMM HH:mm} (\"{delivered.Subject}\").");
            closed.Add(c);
        }
        if (closed.Count > 0)
            RecordDone(closed.Count == 1 ? $"Spotted you kept a promise: {PromiseAction(closed[0].Summary)}." : $"Spotted {closed.Count} promises you'd already kept.");
        return closed;
    }

    // ---------- "What do I owe": one at a time ----------

    private enum ReviewAnswer { Done, NotAPromise, Book, Skip, Stop }

    private sealed class PromiseReviewState
    {
        public List<Commitment> Items { get; init; } = [];
        public int Index { get; set; }
        public int Kept { get; set; }
        public int Dropped { get; set; }
        public int Booked { get; set; }
        public int Skipped { get; set; }
        public bool Spoken { get; init; }
    }

    private PromiseReviewState? _promiseReview;

    private async Task StartPromiseReviewAsync()
    {
        Think("Going through what you owe people");
        await SafeAsync(async () => { await RefreshCommitmentSummaryAsync(); return true; }, false);
        var me = await SafeAsync(() => _services.GetRequiredService<IEmailService>().GetMailboxAddressAsync(), "");
        var items = ActiveCommitments
            .Where(IsRealPromise)
            .Where(c => !IsTaughtNotAPromise(c.Summary))
            .DistinctBy(c => c.Summary.Trim().ToLowerInvariant())
            .OrderBy(c => c.DueAt ?? DateTimeOffset.MaxValue)
            .Take(15)
            .ToList();
        _reviewMailbox = me;
        if (items.Count == 0)
        {
            await RespondAndSpeakAsync("You don't owe anyone anything I can see. Nothing open from your emails or Teams.");
            return;
        }
        var spoken = DateTime.Now - _lastHeardAt < TimeSpan.FromSeconds(20) || _conversationActive;
        _promiseReview = new PromiseReviewState { Items = items, Spoken = spoken };
        var intro = items.Count == 1 ? "You owe one thing." : $"You owe {items.Count} things. Let's go through them.";
        await AskCurrentReviewItemAsync(intro);
    }

    private string _reviewMailbox = "";

    private async Task AskCurrentReviewItemAsync(string lead = "")
    {
        if (_promiseReview is not { } review) return;
        var c = review.Items[review.Index];
        var who = IsSelf(c.Counterparty, _reviewMailbox) ? "" : PersonFirstName(c.Counterparty);
        var due = c.DueAt is not { } d ? "no date given"
            : d < DateTimeOffset.Now ? $"due {DescribeAgo(d.LocalDateTime)}"
            : $"due {DescribeDue(d.LocalDateTime, DateTime.Now).Replace("by ", "")}";
        var question = $"{(who.Length > 0 ? $"You told {who} you'd" : "You said you'd")} {PromiseSpoken(c.Summary)}, {due}. Done, drop it, book it, or skip?";

        var screen = new List<string> { $"What you owe · {review.Index + 1} of {review.Items.Count}", "" };
        for (var i = 0; i < review.Items.Count; i++)
        {
            var item = review.Items[i];
            var name = IsSelf(item.Counterparty, _reviewMailbox) ? "" : $" · {PersonFirstName(item.Counterparty)}";
            screen.Add($"{(i == review.Index ? "▶" : i < review.Index ? "✓" : "•")} {PromiseAction(item.Summary)}{name}{(item.DueAt is { } dd ? $" · {dd.LocalDateTime:ddd d MMM}" : "")}");
        }
        screen.Add("");
        screen.Add("Say or type: done · drop it (not a promise) · book it · skip · stop");
        ShowPaletteResponse(string.Join(Environment.NewLine, screen));
        await SpeakGuardianAsync((lead.Length > 0 ? lead + " " : "") + question, true);
        ContinueReviewByVoice();
    }

    private static string DescribeAgo(DateTime when)
    {
        var days = (DateTime.Today - when.Date).Days;
        return days <= 0 ? $"at {when:HH:mm} today" : days == 1 ? "yesterday" : days < 7 ? $"last {when:dddd}" : $"{days / 7} week{(days >= 14 ? "s" : "")} ago";
    }

    // Typed or spoken input while a review is running. False means "not an answer": the review ends and the input is
    // handled like any other command.
    private async Task<bool> TryHandlePromiseReviewInputAsync(string input)
    {
        if (_promiseReview is null) return false;
        var text = NormaliseUtterance(input);
        ReviewAnswer? answer = text switch
        {
            "done" or "it's done" or "that's done" or "did it" or "i did it" or "i've done it" or "yes done" or "kept" or "sent" or "sorted" => ReviewAnswer.Done,
            "drop" or "drop it" or "not a promise" or "that's not a promise" or "ignore it" or "ignore that" or "forget it" or "cancel it" or "remove it" or "delete it" => ReviewAnswer.NotAPromise,
            "book" or "book it" or "book time" or "book time for it" or "schedule it" or "find time" or "find time for it" => ReviewAnswer.Book,
            "skip" or "skip it" or "next" or "pass" or "later" or "leave it" or "not now" => ReviewAnswer.Skip,
            "stop" or "that's enough" or "enough" or "finish" or "we're done" or "end review" or "stop the review" or "that's all" => ReviewAnswer.Stop,
            _ => null
        };
        if (answer is null)
        {
            _promiseReview = null;
            return false;
        }
        await HandlePromiseReviewAnswerAsync(answer.Value);
        return true;
    }

    private async Task HandlePromiseReviewAnswerAsync(ReviewAnswer answer)
    {
        if (_promiseReview is not { } review) return;
        var c = review.Items[review.Index];
        var promise = new NeglectPromise(c.Summary, IsSelf(c.Counterparty, _reviewMailbox) ? "" : PersonFirstName(c.Counterparty), c.DueAt, false,
            PromiseSource(c.SourceType), c.Id);
        var said = "";
        switch (answer)
        {
            case ReviewAnswer.Done:
                await SetCommitmentStatusAsync(c.Id, CommitmentStatus.Completed, "The user confirmed it was done during a promise review.");
                ForgetSlippingPromise(promise);
                RecordDone($"Kept: {PromiseAction(c.Summary)}.");
                review.Kept++; said = "Ticked off.";
                break;
            case ReviewAnswer.NotAPromise:
                await MarkNotAPromiseAsync(promise);
                review.Dropped++; said = "Dropped, and noted for next time.";
                break;
            case ReviewAnswer.Book:
                var linked = c.TaskId is long id && Workspace.Tasks.Any(t => t.Id == id && !t.IsCompleted && !t.Completed) ? c.TaskId : null;
                var item = new NeglectItem(NeglectKind.UnbookedPromise, c.Summary, "From your promise review.", 80, "", linked) { Promise = promise };
                said = (await BookNeglectedWorkAsync([item])).Replace(" Say undo to take them out.", "");
                review.Booked++;
                break;
            case ReviewAnswer.Skip:
                review.Skipped++; said = "Skipped.";
                break;
            case ReviewAnswer.Stop:
                review.Skipped += review.Items.Count - review.Index;
                review.Index = review.Items.Count;
                break;
        }

        if (answer != ReviewAnswer.Stop) review.Index++;
        if (review.Index < review.Items.Count)
        {
            await AskCurrentReviewItemAsync(said);
            return;
        }

        _promiseReview = null;
        var parts = new List<string>();
        if (review.Kept > 0) parts.Add($"{review.Kept} done");
        if (review.Dropped > 0) parts.Add($"{review.Dropped} dropped");
        if (review.Booked > 0) parts.Add($"{review.Booked} booked");
        if (review.Skipped > 0) parts.Add($"{review.Skipped} left for later");
        var summary = parts.Count == 0 ? "That's the lot." : $"That's the lot: {SpokenList(parts)}.";
        await RespondAndSpeakAsync((said.Length > 0 && answer != ReviewAnswer.Stop ? said + " " : "") + summary);
    }

    // When the review was asked for by voice and no conversation is running, keep the microphone open between items so
    // the user can just answer.
    private void ContinueReviewByVoice()
    {
        // Inside a conversation the loop already listens for the answer. Outside one the mic stays closed: the user answers
        // on screen, or presses the talk key.
        if (_promiseReview is not { Spoken: true } || _conversationActive || !GuardianVoiceEnabled) return;
        Think("Press the talk key to answer: done, drop it, book it, skip or stop");
    }

    private async Task RunReviewConversationAsync(GuardianListeningService listening)
    {
        _conversationActive = true;
        try
        {
            await WaitForAsync(() => _guardianSpeakingDepth == 0, TurnTimeout);
            await Task.Delay(300);
            var heard = await ListenOnceAsync(listening, followUp: true);
            await RunConversationLoopAsync(listening, heard);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Promise review by voice ended: {ex.Message}");
        }
        finally
        {
            EndConversation();
        }
    }
}
