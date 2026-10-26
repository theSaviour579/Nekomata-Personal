using System.IO;
using System.Text;
using Nekomata.Core.Planning;

namespace Nekomata.UI.ViewModels;

// Recurring duties: the jobs that come round every week, month, quarter or year (a monthly review, an expenses claim,
// a quarterly plan...). Each has a schedule; Guardian puts it in Needs you a few days before it's due (Warning once
// overdue). "What's due?" lists them; "duty done: monthly review" ticks one; the Planner window edits them.
public partial class MainViewModel
{
    private static string DutiesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "duties.json");
    private List<Duty>? _duties;
    private DateTime _dutiesCheckedOn;

    private List<Duty> Duties
    {
        get
        {
            if (_duties is not null) return _duties;
            _duties = LoadJson<List<Duty>>(DutiesPath);
            if (_duties is null) { _duties = DutySchedule.Defaults(); SaveDuties(); }
            return _duties;
        }
    }

    private void SaveDuties() => SaveJson(DutiesPath, _duties ?? []);

    private void MaybeCheckDuties(DateTimeOffset now)
    {
        var today = now.LocalDateTime.Date;
        if (_dutiesCheckedOn == today || now.LocalDateTime.TimeOfDay < new TimeSpan(8, 45, 0)) return;
        _dutiesCheckedOn = today;
        var changed = false;
        foreach (var duty in Duties.Where(d => d.Enabled))
        {
            var due = DutySchedule.NextDue(duty, today);
            // A duty Guardian has never seen done starts from its next date rather than flagging a backlog on day one.
            if (duty.LastDone is null && due < today)
            {
                duty.LastDone = due;
                changed = true;
                due = DutySchedule.NextDue(duty, today);
            }
            var days = (due - today).Days;
            if (days > duty.LeadDays) continue;
            RaiseAttention($"duty:{duty.Id}:{due:yyyyMMdd}", "Duties", days < 0 ? "Warning" : "Info",
                days < 0 ? $"Overdue: {duty.Name}" : days == 0 ? $"Due today: {duty.Name}" : $"Due {due:dddd}: {duty.Name}",
                $"{char.ToUpperInvariant(DutySchedule.Describe(duty)[0])}{DutySchedule.Describe(duty)[1..]}" +
                (duty.LastDone is { } last ? $"; last done {last:d MMM}." : "; not ticked off before.") +
                (duty.Notes.Length > 0 ? $" {duty.Notes}." : "") + " Open to tick it off.", "duty", duty.Id);
        }
        if (changed) SaveDuties();
    }

    private Duty? FindDuty(string text)
    {
        text = text.Trim().TrimEnd('.');
        var exact = Duties.FirstOrDefault(d => d.Name.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var words = SignificantWords(text);
        return Duties.Select(d => (d, n: SignificantWords(d.Name).Count(words.Contains))).Where(x => x.n > 0)
            .OrderByDescending(x => x.n).Select(x => x.d).FirstOrDefault();
    }

    private void MarkDutyDone(Duty duty, DateTime? when = null)
    {
        duty.LastDone = (when ?? DateTime.Today).Date;
        SaveDuties();
        ResolveAttentionByPrefix($"duty:{duty.Id}:");
        _plannerViewModel?.RefreshDuties();
    }

    private async Task DutyDoneAsync(string which)
    {
        var duty = FindDuty(which);
        if (duty is null) { await RespondAndSpeakAsync($"I can't find a duty like \"{which}\". Say what's due for the list."); return; }
        MarkDutyDone(duty);
        await RespondAndSpeakAsync($"Ticked off: {duty.Name}. Next due {DutySchedule.NextDue(duty, DateTime.Today):dddd d MMMM}.");
    }

    private async Task OpenDutyAsync(string id)
    {
        var duty = Duties.FirstOrDefault(d => d.Id == id);
        if (duty is null) { OpenPlanner("duties"); return; }
        var due = DutySchedule.NextDue(duty, DateTime.Today);
        ShowPaletteResponse($"DUTY · {duty.Name}\nDue {due:dddd d MMM} ({DutySchedule.Describe(duty)})" +
                            (duty.LastDone is { } l ? $"\nLast done {l:d MMM yyyy}" : "") + (duty.Notes.Length > 0 ? $"\n{duty.Notes}" : "") +
                            "\n\nSay do it to tick it off as done today.");
        SetPendingAutonomyAction($"tick off \"{duty.Name}\"", () => { MarkDutyDone(duty); return RespondAndSpeakAsync($"Ticked off: {duty.Name}."); });
        await SpeakGuardianAsync($"{duty.Name}, due {due:dddd}. Say do it when it's done.", true);
    }

    private async Task WhatsDueAsync()
    {
        var today = DateTime.Today;
        var list = Duties.Where(d => d.Enabled).Select(d => (d, due: DutySchedule.NextDue(d, today))).OrderBy(x => x.due).ToList();
        var sb = new StringBuilder("DUTIES\n");
        void Section(string title, IEnumerable<(Duty d, DateTime due)> items)
        {
            var l = items.ToList();
            if (l.Count == 0) return;
            sb.AppendLine().AppendLine(title);
            foreach (var (d, due) in l) sb.AppendLine($"• {due:ddd d MMM} · {d.Name}{(d.LastDone is { } last ? $" (last {last:d MMM})" : "")}");
        }
        Section("OVERDUE", list.Where(x => x.due < today));
        Section("NEXT 14 DAYS", list.Where(x => x.due >= today && x.due <= today.AddDays(14)));
        Section("LATER", list.Where(x => x.due > today.AddDays(14)).Take(8));
        sb.AppendLine().Append("Say \"duty done: <name>\" to tick one off, or open the Planner to edit them.");
        ShowPaletteResponse(sb.ToString());
        var overdue = list.Count(x => x.due < today);
        var soon = list.Count(x => x.due >= today && x.due <= today.AddDays(7));
        await SpeakGuardianAsync(overdue + soon == 0 ? "Nothing due this week." :
            $"{(overdue > 0 ? $"{overdue} overdue, " : "")}{soon} due this week{(list.FirstOrDefault().d is { } first ? $". First up: {first.Name}" : "")}.", true);
    }
}
