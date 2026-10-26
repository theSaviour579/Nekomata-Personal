using System.Text.RegularExpressions;
using Nekomata.Integrations.MicrosoftGraph.Context;

namespace Nekomata.Core.Integrations;

public enum TeamsAlertKind { Manager, Urgent, Mention }

public sealed record TeamsChatGroup(string ChatId, string ChatName, string ChatType, string ChatUrl,
    IReadOnlyList<GraphChatMessage> NewMessages, IReadOnlyList<GraphChatMessage> Context)
{
    public GraphChatMessage Latest => NewMessages[^1];
    public bool IsOneToOne => ChatType.Equals("oneOnOne", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The rules behind Guardian's Teams watch, kept free of UI and Graph calls so they can be tested: who counts as a
/// manager, which new messages deserve an alert rather than a line on Home, whether the user has already answered, and
/// which messages are just acknowledgements.
/// </summary>
public static partial class TeamsSignals
{
    /// <summary>
    /// A sender is a manager when their display name matches a listed name: the full name, or a listed first name that
    /// is the first word of the sender's name ("Phil" matches "Phil Jones"). Names the user has said aren't managers win.
    /// </summary>
    public static bool IsManager(string sender, IEnumerable<string> managers, IEnumerable<string>? notManagers = null)
    {
        if (string.IsNullOrWhiteSpace(sender)) return false;
        if (notManagers is not null && notManagers.Any(n => NameMatches(sender, n))) return false;
        return managers.Any(m => NameMatches(sender, m));
    }

    public static bool NameMatches(string sender, string name)
    {
        sender = sender.Trim(); name = name.Trim();
        if (sender.Length == 0 || name.Length == 0) return false;
        if (sender.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        var senderWords = sender.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nameWords = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // "Phil" matches "Phil Jones"; "Phil Jones" matches "Phil Jones (IT)".
        return nameWords.Length <= senderWords.Length &&
               nameWords.Select((w, i) => senderWords[i].Equals(w, StringComparison.OrdinalIgnoreCase)).All(x => x);
    }

    /// <summary>"thanks", "ok 👍", "cheers" and the like: nothing to summarise or alert on.</summary>
    public static bool IsNoise(string body)
    {
        var text = NonWordRegex().Replace(body ?? "", " ").Trim().ToLowerInvariant();
        if (text.Length == 0) return true;
        if (text.Length > 40) return false;
        return AcknowledgementRegex().IsMatch(text);
    }

    public static bool IsUrgent(GraphChatMessage message) =>
        message.Importance.Equals("urgent", StringComparison.OrdinalIgnoreCase) ||
        message.Importance.Equals("high", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the user has written in the chat after <paramref name="at"/>.</summary>
    public static bool AnsweredAfter(IEnumerable<GraphChatMessage> messages, string chatId, DateTimeOffset at) =>
        messages.Any(m => m.IsFromCurrentUser && m.ChatId == chatId && m.CreatedAt > at);

    /// <summary>Who a chat is with, for showing: its topic, the other person in a one-to-one, or the people in it.</summary>
    public static string ChatName(IReadOnlyList<GraphChatMessage> messages)
    {
        var topic = messages.Select(m => m.ChatTopic).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        if (topic is not null) return topic.Trim();
        var others = messages.Where(m => !m.IsFromCurrentUser && m.Sender.Length > 0).Select(m => m.Sender).Distinct().ToList();
        if (others.Count == 0) return "a chat";
        var oneToOne = messages.Any(m => m.ChatType.Equals("oneOnOne", StringComparison.OrdinalIgnoreCase));
        if (oneToOne || others.Count == 1) return others[0];
        return string.Join(", ", others.Take(3).Select(FirstName)) + (others.Count > 3 ? " and others" : "");
    }

    public static string FirstName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;

    /// <summary>
    /// New inbound messages, grouped by chat, each with the conversation just before them for context. Messages
    /// already seen, the user's own messages and bare acknowledgements are left out of the new set.
    /// </summary>
    public static IReadOnlyList<TeamsChatGroup> GroupNew(IReadOnlyList<GraphChatMessage> all, ISet<string> seen, int contextSize = 6)
    {
        var groups = new List<TeamsChatGroup>();
        foreach (var chat in all.GroupBy(m => m.ChatId))
        {
            var ordered = chat.OrderBy(m => m.CreatedAt).ToList();
            var fresh = ordered.Where(m => !m.IsFromCurrentUser && !seen.Contains(Key(m)) && !IsNoise(m.Body)).ToList();
            if (fresh.Count == 0) continue;
            var firstFresh = fresh[0].CreatedAt;
            var context = ordered.Where(m => m.CreatedAt < firstFresh).TakeLast(contextSize).ToList();
            var sample = ordered[^1];
            groups.Add(new TeamsChatGroup(chat.Key, ChatName(ordered), sample.ChatType, ordered.Select(m => m.ChatWebUrl).FirstOrDefault(u => u.Length > 0) ?? "",
                fresh, context));
        }
        return groups.OrderBy(g => g.Latest.CreatedAt).ToList();
    }

    public static string Key(GraphChatMessage message) => $"{message.ChatId}:{message.Id}";

    /// <summary>
    /// Which alerts a chat's new messages earn, strongest first: a manager writing; a message marked urgent or
    /// important; the user @mentioned in a group chat. Nothing once the user has replied, and nothing for messages older
    /// than <paramref name="freshFor"/> (so a restart doesn't replay the morning).
    /// </summary>
    public static IReadOnlyList<TeamsAlertKind> AlertsFor(TeamsChatGroup group, IReadOnlyList<GraphChatMessage> all,
        IEnumerable<string> managers, IEnumerable<string> notManagers, DateTimeOffset now, TimeSpan freshFor)
    {
        var recent = group.NewMessages.Where(m => now - m.CreatedAt <= freshFor).ToList();
        if (recent.Count == 0 || AnsweredAfter(all, group.ChatId, recent[^1].CreatedAt)) return [];
        var managerList = managers.ToList(); var notList = notManagers.ToList();
        var kinds = new List<TeamsAlertKind>();
        if (recent.Any(m => IsManager(m.Sender, managerList, notList))) kinds.Add(TeamsAlertKind.Manager);
        if (recent.Any(IsUrgent)) kinds.Add(TeamsAlertKind.Urgent);
        if (!group.IsOneToOne && recent.Any(m => m.MentionsCurrentUser)) kinds.Add(TeamsAlertKind.Mention);
        return kinds;
    }

    [GeneratedRegex(@"[^\p{L}\p{N}' ]+")]
    private static partial Regex NonWordRegex();
    [GeneratedRegex(@"^(?:(?:ok|okay|k|kk|thanks|thank you|thank u|thx|ta|cheers|nice one|nice|great|cool|perfect|lovely|brilliant|no worries|np|no problem|will do|got it|noted|sounds good|lol|haha|morning|good morning|hi|hello|hey|night|bye)\s*)+(?:\s*(?:david|dave|mate|all|guys))?$")]
    private static partial Regex AcknowledgementRegex();
}
