using System.Globalization;
using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian.OpenLoops;

public static class CommitmentDate
{
    public static DateTime? FromQuote(string quote, DateTime messageLocalDate)
    {
        // Resolve relative language against the message date, never today's polling date.
        if (Regex.IsMatch(quote, @"\b(next|hopefully|hope|aim|aiming|perhaps|if|or|between|not|cannot|can't|won't|might|may|should|expect|try|tentative)\b|\?", RegexOptions.IgnoreCase)) return null;
        var dates = new HashSet<DateTime>();
        foreach (Match match in Regex.Matches(quote, @"\b\d{4}-\d{2}-\d{2}\b"))
            if (DateTime.TryParseExact(match.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) dates.Add(date);
        if (Regex.IsMatch(quote, @"\btomorrow\b", RegexOptions.IgnoreCase)) dates.Add(messageLocalDate.Date.AddDays(1));
        if (Regex.IsMatch(quote, @"\btoday\b", RegexOptions.IgnoreCase)) dates.Add(messageLocalDate.Date);
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
            if (Regex.IsMatch(quote, $@"\b(?:by|on|this)\s+{day}\b", RegexOptions.IgnoreCase))
                dates.Add(messageLocalDate.Date.AddDays(((int)day - (int)messageLocalDate.DayOfWeek + 7) % 7));
        var result = dates.Count == 1 ? dates.Single() : (DateTime?)null;
        return result >= messageLocalDate.Date && result <= messageLocalDate.Date.AddDays(30) ? result : null;
    }

    public static DateTimeOffset FollowUpAt(DateTime promisedDate)
    {
        var next = promisedDate.Date.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) next = next.AddDays(1);
        return new DateTimeOffset(DateTime.SpecifyKind(next.AddHours(8), DateTimeKind.Local));
    }
}
