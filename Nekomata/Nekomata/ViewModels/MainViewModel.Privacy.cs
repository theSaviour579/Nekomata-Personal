using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Privacy;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Integrations.MicrosoftGraph.People;

namespace Nekomata.UI.ViewModels;

public sealed class PrivacyDirectoryState
{
    public DateTimeOffset RefreshedAt { get; set; }
    public List<KnownPerson> People { get; set; } = [];
    public List<string> Organisations { get; set; } = [];
}

// AI-use policy: nothing reaches the AI provider in a form that identifies other people. The providers swap people,
// addresses, phone numbers, organisations and money amounts for placeholders and put the real values back in the
// answer (Nekomata.AI.Privacy). This part keeps the list of who counts: the user's direct reports (twice a day) plus
// mail contacts as they are seen. It is saved locally, and every AI call is logged to ai-sent.log with its size and
// placeholder counts, never its text. The user's own name and mailbox are never hidden from the AI.
public partial class MainViewModel
{
    internal static readonly string[] PrivacyExempt = ["Guardian", "Nekomata", "Nekomata Personal"];

    private static string PrivacyDirectoryPath => Path.Combine(DataFolder, "privacy-directory.json");
    private static string AiSentLogPath => Path.Combine(DataFolder, "ai-sent.log");
    private static readonly object AiLogGate = new();
    private DateTimeOffset _privacyRefreshedAt;
    private int _privacySavedCount = -1;
    private bool _privacyBusy;

    private void InitialisePrivacy()
    {
        PrivacyDirectory.Exempt(PrivacyExempt);
        PrivacyDirectory.Exempt(_personalProfile.Current.DisplayName, UserFirstName);
        if (LoadJson<PrivacyDirectoryState>(PrivacyDirectoryPath) is { } saved)
        {
            PrivacyDirectory.AddPeople(saved.People);
            PrivacyDirectory.AddOrganisations(saved.Organisations);
            _privacyRefreshedAt = saved.RefreshedAt;
        }
        AiSendLog.Sent += WriteAiSendRecord;
    }

    private static void WriteAiSendRecord(AiSendRecord record)
    {
        try
        {
            lock (AiLogGate)
            {
                Directory.CreateDirectory(DataFolder);
                var info = new FileInfo(AiSentLogPath);
                if (info.Exists && info.Length > 5_000_000) File.Move(AiSentLogPath, AiSentLogPath + ".old", true);
                File.AppendAllText(AiSentLogPath, JsonSerializer.Serialize(record) + Environment.NewLine);
            }
        }
        catch { /* logging never blocks a call */ }
    }

    private async Task MaybeRefreshPrivacyDirectoryAsync(DateTimeOffset now)
    {
        if (_privacyBusy) return;
        _privacyBusy = true;
        try
        {
            if (now - _privacyRefreshedAt >= TimeSpan.FromHours(12))
            {
                _privacyRefreshedAt = now;
                try
                {
                    // The user's own mailbox is never a placeholder.
                    var mailbox = await SafeAsync(() => _services.GetRequiredService<IEmailService>().GetMailboxAddressAsync(), "");
                    PrivacyDirectory.Exempt(mailbox, _personalProfile.Current.DisplayName, UserFirstName);
                    var people = await _services.GetRequiredService<IMicrosoftPeopleService>().GetDirectReportsAsync();
                    PrivacyDirectory.AddPeople(people.Select(p => new KnownPerson(p.DisplayName, p.Email.Length > 0 ? p.Email : null)));
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Directory for AI placeholders not read: {ex.Message}"); }
            }

            var (people2, organisations, exempt) = PrivacyDirectory.Snapshot();
            var count = people2.Count + organisations.Count;
            if (count != _privacySavedCount)
            {
                _privacySavedCount = count;
                SaveJson(PrivacyDirectoryPath, new PrivacyDirectoryState { RefreshedAt = _privacyRefreshedAt, People = people2.ToList(), Organisations = organisations.ToList() });
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Privacy directory not saved: {ex.Message}"); }
        finally { _privacyBusy = false; }
    }

    /// <summary>"What have you sent to the AI?": today's calls from ai-sent.log, by feature, with what was hidden.</summary>
    private async Task AiPrivacyReportAsync()
    {
        var today = DateTime.Today;
        var records = new List<AiSendRecord>();
        try
        {
            lock (AiLogGate)
                if (File.Exists(AiSentLogPath))
                    foreach (var line in File.ReadLines(AiSentLogPath))
                        if (line.Length > 0 && JsonSerializer.Deserialize<AiSendRecord>(line) is { } r && r.At.LocalDateTime.Date == today) records.Add(r);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AI log unreadable: {ex.Message}"); }

        var hidden = records.SelectMany(r => r.Hidden).GroupBy(h => h.Key).ToDictionary(g => g.Key, g => g.Sum(h => h.Value));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("AI CALLS TODAY").AppendLine();
        sb.AppendLine($"{records.Count(r => !r.Refused)} calls to OpenAI, {records.Sum(r => r.Characters):N0} characters in all. " +
            $"{PrivacyDirectory.Count:N0} people are on the protected list.");
        if (hidden.Count > 0)
            sb.AppendLine("Hidden behind placeholders: " + string.Join(", ", hidden.OrderByDescending(h => h.Value).Select(h => $"{h.Value} {h.Key.ToLowerInvariant()}{(h.Value == 1 ? "" : "s")}")) + ".");
        if (records.Count(r => r.Refused) is > 0 and var refused) sb.AppendLine($"{refused} refused (screenshots are never sent).");
        sb.AppendLine().AppendLine("BY FEATURE");
        foreach (var g in records.GroupBy(r => r.Channel).OrderByDescending(g => g.Count()))
            sb.AppendLine($"- {g.Key}: {g.Count()} call{(g.Count() == 1 ? "" : "s")}, {g.Sum(r => r.Hidden.Values.Sum())} placeholders");
        sb.AppendLine().AppendLine($"Voice: Kokoro on this PC is {_guardianSpeech.KokoroStatus}. Speech is transcribed on this PC too.");
        if (_guardianSpeech.LastVoiceProblem.Length > 0) sb.AppendLine($"Last voice problem: {_guardianSpeech.LastVoiceProblem}");
        sb.AppendLine("Until Kokoro is ready, lines that name anyone are spoken with the Windows voice. The log is ai-sent.log in %LOCALAPPDATA%\\Nekomata (sizes and counts only, never text).");
        ShowPaletteResponse(sb.ToString().TrimEnd());
        await SpeakGuardianAsync(records.Count == 0
            ? "Nothing has gone to the AI today."
            : $"{records.Count} AI calls today, with {hidden.Values.Sum()} names, addresses or amounts swapped for placeholders before sending.", true);
    }
}
