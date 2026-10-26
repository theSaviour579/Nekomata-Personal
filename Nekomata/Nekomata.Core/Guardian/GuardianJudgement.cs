using System.Text;

namespace Nekomata.Core.Guardian;

/// <summary>Something that may need the user: how soon (3 now, 2 today, 1 just worth watching) and in what area.</summary>
public sealed record Concern(string Area, string Text, int Weight);

/// <summary>What Guardian can see right now, gathered by the desktop. Unknown sources are flagged so the answer can say so.</summary>
public sealed record SituationFacts
{
    public bool CalendarKnown { get; init; } = true;
    public bool MailKnown { get; init; } = true;
    public IReadOnlyList<(string Severity, string Title)> NeedsYou { get; init; } = [];
    public IReadOnlyList<string> OverdueTasks { get; init; } = [];
    public IReadOnlyList<string> OverduePromises { get; init; } = [];
    public IReadOnlyList<string> PromisesToday { get; init; } = [];
    public IReadOnlyList<string> FailingJobs { get; init; } = [];
    public int ApprovalsWaiting { get; init; }
    public int ApprovalsOld { get; init; }
    public IReadOnlyList<string> ManagersWaiting { get; init; } = [];
    public IReadOnlyList<string> MeetingsWithoutPrep { get; init; } = [];
}

public sealed record JudgementAnswer(bool AllClear, string Spoken, string Written, IReadOnlyList<Concern> Concerns);

/// <summary>
/// Guardian's judgement, kept to evidence. "Anything I need to worry about?" answers no when nothing needs the user,
/// says what it checked, and recommends leaving things alone, rather than finding something to say. When it can't see a
/// source it says so instead of sounding certain. When something does need attention it ranks it and says what can wait.
/// Pure and tested; the desktop gathers the facts.
/// </summary>
public static class GuardianJudgement
{
    public static JudgementAnswer AnythingToWorryAbout(SituationFacts f)
    {
        var concerns = new List<Concern>();
        void Add(string area, string text, int weight) => concerns.Add(new(area, text, weight));

        foreach (var n in f.NeedsYou.Where(n => n.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase)).Take(2)) Add("Needs you", n.Title, 3);
        foreach (var p in f.OverduePromises.Take(2)) Add("Promises", $"your promise to {p} is overdue", 2);
        foreach (var t in f.OverdueTasks.Take(2)) Add("Tasks", $"{t} is overdue", 2);
        foreach (var n in f.NeedsYou.Where(n => n.Severity.Equals("High", StringComparison.OrdinalIgnoreCase)).Take(2)) Add("Needs you", n.Title, 2);
        foreach (var p in f.PromisesToday.Take(2)) Add("Promises", $"you said you'd {p} today", 2);
        if (f.ManagersWaiting.Count > 0) Add("Teams", $"{f.ManagersWaiting[0]} is waiting for a reply on Teams", 2);
        if (f.ApprovalsOld > 0) Add("Approvals", $"{f.ApprovalsOld} approval{(f.ApprovalsOld == 1 ? " has" : "s have")} been waiting over a day", 2);
        foreach (var m in f.MeetingsWithoutPrep.Take(1)) Add("Meetings", $"{m} is coming up soon", 1);
        if (f.FailingJobs.Count > 0) Add("Guardian", $"my {f.FailingJobs[0]} isn't running properly, so I may be missing things", 2);
        if (f.ApprovalsWaiting > f.ApprovalsOld) Add("Approvals", $"{f.ApprovalsWaiting - f.ApprovalsOld} new approval{(f.ApprovalsWaiting - f.ApprovalsOld == 1 ? "" : "s")} waiting", 1);

        var blind = new List<string>();
        if (!f.CalendarKnown) blind.Add("your calendar");
        if (!f.MailKnown) blind.Add("your email");
        var important = concerns.Where(c => c.Weight >= 2).OrderByDescending(c => c.Weight).ToList();
        var watch = concerns.Where(c => c.Weight == 1).ToList();
        var written = new StringBuilder();

        if (important.Count == 0)
        {
            var checkedLine = "Nothing critical" +
                              (f.OverduePromises.Count == 0 ? ", nothing overdue that you owe" : "") +
                              (f.OverdueTasks.Count == 0 ? ", and no overdue tasks" : "");
            var spoken = blind.Count == 0
                ? $"No. {checkedLine}. I'd leave things alone."
                : $"Not as far as I can see. {checkedLine}. But I can't see {string.Join(" or ", blind)} right now, so I can't be sure.";
            if (watch.Count > 0) spoken += $" The only thing I'd keep half an eye on: {watch[0].Text}.";
            written.AppendLine("ANYTHING TO WORRY ABOUT? NO").AppendLine().AppendLine(spoken).AppendLine();
            written.AppendLine("CHECKED").AppendLine("• Needs you, overdue tasks, promises you owe, approvals, managers waiting on Teams,")
                .AppendLine("  upcoming meetings and Guardian's own background jobs");
            if (blind.Count > 0) written.AppendLine($"• Couldn't see: {string.Join(", ", blind)}");
            foreach (var w in watch) written.AppendLine($"• Watching: {w.Text}");
            return new(true, spoken, written.ToString().TrimEnd(), concerns);
        }

        var top = important.Take(3).ToList();
        var count = important.Count;
        var lead = count == 1 ? "One thing." : count <= 3 ? $"{Words(count)} things." : $"{Words(count)} things; here are the three that matter most.";
        var speak = new StringBuilder(lead + " ");
        speak.Append(Sentence(top[0].Text) + ".");
        if (top.Count > 1) speak.Append(" Then " + top[1].Text + ".");
        if (top.Count > 2) speak.Append(" And " + top[2].Text + ".");
        speak.Append(important.All(c => c.Weight < 3) ? " None of it is urgent this minute." : "");
        speak.Append(count <= 3 ? " Everything else can wait." : " The rest can wait; it's on screen.");
        if (blind.Count > 0) speak.Append($" I can't see {string.Join(" or ", blind)} right now.");

        written.AppendLine($"ANYTHING TO WORRY ABOUT? {count}").AppendLine();
        foreach (var c in important) written.AppendLine($"{(c.Weight == 3 ? "NOW" : "TODAY")} · {Sentence(c.Text)}");
        if (watch.Count > 0)
        {
            written.AppendLine().AppendLine("CAN WAIT");
            foreach (var w in watch) written.AppendLine($"• {Sentence(w.Text)}");
        }
        if (blind.Count > 0) written.AppendLine().AppendLine($"Couldn't see: {string.Join(", ", blind)}.");
        return new(false, speak.ToString().Trim(), written.ToString().TrimEnd(), concerns);
    }

    private static string Words(int n) => n switch { 2 => "Two", 3 => "Three", 4 => "Four", 5 => "Five", 6 => "Six", _ => n.ToString() };

    private static string Sentence(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
