using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.UI.ViewModels;

// Guardian checking its own judgement, from what it already records (the Chronicle, answer ratings and corrections,
// routing choices, desk forecasts): "how is Guardian doing?" shows the last week (or "this month"). "You should have caught
// that: the backup failed on Tuesday" records something it missed, the most important signal of all, and keeps it as a
// fact so future answers know it matters. The Friday week review includes the headline.
public partial class MainViewModel
{
    private async Task<string> BuildScorecardAsync(int days)
    {
        var to = DateTimeOffset.Now;
        var from = to.AddDays(-days);
        var events = await SafeAsync(async () => await _services.GetRequiredService<IWorkdayEventRepository>().GetBetweenAsync(from, to, 5000),
            new List<Nekomata.Models.Guardian.WorkdayEvent>());
        var ratings = Feedback.Where(f => f.At >= from.LocalDateTime).ToList();
        var corrections = ratings.Where(f => f.Rating < 0 && f.Note.Length > 0 && f.Note != "(no detail)").OrderByDescending(f => f.At).Select(f => Trim(f.Note, 120)).ToList();
        return GuardianScorecard.Build(new ScorecardInputs(events, ratings.Count(f => f.Rating > 0), ratings.Count(f => f.Rating < 0), corrections), from, to);
    }

    private async Task ScorecardAsync(string period)
    {
        Think("Looking at how my judgement held up");
        var days = period.Contains("month", StringComparison.OrdinalIgnoreCase) ? 30 : 7;
        var text = await BuildScorecardAsync(days);
        ShowPaletteResponse(text);
        await SpeakGuardianAsync(text.Split('\n').Skip(2).FirstOrDefault() ?? "Here's how I did.", true);
    }

    private async Task RecordMissedAsync(string what)
    {
        what = what.Trim().TrimEnd('.');
        if (what.Length == 0) { await RespondAndSpeakAsync("What did I miss? Say, for example, you should have caught that: the backup failed on Tuesday."); return; }
        await RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
        {
            EventType = "GuardianMissed", Category = "Guardian", Title = "Guardian missed something", Narrative = Trim(what, 400), Outcome = "Missed", Significance = 80
        });
        await SafeAsync(async () =>
        {
            await _services.GetRequiredService<IGuardianMemoryRepository>().AddAsync(new Nekomata.Models.Guardian.GuardianMemory
            { Category = GuardianMemoryCategories.UserFact, Importance = 85, Source = $"{UserName} (missed by Guardian)", Summary = $"Guardian should flag this kind of thing: {Trim(what, 300)}" });
            return true;
        }, false);
        await RespondAndSpeakAsync("Noted, and I'm sorry I missed it. It's in my scorecard, and I'll treat that kind of thing as important from now on.");
    }
}
