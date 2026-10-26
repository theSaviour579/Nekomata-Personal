namespace Nekomata.Core.Guardian;

/// <summary>A named routine: a few of Guardian's existing commands run in order ("end of day protocol").</summary>
public sealed record Protocol(string Name, string Description, IReadOnlyList<string> Steps, IReadOnlyList<string> Aliases, bool BuiltIn = true);

/// <summary>
/// Protocols: named routines the user triggers by voice. Each step is an ordinary Guardian command, so a protocol can
/// never do more than those commands already do; anything consequential still waits for "do it" (a protocol may not
/// contain "do it" itself). The user can add their own: "create protocol lunch: plan my day; anything I need to worry
/// about". A step may carry {detail}, filled from "my protocol: the detail".
/// </summary>
public static class Protocols
{
    public static readonly IReadOnlyList<Protocol> BuiltIn =
    [
        new("morning", "Systems check, the morning brief, and anything that needs you",
            ["systems check", "morning brief", "anything I need to worry about"], ["start of day", "good morning", "wake up", "daybreak"]),
        new("end of day", "Wrap up the day, today's promises, and anything still needing you",
            ["wrap up my day", "what did I promise people today", "anything I need to worry about"], ["evening", "close down", "shutdown", "shut down", "home time"]),
        new("monday", "The week ahead: what's due and the scorecard",
            ["what's due", "how is guardian doing"], ["week ahead", "start of week"]),
        new("friday", "Week in review, the scorecard, and anything to clear before the weekend",
            ["week in review", "how is guardian doing", "anything I need to worry about"], ["end of week", "weekend"]),
        new("focus", "Focus music on and quiet for an hour: unprompted lines wait, urgent ones still get through",
            ["play focus music", "quiet for an hour"], ["deep work", "heads down", "concentration"]),
    ];

    public static Protocol? Find(string name, IEnumerable<Protocol> custom)
    {
        var key = Normalise(name);
        return custom.Concat(BuiltIn).FirstOrDefault(p => Normalise(p.Name) == key || p.Aliases.Any(a => Normalise(a) == key));
    }

    /// <summary>The commands to run, with {detail} filled in; null when the protocol needs a detail that wasn't given.</summary>
    public static IReadOnlyList<string>? StepsFor(Protocol protocol, string detail)
    {
        if (protocol.Steps.Any(s => s.Contains("{detail}")) && string.IsNullOrWhiteSpace(detail)) return null;
        return protocol.Steps.Select(s => s.Replace("{detail}", detail.Trim()).Trim()).Where(s => s.Length > 0).ToList();
    }

    /// <summary>Splits "plan my day; anything I need to worry about" into steps.</summary>
    public static IReadOnlyList<string> ParseSteps(string steps) =>
        steps.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => s.Length > 0).ToList();

    public static string Normalise(string name) =>
        string.Join(' ', name.Trim().TrimEnd('.', '!', '?').ToLowerInvariant().Replace("the ", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
