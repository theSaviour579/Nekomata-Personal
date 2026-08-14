using System.Text.RegularExpressions;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Analytics.Capacity;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Models.Planning;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Workspace;
using System.Windows;
using System.Windows.Threading;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private const string ManagementAttentionCategory = "Nekomata - Attention";
    private readonly HashSet<string> _managementSenders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _alertedManagementMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _emailMonitorTimer = new() { Interval = TimeSpan.FromMinutes(2) };
    private bool _managementSendersLoaded;
    private bool _emailMonitorStarted;

    [ObservableProperty] private ObservableCollection<EmailTriageItem> emailItems = [];
    [ObservableProperty] private bool emailBusy;
    [ObservableProperty] private bool emailLoaded;
    [ObservableProperty] private string emailStatus = "Open Email to connect and triage your unread inbox.";
    [ObservableProperty] private DateTimeOffset? emailLastRefreshed;

    public int UnreadEmailCount => EmailItems.Count;
    public int PriorityEmailCount => EmailItems.Count(item => !item.IsFiltered && item.PriorityScore >= 70);
    public int FilteredEmailCount => EmailItems.Count(item => item.IsFiltered);
    public int ManagementEmailCount => EmailItems.Count(item => item.IsManagement);
    public bool HasEmailItems => EmailItems.Count > 0;
    public bool ShowEmailEmptyState => EmailLoaded && !EmailBusy && !HasEmailItems;
    public string EmailLastRefreshedLabel => EmailLastRefreshed is null
        ? "Not refreshed yet"
        : $"Last checked {EmailLastRefreshed.Value.LocalDateTime:HH:mm}";

    [RelayCommand]
    private async Task ShowEmailAsync()
    {
        WorkspaceMode = WorkspaceMode.Email;
        if (!EmailLoaded)
            await RefreshEmailAsync();
    }

    [RelayCommand]
    private async Task RefreshEmailAsync()
    {
        if (EmailBusy) return;
        EmailBusy = true;
        EmailStatus = "Connecting to Outlook and reading unread mail...";
        try
        {
            EnsureManagementSendersLoaded();
            var service = _services.GetRequiredService<IEmailService>();
            var existingState = EmailItems.ToDictionary(item => item.Message.Id, StringComparer.OrdinalIgnoreCase);
            var messages = await service.GetUnreadInboxAsync(40);
            var triaged = messages.Select(message => new EmailTriageItem
            {
                Message = message,
                Classification = ClassifyEmail(message),
                PriorityScore = ScoreEmail(message),
                IsManagement = _managementSenders.Contains(message.SenderAddress)
            }).ToList();

            foreach (var item in triaged)
            {
                ApplyLocalExplanation(item);
                if (item.MeetingDateOptions.Count == 0)
                    PopulateLocalMeetingDates(item, item.DisplayContent);
                if (existingState.TryGetValue(item.Message.Id, out var existing))
                {
                    item.DraftText = existing.DraftText;
                    item.DraftStatus = existing.DraftStatus;
                    item.OutlookDraftId = existing.OutlookDraftId;
                    item.OutlookDraftWebLink = existing.OutlookDraftWebLink;
                    item.ReplySent = existing.ReplySent;
                    item.FullContent = existing.FullContent;
                    item.IsBodyExpanded = existing.IsBodyExpanded;
                    item.SelectedMeetingDate = existing.SelectedMeetingDate;
                    item.RequestedMeetingMinutes = existing.RequestedMeetingMinutes;
                    item.MeetingSuggestions = existing.MeetingSuggestions;
                    item.MeetingDateOptions = existing.MeetingDateOptions;
                    item.MeetingTimePreference = existing.MeetingTimePreference;
                }
                if (!item.IsManagement) continue;

                PromoteManagementEmail(item);
                try
                {
                    item.FullContent = await service.GetMessageContentAsync(item.Message.Id);
                PopulateLocalMeetingDates(item, item.FullContent);
                    if (!item.Message.Categories.Contains(ManagementAttentionCategory, StringComparer.OrdinalIgnoreCase))
                        await service.ApplyCategoryAsync(item.Message.Id, item.Message.Categories, ManagementAttentionCategory);
                    item.CategoryApplied = true;
                }
                catch (Exception ex)
                {
                    item.DraftStatus = "Management alert detected, but Outlook attention marking failed: " + ex.Message;
                }
            }

            EmailItems = new ObservableCollection<EmailTriageItem>(triaged
                .OrderByDescending(item => item.IsManagement)
                .ThenBy(item => item.IsFiltered)
                .ThenByDescending(item => item.PriorityScore)
                .ThenByDescending(item => item.Message.ReceivedAt));
            EmailLoaded = true;
            EmailLastRefreshed = DateTimeOffset.Now;
            RaiseEmailSummaryChanged();
            EmailStatus = messages.Count == 0
                ? "Inbox clear — there are no unread messages to triage."
                : $"Local triage complete. {ManagementEmailCount} management; {PriorityEmailCount} prioritised; {FilteredEmailCount} filtered.";
            StartEmailMonitoring();
            AlertForNewManagementMessages(triaged);
        }
        catch (Exception ex)
        {
            EmailStatus = "Email refresh failed: " + ex.Message;
        }
        finally
        {
            EmailBusy = false;
            OnPropertyChanged(nameof(ShowEmailEmptyState));
        }
    }

    [RelayCommand]
    private void OpenEmail(EmailTriageItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Message.WebLink)) return;
        Process.Start(new ProcessStartInfo(item.Message.WebLink) { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task ToggleManagementSenderAsync(EmailTriageItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Message.SenderAddress)) return;
        EnsureManagementSendersLoaded();

        if (item.IsManagement)
        {
            _managementSenders.Remove(item.Message.SenderAddress);
            item.IsManagement = false;
            item.Classification = ClassifyEmail(item.Message);
            item.PriorityScore = ScoreEmail(item.Message);
            ApplyLocalExplanation(item);
            item.DraftStatus = "Sender removed from the local management list.";
        }
        else
        {
_managementSenders.Add(item.Message.SenderAddress);
            item.IsManagement = true;
            PromoteManagementEmail(item);
            try
            {
                var service = _services.GetRequiredService<IEmailService>();
                item.FullContent = await service.GetMessageContentAsync(item.Message.Id);
                PopulateLocalMeetingDates(item, item.FullContent);
                await service.ApplyCategoryAsync(item.Message.Id, item.Message.Categories, ManagementAttentionCategory);
                item.CategoryApplied = true;
                item.DraftStatus = "Sender saved as management and this message was marked for attention.";
            }
            catch (Exception ex)
            {
                item.DraftStatus = "Sender saved, but Outlook attention marking failed: " + ex.Message;
            }
            _alertedManagementMessages.Add(item.Message.Id);
        }

        SaveManagementSenders();
        RaiseEmailSummaryChanged();
    }

    [RelayCommand]
    private async Task ToggleEmailBodyAsync(EmailTriageItem? item)
    {
        if (item is null) return;
        if (item.IsBodyExpanded)
        {
            item.IsBodyExpanded = false;
            return;
        }

        item.DraftStatus = "Loading the full message from Outlook...";
        try
        {
            if (string.IsNullOrWhiteSpace(item.FullContent))
            {
                var service = _services.GetRequiredService<IEmailService>();
                item.FullContent = await service.GetMessageContentAsync(item.Message.Id);
                PopulateLocalMeetingDates(item, item.FullContent);
            }
            item.IsBodyExpanded = true;
            item.DraftStatus = "Full message loaded.";
        }
        catch (Exception ex)
        {
            item.DraftStatus = "Could not load the full message: " + ex.Message;
        }
    }

    [RelayCommand]
    private void AddMeetingDate(EmailTriageItem? item)
    {
        if (item?.SelectedMeetingDate is not DateTime date) return;
        AddMeetingDateOption(item, date.Date);
    }

    [RelayCommand]
    private async Task FindMeetingTimesAsync(EmailTriageItem? item)
    {
        if (item is null) return;
        if (string.IsNullOrWhiteSpace(item.FullContent))
        {
            try
            {
                item.FullContent = await _services.GetRequiredService<IEmailService>().GetMessageContentAsync(item.Message.Id);
                PopulateLocalMeetingDates(item, item.FullContent);
            }
            catch (Exception ex)
            {
                item.DraftStatus = "Full message could not be loaded; checking dates visible in the preview. " + ex.Message;
            }
        }
        var dates = item.MeetingDateOptions.Where(option => option.IsSelected).Select(option => option.Date).ToList();
        if (dates.Count == 0 && item.SelectedMeetingDate is DateTime manualDate)
        {
            AddMeetingDateOption(item, manualDate.Date);
            dates.Add(manualDate.Date);
        }
        await FindMeetingTimesForDatesAsync(item, dates);
    }

    private async Task PrepareMeetingAvailabilityFromContentAsync(EmailTriageItem item)
    {
        var content = $"{item.Message.Subject} {item.DisplayContent}";
        if (!ContainsAny(content.ToLowerInvariant(), "meeting", "meet", "call", "catch up", "catch-up", "availability", "available", "schedule", "diary"))
            return;

        PopulateLocalMeetingDates(item, content);
        try
        {
            var prompt = $$"""
                Extract meeting scheduling details from this email. Today is {{DateTime.Today:yyyy-MM-dd}}.
                Resolve relative dates such as tomorrow, next Tuesday, or the 18th into exact future dates.
                Include every date the requester proposes, not just the first. Do not invent dates.
                Infer duration only if stated or strongly implied; otherwise use 30 minutes.
                preference must be one of Any, Morning, Afternoon, Earliest, or Latest.
                Return JSON only:
                {"dates":["yyyy-MM-dd"],"durationMinutes":30,"preference":"Any","notes":"short constraint summary"}

                Subject: {{item.Message.Subject}}
                Message: {{item.DisplayContent}}
                """;
            var extracted = await _aiProvider.AskJsonAsync<MeetingRequestExtraction>(prompt);
            if (extracted is not null)
            {
                foreach (var value in extracted.Dates)
                {
                    if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        AddMeetingDateOption(item, date);
                }
                if (extracted.DurationMinutes is >= 15 and <= 180)
                    item.RequestedMeetingMinutes = extracted.DurationMinutes;
                item.MeetingTimePreference = NormaliseMeetingPreference(extracted.Preference);
            }
        }
        catch
        {
            // Local date detection still supplies availability when structured AI extraction is unavailable.
        }

        var dates = item.MeetingDateOptions.Where(option => option.IsSelected).Select(option => option.Date).ToList();
        if (dates.Count > 0)
            await FindMeetingTimesForDatesAsync(item, dates);
    }

    private async Task FindMeetingTimesForDatesAsync(EmailTriageItem item, IReadOnlyCollection<DateTime> requestedDates)
    {
        var dates = requestedDates.Select(date => date.Date).Where(date => date >= DateTime.Today).Distinct().OrderBy(date => date).ToList();
        if (dates.Count == 0)
        {
            item.MeetingSuggestions = "No future requested date was detected. Choose a date manually.";
            return;
        }

        var duration = Math.Clamp(item.RequestedMeetingMinutes, 15, 180);
        item.RequestedMeetingMinutes = duration;
        item.DraftStatus = $"Checking Outlook across {dates.Count} requested date{(dates.Count == 1 ? "" : "s")}...";
        try
        {
            var settings = _services.GetRequiredService<WorkingDaySettings>();
            var calendar = _services.GetRequiredService<ICalendarService>();
            var ranked = new List<(DateTimeOffset Start, DateTimeOffset End, double Score)>();
            foreach (var day in dates)
            {
                var offset = TimeZoneInfo.Local.GetUtcOffset(day);
                var workStart = new DateTimeOffset(settings.GetStart(day), offset);
                var workEnd = new DateTimeOffset(settings.GetEnd(day), offset);
                var dayStart = new DateTimeOffset(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), offset);
                var events = await calendar.GetEventsAsync(dayStart, dayStart.AddDays(1));
                var rawBusy = events.Select(calendarEvent => calendarEvent.IsAllDay
                        ? (Start: workStart, End: workEnd)
                        : (calendarEvent.Start, calendarEvent.End))
                    .ToList();
                if (settings.IncludeLunchBreak)
                    rawBusy.Add((new DateTimeOffset(settings.GetLunchStart(day), offset), new DateTimeOffset(settings.GetLunchEnd(day), offset)));
                var busy = CalendarCapacityIntervalCalculator.Calculate(rawBusy, workStart, workEnd);
                var earliest = day == DateTime.Today && DateTimeOffset.Now > workStart
                    ? RoundUpToQuarterHour(DateTimeOffset.Now)
                    : workStart;
                ranked.AddRange(BuildMeetingCandidates(busy, earliest, workEnd, duration)
                    .Select(slot => (slot.Start, slot.End, ScoreMeetingCandidate(slot.Start, slot.End, busy, item.MeetingTimePreference))));
            }

            var ordered = ranked.OrderByDescending(slot => slot.Score).ThenBy(slot => slot.Start).ToList();
            var selected = new List<(DateTimeOffset Start, DateTimeOffset End, double Score)>();
            foreach (var date in dates)
            {
                var bestForDate = ordered.FirstOrDefault(slot => slot.Start.Date == date);
                if (bestForDate != default) selected.Add(bestForDate);
            }
            foreach (var candidate in ordered)
            {
                if (selected.Any(slot => slot.Start == candidate.Start)) continue;
                if (selected.Any(slot => slot.Start.Date == candidate.Start.Date && Math.Abs((slot.Start - candidate.Start).TotalMinutes) < 30)) continue;
                selected.Add(candidate);
                if (selected.Count == Math.Min(5, Math.Max(3, dates.Count + 1))) break;
            }
            selected = selected.OrderByDescending(slot => slot.Score).ToList();

            item.MeetingSuggestions = selected.Count == 0
                ? $"No {duration}-minute working-hours slot is free across {string.Join(", ", dates.Select(date => date.ToString("ddd d MMM")))}."
                : $"Recommended: {FormatMeetingSlot(selected[0])}. " +
                  (selected.Count > 1 ? $"Contingencies: {string.Join("; ", selected.Skip(1).Select(FormatMeetingSlot))}." : "");
            item.DraftStatus = "Requested dates checked. Guardian will use the recommended time and contingencies in the reply.";
        }
        catch (Exception ex)
        {
            item.MeetingSuggestions = "Could not check calendar availability: " + ex.Message;
            item.DraftStatus = item.MeetingSuggestions;
        }
    }

    private static string FormatMeetingSlot((DateTimeOffset Start, DateTimeOffset End, double Score) slot) =>
        $"{slot.Start:ddd d MMM} {slot.Start:HH:mm}-{slot.End:HH:mm}";

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> BuildMeetingCandidates(
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> busy,
        DateTimeOffset earliest,
        DateTimeOffset workEnd,
        int durationMinutes)
    {
        var cursor = earliest;
        foreach (var interval in busy.Where(interval => interval.End > earliest))
        {
            var freeEnd = interval.Start < workEnd ? interval.Start : workEnd;
            for (var start = cursor; start.AddMinutes(durationMinutes) <= freeEnd; start = start.AddMinutes(15))
                yield return (start, start.AddMinutes(durationMinutes));
            if (interval.End > cursor) cursor = interval.End;
            if (cursor >= workEnd) yield break;
        }
        for (var start = cursor; start.AddMinutes(durationMinutes) <= workEnd; start = start.AddMinutes(15))
            yield return (start, start.AddMinutes(durationMinutes));
    }

    private static double ScoreMeetingCandidate(
        DateTimeOffset start,
        DateTimeOffset end,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> busy,
        string preference)
    {
        var hour = start.TimeOfDay.TotalHours;
        var preferredDistance = Math.Min(Math.Abs(hour - 10.5), Math.Abs(hour - 14.5));
        var score = 100 - preferredDistance * 12;
        score += preference switch
        {
            "Morning" => hour < 12 ? 35 : -35,
            "Afternoon" => hour >= 13 ? 35 : -35,
            "Earliest" => -hour * 4,
            "Latest" => hour * 4,
            _ => 0
        };
        if (busy.Any(interval => Math.Abs((interval.End - start).TotalMinutes) < 1 || Math.Abs((interval.Start - end).TotalMinutes) < 1))
            score += 12;
        return score - start.Minute / 60d;
    }

    private static DateTimeOffset RoundUpToQuarterHour(DateTimeOffset value)
    {
        var add = value.Minute % 15 == 0 && value.Second == 0 ? 0 : 15 - value.Minute % 15;
        var rounded = value.AddMinutes(add);
        return new DateTimeOffset(rounded.Year, rounded.Month, rounded.Day, rounded.Hour, rounded.Minute, 0, rounded.Offset);
    }

    private static void PopulateLocalMeetingDates(EmailTriageItem item, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        var lower = content.ToLowerInvariant();
        if (!ContainsAny(lower, "meeting", "meet", "call", "catch up", "catch-up", "availability", "available", "schedule", "diary")) return;
        var today = DateTime.Today;
        if (Regex.IsMatch(content, @"\btomorrow\b", RegexOptions.IgnoreCase))
            AddMeetingDateOption(item, today.AddDays(1));

        var named = Regex.Matches(content, @"\b(?<day>\d{1,2})(?:st|nd|rd|th)?\s+(?<month>Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)(?:\s+(?<year>\d{4}))?\b", RegexOptions.IgnoreCase);
        foreach (Match match in named)
        {
            var year = match.Groups["year"].Success ? int.Parse(match.Groups["year"].Value) : today.Year;
            if (!DateTime.TryParse($"{match.Groups["day"].Value} {match.Groups["month"].Value} {year}", CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out var date)) continue;
            if (date.Date < today && !match.Groups["year"].Success) date = date.AddYears(1);
            AddMeetingDateOption(item, date);
        }

        var numeric = Regex.Matches(content, @"\b(?<day>\d{1,2})[/-](?<month>\d{1,2})(?:[/-](?<year>\d{2,4}))?\b");
        foreach (Match match in numeric)
        {
            var year = match.Groups["year"].Success ? int.Parse(match.Groups["year"].Value) : today.Year;
            if (year < 100) year += 2000;
            if (!DateTime.TryParseExact($"{match.Groups["day"].Value}/{match.Groups["month"].Value}/{year}", "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (date.Date < today && !match.Groups["year"].Success) date = date.AddYears(1);
            AddMeetingDateOption(item, date);
        }

        var weekdayAliases = new Dictionary<DayOfWeek, string>
        {
            [DayOfWeek.Monday] = "Mon(?:day)?",
            [DayOfWeek.Tuesday] = "Tue(?:s|sday)?",
            [DayOfWeek.Wednesday] = "Wed(?:nesday)?",
            [DayOfWeek.Thursday] = "Thu(?:r|rs|rsday)?",
            [DayOfWeek.Friday] = "Fri(?:day)?",
            [DayOfWeek.Saturday] = "Sat(?:urday)?",
            [DayOfWeek.Sunday] = "Sun(?:day)?"
        };
        foreach (var (dayOfWeek, alias) in weekdayAliases)
        {
            if (!Regex.IsMatch(content, $@"\b(?:next\s+)?{alias}\b", RegexOptions.IgnoreCase)) continue;
            var daysAhead = ((int)dayOfWeek - (int)today.DayOfWeek + 7) % 7;
            if (daysAhead == 0 || Regex.IsMatch(content, $@"\bnext\s+{alias}\b", RegexOptions.IgnoreCase)) daysAhead += 7;
            AddMeetingDateOption(item, today.AddDays(daysAhead));
        }
    }

    private static void AddMeetingDateOption(EmailTriageItem item, DateTime date)
    {
        if (date.Date < DateTime.Today || item.MeetingDateOptions.Any(option => option.Date.Date == date.Date)) return;
        item.MeetingDateOptions.Add(new MeetingDateOption { Date = date.Date });
        item.SelectedMeetingDate = date.Date;
    }

    private static string NormaliseMeetingPreference(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "morning" => "Morning",
        "afternoon" => "Afternoon",
        "earliest" => "Earliest",
        "latest" => "Latest",
        _ => "Any"
    };

    private sealed class MeetingRequestExtraction
    {
        public List<string> Dates { get; init; } = [];
        public int DurationMinutes { get; init; } = 30;
        public string Preference { get; init; } = "Any";
        public string Notes { get; init; } = "";
    }
    [RelayCommand]
    private async Task GenerateEmailDraftAsync(EmailTriageItem? item) =>
        await GenerateEmailDraftCoreAsync(item, guardianAuthored: false);

    [RelayCommand]
    private async Task GenerateGuardianEmailDraftAsync(EmailTriageItem? item) =>
        await GenerateEmailDraftCoreAsync(item, guardianAuthored: true);

    private async Task GenerateEmailDraftCoreAsync(EmailTriageItem? item, bool guardianAuthored)
    {
        if (item is null) return;
item.DraftStatus = guardianAuthored
            ? "Guardian is preparing a transparent assistant-authored draft..."
            : "Guardian is studying your recent writing style and drafting in your voice...";
        try
        {
            await PrepareMeetingAvailabilityFromContentAsync(item);
            string instruction;
            if (guardianAuthored)
            {
                instruction = """
                    Write as Guardian, David Myers' personal assistant. Do not include an explanatory disclosure about triage, prioritisation, AI or drafting. Be concise and natural, do not imply that Guardian can make decisions for David, and close with the exact single-line signature "Guardian | Personal Assistant to David Myers". Do not pretend the words are directly David's.
                    """;
            }
            else
            {
                var service = _services.GetRequiredService<IEmailService>();
                var samples = await service.GetRecentSentMessageBodiesAsync(5);
                var sampleText = samples.Count == 0
                    ? "No recent writing samples were available. Use a concise, warm and professional British business tone."
                    : string.Join("\n\n--- WRITING SAMPLE ---\n", samples);
                instruction = $"""
                    Write in David's voice. Infer only stylistic traits from the samples below: greeting, sentence length, directness, warmth, vocabulary and sign-off. Do not copy names, facts, promises, dates, confidential details or subject matter from the samples. Do not mention Guardian or AI. Do not claim David reviewed or approved the draft.

                    DAVID'S RECENT WRITING SAMPLES:
                    {sampleText}
                    """;
            }

            var prompt = $"""
                Draft a concise professional email reply for David Myers.
                {instruction}
                Never invent facts, dates, promises, attachments, decisions or availability. Use [bracketed placeholders] only for genuinely missing information. Calendar availability supplied below has already been verified against Outlook: reproduce those dates and times as normal text without square brackets. Do not put any verified date or time in brackets.
                Address the reply only to the sender. Mention or instruct another named person only when the source message explicitly identifies that person and explicitly assigns them the relevant action; otherwise omit the third-party instruction.
                Match the sender's formality. Return only the reply body, with no subject line and no markdown.

                From: {item.SenderLine}
                Subject: {item.Message.Subject}
                Received: {item.Message.ReceivedAt.LocalDateTime:f}
                Message content: {item.DisplayContent}
                Triage context: {item.Reason}
                Calendar availability: {(string.IsNullOrWhiteSpace(item.MeetingSuggestions) ? "No availability guidance has been requested." : item.MeetingSuggestions)}
                """;
            var generatedDraft = (await _aiProvider.AskAsync(prompt)).Trim();
            item.DraftText = NormaliseVerifiedAvailabilityFormatting(generatedDraft, item.MeetingSuggestions);
            item.DraftStatus = guardianAuthored
                ? "Guardian-authored draft ready with the formatted assistant signature; review before saving."
                : "Drafted in your inferred voice. Review and edit before saving.";
        }
        catch (Exception ex)
        {
            item.DraftStatus = "Could not generate draft: " + ex.Message;
        }
    }
    private static string NormaliseVerifiedAvailabilityFormatting(string draft, string meetingSuggestions)
    {
        if (string.IsNullOrWhiteSpace(meetingSuggestions)) return draft;

        var result = Regex.Replace(
            draft,
            @"\[(?<value>\d{1,2}:\d{2}\s*[-–—]\s*\d{1,2}:\d{2})\]",
            match => match.Groups["value"].Value);
        result = Regex.Replace(
            result,
            @"\[(?<value>(?:Mon(?:day)?|Tue(?:sday)?|Wed(?:nesday)?|Thu(?:rsday)?|Fri(?:day)?|Sat(?:urday)?|Sun(?:day)?)\s+\d{1,2}(?:st|nd|rd|th)?\s+(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)(?:\s+\d{4})?)\]",
            match => match.Groups["value"].Value,
            RegexOptions.IgnoreCase);
        result = Regex.Replace(
            result,
            @"\[(?<value>\d{1,2}[/-]\d{1,2}(?:[/-]\d{2,4})?)\]",
            match => match.Groups["value"].Value);
        return result;
    }
    [RelayCommand]
    private async Task SaveEmailDraftAsync(EmailTriageItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.DraftText) || item.ReplySent) return;
        item.DraftStatus = "Saving your edited reply to Outlook Drafts...";
        try
        {
            await SaveOrUpdateEmailDraftAsync(item);
            item.DraftStatus = "Saved to Outlook Drafts. Continue editing here or send when ready.";
        }
        catch (Exception ex)
        {
            item.DraftStatus = "Could not save Outlook draft: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SendEmailDraftAsync(EmailTriageItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.DraftText) || item.ReplySent) return;
item.DraftStatus = "Saving final edits and sending...";
        try
        {
            await SaveOrUpdateEmailDraftAsync(item);
            var service = _services.GetRequiredService<IEmailService>();
            await service.SendDraftAsync(item.OutlookDraftId);
            item.ReplySent = true;
            item.DraftStatus = $"Reply sent to {item.Message.SenderAddress} at {DateTime.Now:HH:mm}.";
            ResolveAttention($"email:management:{item.Message.Id}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            item.DraftStatus = "Send permission is not available. Add delegated Mail.Send in Entra, then sign in again. Your Outlook draft has been preserved.";
        }
        catch (Exception ex)
        {
            item.DraftStatus = "Send failed; your Outlook draft has been preserved. " + ex.Message;
        }
    }

    private async Task SaveOrUpdateEmailDraftAsync(EmailTriageItem item)
    {
        var service = _services.GetRequiredService<IEmailService>();
        if (string.IsNullOrWhiteSpace(item.OutlookDraftId))
        {
            var created = await service.CreateReplyDraftAsync(item.Message.Id, item.DraftText);
            item.OutlookDraftId = created.Id;
            item.OutlookDraftWebLink = created.WebLink;
            return;
        }
        await service.UpdateDraftAsync(item.OutlookDraftId, item.DraftText);
    }
    [RelayCommand]
    private async Task ApplyEmailCategoryAsync(EmailTriageItem? item)
    {
        if (item is null) return;
        var category = item.IsManagement ? ManagementAttentionCategory : item.Classification switch
        {
            "Spam" => "Nekomata - Suspected Spam",
            "Marketing" => "Nekomata - Marketing",
            "Priority" => "Nekomata - Priority",
            _ => "Nekomata - Reviewed"
        };
        try
        {
            var service = _services.GetRequiredService<IEmailService>();
            await service.ApplyCategoryAsync(item.Message.Id, item.Message.Categories, category);
            item.CategoryApplied = true;
            item.DraftStatus = $"Applied Outlook category: {category}.";
        }
        catch (Exception ex)
        {
            item.DraftStatus = "Could not apply category: " + ex.Message;
        }
    }

    private void StartEmailMonitoring()
    {
        if (_emailMonitorStarted) return;
        _emailMonitorTimer.Tick += async (_, _) => await RefreshEmailAsync();
        _emailMonitorTimer.Start();
        _emailMonitorStarted = true;
    }

    private void AlertForNewManagementMessages(IEnumerable<EmailTriageItem> items)
    {
        foreach (var item in items.Where(candidate => candidate.IsManagement && _alertedManagementMessages.Add(candidate.Message.Id)))
        {
            var content = string.IsNullOrWhiteSpace(item.DisplayContent)
                ? "No message text was returned."
                : item.DisplayContent;
            if (content.Length > 3000)
                content = content[..3000] + Environment.NewLine + "[Message shortened - open in Outlook for the remainder.]";

            RaiseAttention(
                $"email:management:{item.Message.Id}",
                "MANAGEMENT EMAIL",
                "High",
                item.Message.Subject,
                $"From {item.SenderLine}{Environment.NewLine}{Environment.NewLine}{content}",
                "open_email",
                item.Message.Id,
                item.Message.WebLink);
        }
    }

    private static void PromoteManagementEmail(EmailTriageItem item)
    {
        item.Classification = "Management";
        item.PriorityScore = 100;
        item.Reason = "The sender is on your locally configured management list.";
        item.SuggestedAction = "Read and respond as a priority";
        item.ReplyRecommended = true;
    }

    private static string ClassifyEmail(EmailMessage message)
    {
        var content = $"{message.Subject} {message.BodyPreview}".ToLowerInvariant();
        if (ContainsAny(content, "crypto", "bitcoin", "gift card", "wire transfer", "claim your prize", "password expires today")) return "Spam";
        if (ContainsAny(content, "unsubscribe", "newsletter", "webinar", "promotion", "special offer", "marketing preferences")) return "Marketing";
        if (message.Importance.Equals("high", StringComparison.OrdinalIgnoreCase) || ContainsAny(content, "urgent", "deadline", "outage", "critical")) return "Priority";
        if (ContainsAny(content, "please", "can you", "could you", "action required", "approval", "confirm", "question")) return "Action";
        return "FYI";
    }

    private static int ScoreEmail(EmailMessage message)
    {
        var classification = ClassifyEmail(message);
        var baseScore = classification switch { "Spam" => 5, "Marketing" => 15, "Priority" => 85, "Action" => 65, _ => 40 };
        if (message.HasAttachments && classification is "Priority" or "Action") baseScore += 5;
        return Math.Clamp(baseScore, 0, 100);
    }

    private static void ApplyLocalExplanation(EmailTriageItem item)
    {
        (item.Reason, item.SuggestedAction, item.ReplyRecommended) = item.Classification switch
        {
            "Spam" => ("Contains wording commonly associated with suspicious or unsolicited mail; verify before acting.", "Verify sender; ignore if unexpected", false),
            "Marketing" => ("Looks like promotional, newsletter or webinar content.", "Read later or unsubscribe", false),
            "Priority" => ("Marked high importance or contains time-sensitive language.", "Review next", true),
            "Action" => ("The message appears to request a response or action.", "Review and respond", true),
            _ => ("No explicit action or urgency was detected.", "Read when convenient", false)
        };
    }

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(value.Contains);

    private void EnsureManagementSendersLoaded()
    {
        if (_managementSendersLoaded) return;
        _managementSendersLoaded = true;
        try
        {
            if (!File.Exists(ManagementSendersPath)) return;
            var senders = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ManagementSendersPath)) ?? [];
            foreach (var sender in senders.Where(value => !string.IsNullOrWhiteSpace(value)))
                _managementSenders.Add(sender.Trim());
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Could not load management senders: " + ex);
        }
    }

    private void SaveManagementSenders()
    {
        try
        {
            var directory = Path.GetDirectoryName(ManagementSendersPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(ManagementSendersPath, JsonSerializer.Serialize(_managementSenders.OrderBy(value => value)));
        }
        catch (Exception ex)
        {
            EmailStatus = "Could not save management sender list: " + ex.Message;
        }
    }

    private static string ManagementSendersPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nekomata",
        "management-senders.json");

    private void RaiseEmailSummaryChanged()
    {
        OnPropertyChanged(nameof(UnreadEmailCount));
        OnPropertyChanged(nameof(PriorityEmailCount));
        OnPropertyChanged(nameof(FilteredEmailCount));
        OnPropertyChanged(nameof(ManagementEmailCount));
        OnPropertyChanged(nameof(HasEmailItems));
        OnPropertyChanged(nameof(ShowEmailEmptyState));
        OnPropertyChanged(nameof(EmailLastRefreshedLabel));
    }
}