using Nekomata.Core.Integrations;
using Nekomata.Integrations.MicrosoftGraph.Context;
using Xunit;

namespace Nekomata.Tests;

public sealed class TeamsSignalsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(1));

    private static GraphChatMessage Msg(string id, string chat, string sender, string body, int minutesAgo,
        bool mine = false, string type = "group", string topic = "", string importance = "normal", bool mention = false) => new()
    {
        Id = id, ChatId = chat, Sender = sender, Body = body, CreatedAt = Now.AddMinutes(-minutesAgo), IsFromCurrentUser = mine,
        ChatType = type, ChatTopic = topic, Importance = importance, MentionsCurrentUser = mention
    };

    [Theory]
    [InlineData("Phil Jones", "Phil", true)]
    [InlineData("Phil Jones", "phil jones", true)]
    [InlineData("Phillip Jones", "Phil", false)]
    [InlineData("Sam Phil", "Phil", false)]
    [InlineData("Phil Jones", "Phil Smith", false)]
    public void Matches_manager_names_by_full_or_first_name(string sender, string listed, bool expected) =>
        Assert.Equal(expected, TeamsSignals.IsManager(sender, [listed]));

    [Fact]
    public void Someone_the_user_said_is_not_a_manager_is_never_one() =>
        Assert.False(TeamsSignals.IsManager("Phil Jones", ["Phil"], ["Phil Jones"]));

    [Theory]
    [InlineData("thanks", true)]
    [InlineData("Ok 👍", true)]
    [InlineData("cheers mate!", true)]
    [InlineData("", true)]
    [InlineData("yes", false)]
    [InlineData("Can you look at the printer on floor 2?", false)]
    public void Recognises_bare_acknowledgements(string body, bool noise) => Assert.Equal(noise, TeamsSignals.IsNoise(body));

    [Fact]
    public void Groups_new_inbound_messages_by_chat_with_context()
    {
        var all = new List<GraphChatMessage>
        {
            Msg("1", "a", "Sam Lee", "Is the VPN down?", 30, type: "oneOnOne"),
            Msg("2", "a", "Alex Morgan", "Checking now", 25, mine: true, type: "oneOnOne"),
            Msg("3", "a", "Sam Lee", "Still can't connect", 5, type: "oneOnOne"),
            Msg("4", "b", "Kev Hart", "thanks", 3, topic: "Ops")
        };
        var groups = TeamsSignals.GroupNew(all, new HashSet<string> { "a:1" });
        var group = Assert.Single(groups);
        Assert.Equal("Sam Lee", group.ChatName);
        Assert.Equal("3", Assert.Single(group.NewMessages).Id);
        Assert.Equal(2, group.Context.Count);
    }

    [Fact]
    public void Manager_message_alerts_until_the_user_replies()
    {
        var all = new List<GraphChatMessage> { Msg("1", "a", "Phil Jones", "Can we talk about the laptop budget?", 5, type: "oneOnOne") };
        var group = TeamsSignals.GroupNew(all, new HashSet<string>())[0];
        Assert.Equal(new[] { TeamsAlertKind.Manager }, TeamsSignals.AlertsFor(group, all, ["Phil"], [], Now, TimeSpan.FromHours(2)));

        all.Add(Msg("2", "a", "Alex Morgan", "Sure, 2pm?", 1, mine: true, type: "oneOnOne"));
        Assert.Empty(TeamsSignals.AlertsFor(group, all, ["Phil"], [], Now, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Urgent_and_mentions_alert_but_old_messages_do_not()
    {
        var all = new List<GraphChatMessage>
        {
            Msg("1", "g", "Kev Hart", "@Alex the firewall is dropping traffic", 10, topic: "Ops", importance: "urgent", mention: true),
            Msg("2", "h", "Sam Lee", "@Alex when you get a sec", 300, topic: "Dev", mention: true)
        };
        var groups = TeamsSignals.GroupNew(all, new HashSet<string>());
        var ops = groups.Single(g => g.ChatId == "g");
        Assert.Equal(new[] { TeamsAlertKind.Urgent, TeamsAlertKind.Mention }, TeamsSignals.AlertsFor(ops, all, [], [], Now, TimeSpan.FromHours(2)));
        var dev = groups.Single(g => g.ChatId == "h");
        Assert.Empty(TeamsSignals.AlertsFor(dev, all, [], [], Now, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Names_unnamed_group_chats_by_who_is_in_them()
    {
        var all = new List<GraphChatMessage> { Msg("1", "g", "Kev Hart", "hi", 5), Msg("2", "g", "Sam Lee", "x", 4), Msg("3", "g", "Alex Morgan", "y", 3, mine: true) };
        Assert.Equal("Kev, Sam", TeamsSignals.ChatName(all));
    }
}
