using System.IO;
using System.Text;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian;
using Nekomata.Integrations.MicrosoftGraph.Context;
using Nekomata.Integrations.MicrosoftGraph.Mail;

namespace Nekomata.UI.ViewModels;

// "Keep an eye on it": watches the user sets by voice or typing (GuardianWatches). Every two minutes, while any are
// open, Guardian reads what it can already see - the inbox and Teams chats - and tells them when one is met: spoken
// (an asked-for alert may break through a meeting, never a call), in Needs you, and on their phone via the Teams
// self-chat if they're away from the desk. Watches end when met, or quietly when they expire. Read-only.
public partial class MainViewModel
{
    private static string WatchesPath => Path.Combine(DataFolder, "watches.json");
    private List<Watch>? _watches;
    private List<Watch> Watches => _watches ??= LoadJson<List<Watch>>(WatchesPath) ?? [];
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromMinutes(2) };
    private bool _watchBusy;

    private void InitialiseWatches()
    {
        _watchTimer.Tick += (_, _) => _ = CheckWatchesAsync();
        _watchTimer.Start();
    }

    private async Task AddWatchAsync(string text)
    {
        var now = DateTimeOffset.Now;
        var watch = GuardianWatches.Parse(text, now);
        if (watch is null) { await RespondAndSpeakAsync("I didn't catch what to watch. Try: tell me when Sam replies, or let me know when Kev messages me."); return; }
        Watches.RemoveAll(w => w.Kind == watch.Kind && w.Target.Equals(watch.Target, StringComparison.OrdinalIgnoreCase) && w.FiredAt is null);
        Watches.Add(watch);
        SaveJson(WatchesPath, Watches);
        await RespondAndSpeakAsync($"I'll keep an eye on it: I'll tell you about {GuardianWatches.Describe(watch)}" +
                                   (watch.ExpiresAt - now < TimeSpan.FromDays(2) ? $", until {watch.ExpiresAt.LocalDateTime:ddd HH:mm}." : "."));
        _ = CheckWatchesAsync();
    }

    private void ListWatches()
    {
        var open = Watches.Where(w => w.FiredAt is null && w.ExpiresAt > DateTimeOffset.Now).OrderBy(w => w.CreatedAt).ToList();
        if (open.Count == 0) { ShowPaletteResponse("I'm not watching anything for you at the moment. Try: tell me when Sam replies."); return; }
        var sb = new StringBuilder("WATCHING FOR YOU\n");
        foreach (var w in open) sb.AppendLine($"• {Capitalise(GuardianWatches.Describe(w))} · since {w.CreatedAt.LocalDateTime:ddd HH:mm}, until {w.ExpiresAt.LocalDateTime:ddd HH:mm}");
        sb.AppendLine().AppendLine("Say \"stop watching …\" to cancel one, or \"cancel all watches\".");
        ShowPaletteResponse(sb.ToString().TrimEnd());
        _ = SpeakGuardianAsync($"I'm watching {open.Count} thing{(open.Count == 1 ? "" : "s")} for you: {SpokenList(open.Select(GuardianWatches.Describe).ToList())}.");
    }

    private async Task CancelWatchAsync(string what)
    {
        var open = Watches.Where(w => w.FiredAt is null).ToList();
        var words = what.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).ToList();
        var matches = words.Count == 0 ? open
            : open.Where(w => words.Any(x => (w.Target + " " + w.About).Contains(x.TrimStart('#'), StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var w in matches) Watches.Remove(w);
        SaveJson(WatchesPath, Watches);
        await RespondAndSpeakAsync(matches.Count == 0 ? "I wasn't watching anything like that."
            : matches.Count == 1 ? $"I've stopped watching for {GuardianWatches.Describe(matches[0])}." : $"I've stopped {matches.Count} watches.");
    }

    private async Task CheckWatchesAsync()
    {
        var now = DateTimeOffset.Now;
        var open = Watches.Where(w => w.FiredAt is null).ToList();
        if (_watchBusy || open.Count == 0 || IsInitialLoading) return;
        _watchBusy = true;
        try
        {
            var since = open.Min(w => w.CreatedAt);
            var needMail = open.Any(w => w.Kind is WatchKind.Reply);
            var mail = needMail
                ? await SafeAsync(() => _services.GetRequiredService<IEmailService>().GetRecentInboxAsync(100), (IReadOnlyList<EmailMessage>)Array.Empty<EmailMessage>())
                : Array.Empty<EmailMessage>();
            var chats = open.Any(w => w.Kind == WatchKind.TeamsMessage)
                ? await SafeAsync(() => _services.GetRequiredService<IGraphContextService>().GetChatActivityAsync(since, 30, 10), (IReadOnlyList<GraphChatMessage>)Array.Empty<GraphChatMessage>())
                : Array.Empty<GraphChatMessage>();

            var evidence = new WatchEvidence(
                mail.Select(m => new WatchMail(m.SenderName, m.SenderAddress, m.Subject, m.ReceivedAt)).ToList(),
                chats.Select(c => new WatchChat(c.Sender, c.Body, c.CreatedAt, c.IsFromCurrentUser)).ToList());

            var changed = false;
            foreach (var w in open)
            {
                if (GuardianWatches.Check(w, evidence) is { } news)
                {
                    w.FiredAt = now;
                    changed = true;
                    await TellWatchAsync(w, news);
                }
                else if (now > w.ExpiresAt)
                {
                    w.FiredAt = now;
                    changed = true;
                    RecordDone($"Stopped watching for {GuardianWatches.Describe(w)}: nothing by {w.ExpiresAt.LocalDateTime:ddd HH:mm}.");
                }
            }
            if (changed)
            {
                Watches.RemoveAll(w => w.FiredAt is { } f && now - f > TimeSpan.FromDays(7));
                SaveJson(WatchesPath, Watches);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Watch check skipped: {ex.Message}"); }
        finally { _watchBusy = false; }
    }

    private async Task TellWatchAsync(Watch w, string news)
    {
        RaiseAttention($"watch:{w.Id}", "Watch", "High", news, $"You asked: \"{w.Said}\".", "none");
        RecordDone($"Watch met: {news}");
        await RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
        {
            EventType = "WatchMet", Category = "Guardian", Title = "Watch met", Narrative = news, Outcome = "Met", Significance = 50
        });
        if (_awaySince is not null)
            await SafeAsync(async () => { await _services.GetRequiredService<IGraphContextService>().SendChatMessageAsync("48:notes", $"🛡 {news}"); return true; }, false);
        await SpeakUrgentAsync("watch", news, interrupt: false);
    }
}
