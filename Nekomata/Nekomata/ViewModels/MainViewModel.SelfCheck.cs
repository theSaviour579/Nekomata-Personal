using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Interfaces;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Context;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

public sealed record SelfCheckResult(string Name, bool Ok, string Detail, string Fix = "", bool Optional = false);
public sealed class SelfCheckEcho { public bool Ok { get; set; } public string Word { get; set; } = ""; }

// Guardian checks itself. Every working morning (after 08:05) and on "are you working properly?" / "self check", each link
// is tested with a small real call: the Microsoft account (presence), mail, calendar, Teams chats, the AI (a real
// structured answer - the kind of call that failed silently before) and the neural voice. Connections you haven't set up
// are shown as optional rather than broken. Anything broken goes into the Needs You panel on Home with the reason and
// the fix; the full list is also shown in the self-check answer. Read-only.
public partial class MainViewModel
{
    private DateTime _selfCheckedOn = DateTime.MinValue;
    private bool _selfChecking;

    private void MaybeSelfCheck(DateTimeOffset now)
    {
        if (_selfChecking || _selfCheckedOn == now.LocalDateTime.Date || now.LocalDateTime.TimeOfDay < new TimeSpan(8, 5, 0) ||
            now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || !_initialLoadCompleted) return;
        _selfCheckedOn = now.LocalDateTime.Date;
        _ = SelfCheckAsync(onDemand: false);
    }

    /// <summary>The failure plus Microsoft's own reason underneath it, so a consent or role problem can be told apart.</summary>
    private static string GraphWhy(Exception ex) =>
        ex.InnerException is { } inner && !string.IsNullOrWhiteSpace(inner.Message) ? $"{ex.Message} Microsoft said: {Trim(inner.Message.ReplaceLineEndings(" "), 300)}" : ex.Message;

    private async Task<SelfCheckResult> CheckAsync(string name, Func<Task<string>> probe, string fix, bool optional = false, int seconds = 30)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            var detail = await probe().WaitAsync(timeout.Token);
            return new(name, true, detail, "", optional);
        }
        catch (Exception ex)
        {
            return new(name, false, GraphWhy(ex is TimeoutException or OperationCanceledException ? new TimeoutException($"No answer within {seconds} seconds.") : ex), fix, optional);
        }
    }

    /// <summary>Each start-up: a quick systems check (no AI call), announced in one line.</summary>
    private async Task BootSystemsCheckAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(8));
        await SelfCheckAsync(onDemand: false, boot: true);
    }

    private async Task SelfCheckAsync(bool onDemand, bool boot = false)
    {
        if (_selfChecking) return;
        _selfChecking = true;
        if (onDemand) Think("Checking every connection");
        try
        {
            var now = DateTimeOffset.Now;
            var microsoftConnected = await SafeAsync(async () => !string.IsNullOrWhiteSpace(
                await _services.GetRequiredService<IMicrosoftAuthenticationService>().GetConnectedAccountAsync()), false);
            var personalAi = _services.GetRequiredService<PersonalAIProvider>();
            var aiConfigured = personalAi.IsConfigured;
            var results = new List<SelfCheckResult>();
            if (microsoftConnected)
            {
                results.Add(await CheckAsync("Microsoft account", async () =>
                {
                    var p = await _services.GetRequiredService<IGraphContextService>().GetPresenceAsync();
                    return $"signed in · presence {p.Availability}";
                }, "Reconnect Microsoft in Settings, or restart Nekomata."));
                results.Add(await CheckAsync("Mail", async () =>
                {
                    await _services.GetRequiredService<IEmailService>().GetUnreadInboxAsync(1);
                    return "inbox readable";
                }, "Reconnect Microsoft in Settings and accept the mail permission."));
                results.Add(await CheckAsync("Calendar", async () =>
                {
                    var events = await _services.GetRequiredService<ICalendarService>().GetEventsAsync(now, now.AddHours(8));
                    return $"{events.Count} event(s) in the next 8 hours";
                }, "Reconnect Microsoft in Settings and accept the calendar permission."));
                results.Add(boot ? new("Teams chats", true, "checked at the morning self-check", "", true) : await CheckAsync("Teams chats", async () =>
                {
                    var messages = await _services.GetRequiredService<IGraphContextService>().GetChatActivityAsync(now.AddHours(-2), 2, 2);
                    return $"readable · {messages.Count} recent message(s) sampled";
                }, "Teams chats need the Chat.Read permission, which your organisation may not allow.", optional: true));
            }
            else
            {
                results.Add(new("Microsoft account", false, "not connected", "Settings → CONNECT MICROSOFT for calendar, mail, Teams and tasks.", Optional: true));
            }

            if (aiConfigured && !personalAi.SupportsStructuredAnswers)
                results.Add(new("AI (structured answers)", false,
                    $"{personalAi.ActiveProviderName} is available for conversation, but not for structured automation.",
                    "Open Settings → Your Assistant and choose Automatic, Microsoft AI (Entra), or OpenAI if you want structured automation.",
                    Optional: true));
            else if (aiConfigured)
                results.Add(boot ? new("AI (structured answers)", true, "checked at the morning self-check") : await CheckAsync("AI (structured answers)", async () =>
                {
                    var echo = await _services.GetRequiredService<IStructuredAIProvider>().AskStructuredAsync<SelfCheckEcho>(
                        "Reply with Ok = true and Word = 'ready'.", "Self-check.");
                    if (echo is not { Ok: true }) throw new InvalidOperationException("The AI answered, but not in the expected shape.");
                    return "answering in structured form";
                }, "Check the AI provider in Personal settings and that it is reachable.", seconds: 45));
            else
                results.Add(new("AI", false, "not configured", "Personal settings → choose an AI provider.", Optional: true));

            results.Add(new("Voice", !_guardianSpeech.KokoroStatus.StartsWith("unavailable"), $"Kokoro {_guardianSpeech.KokoroStatus}",
                "See crash.log for why Kokoro didn't start; the Windows voice is standing in.", Optional: true));

            var broken = results.Where(r => !r.Ok && !r.Optional).ToList();
            foreach (var r in broken)
                RaiseAttention($"selfcheck:{r.Name.ToLowerInvariant()}:{now:yyyyMMdd}", "Guardian", "Warning", $"{r.Name} isn't working",
                    $"{r.Detail} Fix: {r.Fix}", "self_check");
            // Optional means unavailable by choice (for example Copilot conversation-only), not a fault. Clear any
            // warning left by an older build so Guardian does not keep announcing an issue that no longer exists.
            foreach (var r in results.Where(r => r.Ok || r.Optional))
                ResolveAttentionByPrefix($"selfcheck:{r.Name.ToLowerInvariant()}:");

            var lagging = JobHealthLines();
            if (boot)
            {
                // The start-up announcement: one line, then the details only if something's wrong.
                var line = broken.Count == 0 ? "All systems online." : $"Systems online, except {JoinNames(broken.Select(b => b.Name).Take(3).ToList())}. Details are in the Needs You panel on Home; select See All.";
                if (lagging.Count > 0) line += lagging.Count == 1 ? $" One background job is lagging: {lagging[0]}." : $" {lagging.Count} background jobs are lagging.";
                Think(line);
                await SpeakGuardianAsync(line);
                return;
            }
            if (!onDemand && broken.Count == 0) return;
            var sb = new StringBuilder($"SELF-CHECK · {now.LocalDateTime:ddd d MMM HH:mm}\n\n");
            foreach (var r in results)
                sb.AppendLine($"{(r.Ok ? "✓" : r.Optional ? "–" : "✗")} {r.Name}: {r.Detail}{(r.Ok || r.Fix.Length == 0 ? "" : $"\n    Fix: {r.Fix}")}");
            if (onDemand) ShowPaletteResponse(sb.ToString().TrimEnd());
            await SpeakGuardianAsync(broken.Count == 0
                ? "All systems online: everything you've connected answered."
                : $"{broken.Count} thing{(broken.Count == 1 ? " isn't" : "s aren't")} working: {JoinNames(broken.Select(b => b.Name).Take(4).ToList())}. The details and fixes are {(onDemand ? "shown in this answer" : "in the Needs You panel on Home; select See All")}.", onDemand);
        }
        catch (Exception ex) { Debug.WriteLine($"Self-check failed safely: {ex.Message}"); }
        finally { _selfChecking = false; }
    }
}
