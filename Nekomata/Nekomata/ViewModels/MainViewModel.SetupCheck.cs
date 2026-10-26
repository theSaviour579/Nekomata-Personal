using System.IO;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Authentication;

namespace Nekomata.UI.ViewModels;

// The setup check ("setup check", "test run", "what's left to set up"): some features need a connection, a bit of
// information from you, or one real run before they're useful. This checks each one live and shows ✓ / ✗ with exactly
// what to say or press to fix it. Things only you can confirm are listed with "say setup done: N". On the first working
// morning it's not complete, it's offered once in Needs you.
public partial class MainViewModel
{
    private static string SetupDonePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "setup-done.json");
    private DateTime _setupOfferedOn;

    private sealed record SetupStep(int Number, string Title, bool Done, string Fix, bool Manual = false);

    private async Task<List<SetupStep>> SetupStepsAsync()
    {
        var manualDone = LoadJson<HashSet<int>>(SetupDonePath) ?? [];
        async Task<bool> Consent(Func<Task<bool>> check) => await SafeAsync(check, false);
        DateTime? Latest<T>(string path, Func<T, DateTime> date) => (LoadJson<List<T>>(path) ?? []).Select(date).DefaultIfEmpty().Max() is var d && d > DateTime.MinValue ? d : null;
        var weekAgo = DateTime.Today.AddDays(-7);
        var microsoftAccount = await SafeAsync(async () => !string.IsNullOrWhiteSpace(
            await _services.GetRequiredService<IMicrosoftAuthenticationService>().GetConnectedAccountAsync()), false);
        var steps = new List<SetupStep>
        {
            new(1, "Microsoft account connected", microsoftAccount, "Settings → CONNECT MICROSOFT"),
            new(2, "Office network learned", Places.Office.Count > 0, "On the office network, say \"I'm in the office\""),
            new(3, "Capture from your phone", CaptureSeen.Count > 0, "Send yourself \"G: test\" in Teams, To Do (Guardian list) or email"),
            new(4, "Weather on Home", HasWeather, "Settings → Weather: your town or postcode"),
            new(5, "Objectives added", Objectives.Any(o => o.Active && o.Title != "New objective"), "Say \"add objective: …\" or use Planner → Objectives"),
            new(6, "Duties match what you do", manualDone.Contains(6), "Planner → Duties: edit the list, then say \"setup done: 6\"", Manual: true),
        };
        return steps;
    }

    private async Task SetupCheckAsync()
    {
        Think("Checking every feature is set up");
        var steps = await SetupStepsAsync();
        var done = steps.Count(s => s.Done);
        var sb = new StringBuilder($"SETUP CHECK · {done} of {steps.Count} done\n\n");
        foreach (var s in steps)
            sb.AppendLine($"{(s.Done ? "✓" : "✗")} {s.Number}. {s.Title}{(s.Done ? "" : $"\n     → {s.Fix}")}");
        sb.AppendLine().Append("Say \"setup check\" again after each change to see it tick.");
        ShowPaletteResponse(sb.ToString());
        var next = steps.FirstOrDefault(s => !s.Done);
        if (next is null) ResolveAttentionByPrefix("setup-check:");
        await SpeakGuardianAsync(next is null ? "Everything's set up. Nice." : $"{done} of {steps.Count} done. Next: {next.Title}. {next.Fix.Replace("→", "then")}.", true);
    }

    private async Task SetupDoneAsync(string argument)
    {
        var manual = (await SetupStepsAsync()).Where(x => x.Manual).Select(x => x.Number).ToList();
        if (!int.TryParse(argument.Trim().TrimStart('#'), out var n) || !manual.Contains(n))
        {
            await RespondAndSpeakAsync($"Step{(manual.Count == 1 ? "" : "s")} {string.Join(" and ", manual)} {(manual.Count == 1 ? "is the one" : "are the ones")} you confirm yourself. Say \"setup done: {manual.FirstOrDefault()}\", for example.");
            return;
        }
        var set = LoadJson<HashSet<int>>(SetupDonePath) ?? [];
        set.Add(n);
        SaveJson(SetupDonePath, set);
        await SetupCheckAsync();
    }

    // First working morning with anything left, from 09:15, once a day until complete.
    private async Task MaybeOfferSetupCheckAsync(DateTimeOffset now)
    {
        var local = now.LocalDateTime;
        if (_setupOfferedOn == local.Date || local.TimeOfDay < new TimeSpan(9, 15, 0) || !_initialLoadCompleted) return;
        _setupOfferedOn = local.Date;
        var steps = await SetupStepsAsync();
        var left = steps.Count(s => !s.Done);
        if (left == 0) return;
        RaiseAttention($"setup-check:{local:yyyyMMdd}", "Guardian", "Info", $"Setup check: {steps.Count - left} of {steps.Count} done",
            $"Next: {steps.First(s => !s.Done).Title}. Open for the full list with what to do for each.", "setup_check");
    }
}
