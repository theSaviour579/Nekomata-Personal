using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian.Context;

public static partial class ProjectCommunicationPolicy
{
    public static bool RequiresEndUserResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsMachineOrUnsafeContent(text)) return false;
        return EndUser().IsMatch(text) && DirectUpdateRequest().IsMatch(text);
    }

    public static bool IndicatesExternalWait(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsMachineOrUnsafeContent(text)) return false;
        return ExternalParty().IsMatch(text) && WaitingLanguage().IsMatch(text);
    }

    public static bool IsMachineOrUnsafeContent(string text) =>
        SqlStatement().IsMatch(text) || SecurityNoise().IsMatch(text) ||
        (text.Count(x => x is '=' or ';' or '{' or '}') >= 3 && !DirectUpdateRequest().IsMatch(text));

    public static DateTimeOffset NextWorkingMorning(DateTimeOffset value)
    {
        var local = value.LocalDateTime.Date.AddDays(1).AddHours(9);
        while (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) local = local.AddDays(1);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    [GeneratedRegex(@"(?i)\b(end[ -]?user|customer|client|requester|site|stakeholder)\b")]
    private static partial Regex EndUser();
    [GeneratedRegex(@"(?i)(?:\b(?:please|can|could|would|need(?:s)? to|should)\b.{0,70}\b(?:update|reply|respond|let .+ know|contact)\b|\b(?:end[ -]?user|customer|client|requester|stakeholder)\s+(?:need(?:s)?|require(?:s)?)\s+(?:an?\s+)?update\b)")]
    private static partial Regex DirectUpdateRequest();
    [GeneratedRegex(@"(?i)\b(supplier|vendor|third[ -]?party|developer|provider|external|customer|client|requester|site)\b")]
    private static partial Regex ExternalParty();
    [GeneratedRegex(@"(?i)\b(awaiting|waiting (?:for|on)|pending|blocked (?:by|on)|with .+ now|until .+ replies|once .+ responds)\b")]
    private static partial Regex WaitingLanguage();
    [GeneratedRegex(@"(?im)^\s*(?:select|update|insert|delete|merge|alter|create|drop)\b|\b(?:set|where|join|from)\s+[A-Za-z_][A-Za-z0-9_.]*\s*(?:=|\bin\s*\()")]
    private static partial Regex SqlStatement();
    [GeneratedRegex(@"(?i)\b(?:i am a hacker|gained access to your|infected with malware|bitcoin|ransom|password expires|gift card)\b")]
    private static partial Regex SecurityNoise();
}
