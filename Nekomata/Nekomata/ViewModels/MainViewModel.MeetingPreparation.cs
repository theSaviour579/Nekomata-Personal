using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private bool _meetingPreparationBusy;
    private Window? _meetingPreparationWindow;

    private async Task CheckMeetingPreparationAsync()
    {
        if (_meetingPreparationBusy || !CalendarLoaded || CalendarBusy) return;
        _meetingPreparationBusy = true;
        try
        {
            var now = DateTimeOffset.Now;
            var settings = _services.GetRequiredService<WorkingDaySettings>();
            if (now.LocalDateTime < settings.GetStart(now.LocalDateTime) || now.LocalDateTime >= settings.GetEnd(now.LocalDateTime) ||
                now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return;
            var meetings = CalendarEvents.Where(x => MeetingPreparationPolicy.IsDue(x, now)).OrderBy(x => x.Start).ToList();
            foreach (var old in AttentionItems.Where(x => x.ActionKind == "meeting_preparation" && x.Status != "Resolved").ToList())
                if (!meetings.Any(x => MeetingPreparationPolicy.Key(x) == old.Key)) ResolveAttention(old.Key);
            foreach (var meeting in meetings)
            {
                var key = MeetingPreparationPolicy.Key(meeting);
                if (_resolvedAttentionKeys.ContainsKey(key)) continue;
                var existing = AttentionItems.FirstOrDefault(x => x.Key == key);
                if (existing == null)
                {
                    var prepared = Workspace.AnticipatedActions.FirstOrDefault(x => x.TriggerKey == meeting.Id && x.ProposedAction == "PrepareMeetingContext" && x.ExpiresAt > now);
                    var lines = new List<string> { $"{meeting.Subject}\n{meeting.Start.LocalDateTime:ddd HH:mm}–{meeting.End.LocalDateTime:HH:mm}",
                        "Participants: " + string.Join(", ", meeting.Attendees),
                        string.IsNullOrWhiteSpace(prepared?.PreparedContent) ? "No established work-thread context is available. Possible matches are labelled below." : "EXISTING PREPARATION · VERIFY AGAINST SOURCES\n" + prepared.PreparedContent };
                    var brief = await GatherMeetingBriefAsync(meeting);
                    lines.AddRange(brief.Lines);
                    if (meeting.Start <= DateTimeOffset.Now || !CalendarEvents.Any(x => MeetingPreparationPolicy.Key(x) == key)) continue;
                    RaiseAttention(key, "Meeting preparation", "Info", $"Prepare for {meeting.Subject}", string.Join("\n\n", lines), "meeting_preparation", meeting.Id, expiresAt: meeting.Start);
                    existing = AttentionItems.FirstOrDefault(x => x.Key == key);
                    if (existing != null) { existing.MeetingSources = brief.Sources; SaveAttentionItems(); }
                }
                if (existing is { IsActive: true, MeetingPromptShown: false } && _meetingPreparationWindow == null && _preflightWindow == null && !DayReviewVisible && !MissionActive &&
                    !CalendarEvents.Any(x => x.Start <= DateTimeOffset.Now && x.End > DateTimeOffset.Now))
                    // With the home screen in front, the meeting card there is the brief; the window would cover it.
                    if (CanPresentRoutinePrompt(GuardianPromptKind.MeetingBrief) &&
                        !(ShowModernHome && Application.Current.MainWindow is { IsActive: true, WindowState: not WindowState.Minimized }))
                        ShowMeetingPreparation(existing);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Meeting preparation failed: " + ex); }
        finally { _meetingPreparationBusy = false; }
    }

    private void ShowMeetingPreparation(GuardianAttentionItem item)
    {
        if (item.ExpiresAt <= DateTimeOffset.Now) { ResolveAttention(item.Key); return; }
        if (_meetingPreparationWindow != null) { _meetingPreparationWindow.Activate(); return; }
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "YOUR MEETING BRIEF IS READY", FontSize = 24, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = item.Detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,16,0,16) });
        foreach (var source in item.MeetingSources)
        {
            var card = new Expander { Header = new TextBlock { Text = source.Title, TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0,0,0,10),
                Content = new TextBox { Text = source.Content, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(8) } };
            card.SetResourceReference(Control.ForegroundProperty, "NekoTextBrush");
            panel.Children.Add(card);
        }
        var actions = new WrapPanel();
        panel.Children.Add(actions);
        var window = new Window { Title = item.Title, Width = 800, Height = 700, ShowActivated = false,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        window.SetResourceReference(Control.BackgroundProperty, "NekoBackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "NekoTextBrush");
        foreach (var label in new[] { "READY", "SNOOZE 5 MIN", "NOT RELEVANT", "OPEN CALENDAR", "OPEN EMAIL" })
        {
            var button = new Button { Content = label, Margin = new Thickness(0,0,8,8) };
            actions.Children.Add(button);
            button.Click += async (_, _) =>
            {
                try
                {
                    if (label == "OPEN CALENDAR") { await ShowCalendarAsync(); window.Close(); Application.Current.MainWindow?.Activate(); return; }
                    if (label == "OPEN EMAIL") { await ShowEmailAsync(); window.Close(); Application.Current.MainWindow?.Activate(); return; }
                    if (label == "SNOOZE 5 MIN")
                    { item.Status = "Snoozed"; item.SnoozedUntil = DateTimeOffset.Now.AddMinutes(5); item.MeetingPromptShown = false; SaveAttentionItems(); RaiseAttentionSummaryChanged(); }
                    else { item.Detail += "\nUser decision: " + label; ResolveAttentionItem(item); }
                    window.Close();
                }
                catch (Exception ex) { MessageBox.Show("Meeting action failed: " + ex.Message); }
            };
        }
        _meetingPreparationWindow = window;
        window.Closed += (_, _) => { _meetingPreparationWindow = null; RoutinePromptClosed(); };
        item.MeetingPromptShown = true;
        SaveAttentionItems();
        window.Show();
    }
}
