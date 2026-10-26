namespace Nekomata.Core.Guardian.Autonomy;

/// <summary>Do it and tell the user (with undo), ask them first, or don't do it at all.</summary>
public enum AutonomyMode { Auto, Ask, Off }

public enum AutonomyAction
{
    MoveOwnBlocks,
    RescheduleMissedBlocks,
    PromisesToTasks,
    BookTimeForPromises,
    OffPlanNudges,
    MorningPlanOffer,
    WeeklySlipping,
    PrepareReplies,
    SelfTuning,
    DelegationChasers,
    MeetingFollowThrough
}

public sealed record AutonomyActionInfo(AutonomyAction Action, string Label, string Description, bool SupportsAsk, AutonomyMode Default);

/// <summary>
/// One place that decides what Guardian may do on its own. Everything Auto is announced and undoable; Ask means Guardian
/// proposes and waits for "do it"; Off means it doesn't happen. Meetings with other people are never moved, whatever
/// the setting - that rule lives in the planner, not here.
/// </summary>
public sealed class GuardianAutonomySettings
{
    public static readonly IReadOnlyList<AutonomyActionInfo> Catalogue =
    [
        new(AutonomyAction.MoveOwnBlocks, "Rearrange my focus blocks",
            "Move Guardian's own focus blocks when a meeting lands on them or they run past the end of the day.", true, AutonomyMode.Auto),
        new(AutonomyAction.RescheduleMissedBlocks, "Reschedule missed blocks",
            "Move a focus block you never started to later today.", true, AutonomyMode.Auto),
        new(AutonomyAction.PromisesToTasks, "Turn email promises into tasks",
            "When you promise something in an email you send, add it as a task with the date you gave.", true, AutonomyMode.Auto),
        new(AutonomyAction.BookTimeForPromises, "Book time for promises",
            "Find a free slot before a promise is due and block it out (needs promises to tasks).", false, AutonomyMode.Auto),
        new(AutonomyAction.OffPlanNudges, "Nudge me when I drift",
            "Ten minutes into a focus block you're not on, say so once.", false, AutonomyMode.Auto),
        new(AutonomyAction.MorningPlanOffer, "Offer to plan my day",
            "After the morning briefing, offer to lay out the day if nothing is booked.", false, AutonomyMode.Auto),
        new(AutonomyAction.WeeklySlipping, "Monday slipping summary",
            "After Monday's briefing, say what's been slipping.", false, AutonomyMode.Auto),
        new(AutonomyAction.PrepareReplies, "Draft replies in advance",
            "When an email clearly needs your reply, draft one in your style and leave it in Outlook Drafts. Never sent.", false, AutonomyMode.Auto),
        new(AutonomyAction.SelfTuning, "Learn from my corrections",
            "Each week, adjust when focus work is booked and how nudges work, based on what you undo and ignore.", false, AutonomyMode.Auto),
        new(AutonomyAction.DelegationChasers, "Chase delegated work",
            "When something you handed over is due with no reply, draft a chaser in Outlook Drafts.", false, AutonomyMode.Auto),
        new(AutonomyAction.MeetingFollowThrough, "Meeting follow-through",
            "After a Teams meeting with a transcript: decisions, your actions as tasks, others' actions tracked, a follow-up email drafted.", false, AutonomyMode.Auto)
    ];

    public Dictionary<string, AutonomyMode> Modes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AutonomyMode Get(AutonomyAction action)
    {
        var info = Catalogue.First(c => c.Action == action);
        if (!Modes.TryGetValue(action.ToString(), out var mode)) return info.Default;
        return mode == AutonomyMode.Ask && !info.SupportsAsk ? AutonomyMode.Off : mode;
    }

    public void Set(AutonomyAction action, AutonomyMode mode)
    {
        var info = Catalogue.First(c => c.Action == action);
        Modes[action.ToString()] = mode == AutonomyMode.Ask && !info.SupportsAsk ? AutonomyMode.Off : mode;
    }

    public bool IsOn(AutonomyAction action) => Get(action) != AutonomyMode.Off;

    /// <summary>A short spoken summary: what Guardian does on its own, what it asks about, what's off.</summary>
    public string Describe()
    {
        string List(AutonomyMode mode) => string.Join(", ", Catalogue.Where(c => Get(c.Action) == mode).Select(c => c.Label.ToLowerInvariant()));
        var auto = List(AutonomyMode.Auto);
        var ask = List(AutonomyMode.Ask);
        var off = List(AutonomyMode.Off);
        var parts = new List<string>();
        if (auto.Length > 0) parts.Add($"On my own, and I'll tell you each time: {auto}.");
        if (ask.Length > 0) parts.Add($"I'll ask first before I {ask}.");
        if (off.Length > 0) parts.Add($"Switched off: {off}.");
        parts.Add("I never move meetings with other people.");
        return string.Join(" ", parts);
    }
}
