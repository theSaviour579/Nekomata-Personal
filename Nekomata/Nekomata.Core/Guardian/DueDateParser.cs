using System.Globalization;
using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian;

/// <summary>
/// Turns spoken deadlines into a local date-time: "Friday", "tomorrow", "end of day", "end of the week",
/// "next week", "Monday morning", "20 Oct", "the 20th". Deadlines without a time mean 17:00.
/// </summary>
public static partial class DueDateParser
{
    private static readonly TimeSpan EndOfDay = new(17, 0, 0);

    public static DateTime? Parse(string? text, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();
        value = Regex.Replace(value, @"^(?:by|before|for|on|due)\s+", "");
        value = Regex.Replace(value, @"^(?:the\s+)?end\s+of\s+", "eo ");

        var time = EndOfDay;
        if (value.EndsWith(" morning")) { time = new(10, 0, 0); value = value[..^8]; }
        else if (value.EndsWith(" afternoon")) { time = new(15, 0, 0); value = value[..^10]; }
        else if (value.EndsWith(" lunchtime") || value.EndsWith(" lunch")) { time = new(12, 0, 0); value = value[..value.LastIndexOf(' ')]; }
        value = value.Trim();

        DateTime? day = value switch
        {
            "today" or "eo day" or "eo today" or "eod" or "tonight" or "this afternoon" => now.Date,
            "tomorrow" or "eo tomorrow" => now.Date.AddDays(1),
            "eo week" or "eo the week" or "eo this week" or "this week" or "eow" => NextOrSame(now.Date, DayOfWeek.Friday),
            "next week" => NextOrSame(now.Date.AddDays(7 - ((int)now.DayOfWeek + 6) % 7), DayOfWeek.Friday),
            "eo month" or "eo the month" => new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month)),
            _ => null
        };
        if (value == "this morning") { day = now.Date; time = new(12, 0, 0); }

        if (day is null)
        {
            var weekday = WeekdayRegex().Match(value);
            if (weekday.Success)
            {
                var target = Enum.Parse<DayOfWeek>(weekday.Groups["d"].Value, ignoreCase: true);
                // "Monday" and "next Monday" both mean the coming one - the usual meaning in a work request.
                day = NextOrSame(now.Date.AddDays(1), target);
            }
        }

        if (day is null)
        {
            var ordinal = OrdinalRegex().Match(value);
            if (ordinal.Success && int.TryParse(ordinal.Groups["n"].Value, out var dom) && dom is >= 1 and <= 31)
            {
                var candidate = SafeDate(now.Year, now.Month, dom);
                if (candidate is not null && candidate.Value < now.Date) candidate = SafeDate(now.AddMonths(1).Year, now.AddMonths(1).Month, dom);
                day = candidate;
            }
        }

        if (day is null)
        {
            string[] formats = ["d MMM", "d MMMM", "d MMM yyyy", "d MMMM yyyy", "dd/MM", "dd/MM/yyyy", "d/M", "d/M/yyyy", "yyyy-MM-dd"];
            var cleaned = Regex.Replace(value, @"(\d+)(st|nd|rd|th)", "$1");
            if (DateTime.TryParseExact(cleaned, formats, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                if (!Regex.IsMatch(cleaned, @"\d{4}") && parsed.Date < now.Date) parsed = parsed.AddYears(1);
                day = parsed.Date;
            }
        }

        return day?.Add(time);
    }

    private static DateTime NextOrSame(DateTime from, DayOfWeek day) => from.AddDays(((int)day - (int)from.DayOfWeek + 7) % 7);

    private static DateTime? SafeDate(int year, int month, int day) =>
        day <= DateTime.DaysInMonth(year, month) ? new DateTime(year, month, day) : null;

    [GeneratedRegex(@"^(?:(?<next>next)\s+|this\s+)?(?<d>monday|tuesday|wednesday|thursday|friday|saturday|sunday)$")]
    private static partial Regex WeekdayRegex();

    [GeneratedRegex(@"^(?:the\s+)?(?<n>\d{1,2})(?:st|nd|rd|th)$")]
    private static partial Regex OrdinalRegex();
}
