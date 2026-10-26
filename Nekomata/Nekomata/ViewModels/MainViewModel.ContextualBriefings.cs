using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Briefing;
using Nekomata.Models.Guardian;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private GuardianContextBriefing emailContextBriefing = new() { Area = "Inbox" };
    [ObservableProperty] private GuardianContextBriefing calendarContextBriefing = new() { Area = "Calendar" };

    private void RefreshContextualBriefings()
    {
        var service = _services.GetRequiredService<GuardianContextBriefingService>();
        EmailContextBriefing = service.Build(new()
        {
            Area = "Inbox", IsLoaded = EmailLoaded, IsBusy = EmailBusy, LastRefreshed = EmailLastRefreshed,
            Signals = EmailItems.Where(x => !x.IsFiltered).Select(x => new GuardianContextSignal
            {
                Title = $"{x.Message.SenderName}: {x.Message.Subject}", Evidence = x.Reason,
                RecommendedAction = "OPEN PRIORITY EMAIL", Significance = x.PriorityScore,
                IsActionable = x.IsManagement || x.ReplyRecommended || x.PriorityScore >= 70,
                ChangedAt = x.Message.ReceivedAt, ContextId = x.Message.Id
            }).ToList()
        });

        var now = DateTimeOffset.Now;
        CalendarContextBriefing = service.Build(new()
        {
            Area = "Calendar", IsLoaded = CalendarLoaded, IsBusy = CalendarBusy,
            Signals = CalendarEvents.Where(x => !x.IsAllDay && x.End > now).Select(x =>
            {
                var minutes = (x.Start - now).TotalMinutes;
                var active = x.Start <= now && x.End > now;
                return new GuardianContextSignal
                {
                    Title = active ? $"In progress: {x.Subject}" : $"Next: {x.Subject} at {x.Start:HH:mm}",
                    Evidence = active ? $"This commitment runs until {x.End:HH:mm}." : $"Starts in {Math.Max(0, (int)minutes)} minutes; {x.Attendees.Count} attendee{(x.Attendees.Count == 1 ? "" : "s")}.",
                    RecommendedAction = "OPEN MEETING CONTEXT", Significance = active ? 90 : minutes <= 60 ? 72 : minutes <= 120 ? 45 : 25,
                    IsActionable = active || minutes <= 120, ChangedAt = x.Start, ContextId = x.Id
                };
            }).ToList()
        });
    }

    [RelayCommand]
    private void OpenEmailContextAction()
    {
        var item = EmailItems.FirstOrDefault(x => x.Message.Id == EmailContextBriefing.ContextId);
        if (item is null) return;
        RecordContextBriefingAction(EmailContextBriefing);
        OpenEmail(item);
    }

    [RelayCommand]
    private void OpenCalendarContextAction()
    {
        var item = CalendarEvents.FirstOrDefault(x => x.Id == CalendarContextBriefing.ContextId);
        if (item is null) return;
        RecordContextBriefingAction(CalendarContextBriefing);
        OpenCalendarThreadContext(item);
    }

    private void RecordContextBriefingAction(GuardianContextBriefing briefing) => _ = RecordWorkdayEventAsync(new WorkdayEvent
    {
        EventType = "ContextBriefingActioned", Category = briefing.Area, Title = briefing.Headline,
        Narrative = $"Guardian recommended: {briefing.NextAction}. Evidence: {briefing.Evidence}",
        ExternalId = briefing.ContextId, Recommendation = briefing.NextAction, Outcome = "Opened",
        Significance = 50
    });
}
