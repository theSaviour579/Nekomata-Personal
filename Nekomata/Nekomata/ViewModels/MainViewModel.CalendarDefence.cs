using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Planning;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

// Calendar defence. Every 10 minutes Guardian looks at invitations the user hasn't answered (next three weeks) and works out
// what each costs him (InviteAssessor): a clash with a meeting he's in or outside his day -> propose the nearest free
// slot; optional with no agenda -> decline politely; no agenda -> tentative, asking for one; otherwise accept. Each new
// invite goes into "Needs you" with the recommendation; Review shows the reasons and the reply, and "do it" sends that
// response through Outlook. Nothing is answered without the user, and nobody's meeting is ever moved - at most another
// time is proposed for the organiser to take or leave. "Review my invites" lists them all.
public partial class MainViewModel
{
    private static string InvitesSeenPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "invites-seen.json");
    private DateTimeOffset _invitesCheckedAt;
    private bool _invitesBusy;
    private string? _myMailAddress;
    private readonly Dictionary<string, InviteAssessment> _inviteAssessments = new(StringComparer.Ordinal);

    private async Task MaybeCheckInvitesAsync(DateTimeOffset now)
    {
        if (_invitesBusy || now - _invitesCheckedAt < TimeSpan.FromMinutes(10)) return;
        _invitesBusy = true;
        _invitesCheckedAt = now;
        try
        {
            var assessed = await AssessInvitesAsync(now);
            var seen = LoadJson<HashSet<string>>(InvitesSeenPath) ?? [];
            foreach (var a in assessed)
            {
                var key = $"{a.Invite.Id}|{a.Invite.Start:O}";
                if (!seen.Add(key)) continue;
                RaiseAttention($"invite:{a.Invite.Id}", "Calendar", a.Advice == InviteAdvice.Accept ? "Info" : "Warning",
                    $"Invite: {a.Invite.Subject} · {a.Invite.Start.LocalDateTime:ddd d MMM HH:mm} · {a.AdviceLabel}",
                    string.Join(" ", a.Reasons), "review_invite", a.Invite.Id, null, a.Invite.Start);
            }
            SaveJson(InvitesSeenPath, seen.TakeLast(2000).ToHashSet());
            // Answered elsewhere (Outlook, phone): clear their items.
            foreach (var id in _inviteAssessments.Keys.Where(id => assessed.All(a => a.Invite.Id != id)).ToList())
            {
                _inviteAssessments.Remove(id);
                ResolveAttention($"invite:{id}");
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Invite check failed safely: {ex.Message}"); }
        finally { _invitesBusy = false; }
    }

    private async Task<List<InviteAssessment>> AssessInvitesAsync(DateTimeOffset now)
    {
        var events = await _services.GetRequiredService<ICalendarService>().GetEventsAsync(now, now.AddDays(21));
        _myMailAddress ??= await SafeAsync(() => _services.GetRequiredService<IEmailService>().GetMailboxAddressAsync(), "");
        var day = _services.GetRequiredService<WorkingDaySettings>();
        var start = day.GetStart(DateTime.Today).TimeOfDay;
        var end = day.GetEnd(DateTime.Today).TimeOfDay;
        var assessed = events.Where(e => e.IsPendingInvite && e.Start > now)
            .Select(e => InviteAssessor.Assess(e, events, _myMailAddress ?? "", start, end, now)).ToList();
        foreach (var a in assessed) _inviteAssessments[a.Invite.Id] = a;
        return assessed;
    }

    private async Task ReviewInviteAsync(string? eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return;
        if (!_inviteAssessments.ContainsKey(eventId)) await SafeAsync(async () => await AssessInvitesAsync(DateTimeOffset.Now), new List<InviteAssessment>());
        if (!_inviteAssessments.TryGetValue(eventId, out var a))
        {
            ResolveAttention($"invite:{eventId}");
            await RespondAndSpeakAsync("That invite has already been answered or has gone.");
            return;
        }
        ShowPaletteResponse(InviteText(a) + $"\n\nSay do it to {a.AdviceLabel}{(a.Comment.Length > 0 ? " with that note" : "")}. Or answer it in Outlook.");
        SetPendingAutonomyAction($"{a.AdviceLabel} for {a.Invite.Subject}", () => SendInviteResponseAsync(a));
        await SpeakGuardianAsync($"{a.Invite.Subject}, {a.Invite.Start.LocalDateTime:dddd 'at' HH:mm}. {a.Reasons[0]} I'd {a.AdviceLabel}. Say do it and I'll reply.", true);
    }

    private static string InviteText(InviteAssessment a) =>
        $"INVITE · {a.Invite.Subject}\n{a.Invite.Start.LocalDateTime:dddd d MMM HH:mm}-{a.Invite.End.LocalDateTime:HH:mm} · from {a.Invite.Organiser.Split('<')[0].Trim()}\n\n" +
        string.Join("\n", a.Reasons.Select(r => $"• {r}")) +
        $"\n\nRECOMMEND · {a.AdviceLabel}" + (a.Comment.Length > 0 ? $"\nNote to send: \"{a.Comment}\"" : "");

    private async Task SendInviteResponseAsync(InviteAssessment a)
    {
        try
        {
            await _services.GetRequiredService<ICalendarService>().RespondToInviteAsync(a.Invite.Id, a.Response, a.Comment, a.ProposedStart, a.ProposedEnd);
            _inviteAssessments.Remove(a.Invite.Id);
            ResolveAttention($"invite:{a.Invite.Id}");
            RecordDone($"Answered the invite for {a.Invite.Subject}: {a.AdviceLabel}.");
            await RespondAndSpeakAsync($"Done. I've replied to {a.Invite.Subject}: {a.AdviceLabel}.");
        }
        catch (Exception ex) { await RespondAndSpeakAsync($"Outlook wouldn't take that reply: {ex.Message}"); }
    }

    private async Task ReviewAllInvitesAsync()
    {
        Think("Looking at invitations you haven't answered");
        var assessed = await SafeAsync(() => AssessInvitesAsync(DateTimeOffset.Now), new List<InviteAssessment>());
        if (assessed.Count == 0) { await RespondAndSpeakAsync("You've no unanswered invitations in the next three weeks."); return; }
        if (assessed.Count == 1) { await ReviewInviteAsync(assessed[0].Invite.Id); return; }
        ShowPaletteResponse($"UNANSWERED INVITES · {assessed.Count}\n\n" + string.Join("\n\n", assessed.OrderBy(a => a.Invite.Start).Select(InviteText)) +
                            "\n\nEach is in Needs you: Review, then say do it.");
        var notAccept = assessed.Count(a => a.Advice != InviteAdvice.Accept);
        await SpeakGuardianAsync($"{assessed.Count} invitations waiting. {(notAccept == 0 ? "All of them look fine to accept." : $"{notAccept} need a different answer; the details are on screen.")}", true);
    }
}
