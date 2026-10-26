using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Core.Analytics;
using Nekomata.Core.Missions;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Guardian;
using System.IO;
using System.Text;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private async Task<string> BuildWeeklyReportAsync()
    {
        var now = DateTimeOffset.Now;
        var monday = now.Date.AddDays(-((7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        var start = new DateTimeOffset(monday, TimeZoneInfo.Local.GetUtcOffset(monday));
        var events = await _services.GetRequiredService<IWorkdayEventRepository>()
            .GetBetweenAsync(start, start.AddDays(7), 5000);
        var completed = events.Where(x => x.EventType == "ObjectiveCompleted").ToList();
        var interruptions = events.Where(x => x.Category == "Interruption" || x.EventType == "PlanDisrupted").ToList();
        var decisions = events.Where(x => !string.IsNullOrWhiteSpace(x.UserDecision)).ToList();
        var accepted = decisions.Count(x => x.UserDecision.Equals("Accepted", StringComparison.OrdinalIgnoreCase));
        var insights = WorkdayJournalService.BuildInsights(events);
        var significant = events.Where(x => x.Significance >= 65).OrderBy(x => x.OccurredAt).TakeLast(30);
        var overtime = await GetWeeklyOvertimeAsync();
        var executiveValue = await GetCurrentWeekExecutiveValueAsync();
        var roleAlignment = RoleAlignmentEngine.Analyse(executiveValue);
        var report = new StringBuilder()
            .AppendLine($"# Guardian weekly report — {start:dd MMM} to {now:dd MMM yyyy}")
            .AppendLine()
            .AppendLine("## Week at a glance")
            .AppendLine()
            .AppendLine($"- Objectives completed: {completed.Count}")
            .AppendLine($"- Interruptions recorded: {interruptions.Count}")
            .AppendLine($"- Estimated business value delivered: {completed.Sum(x => x.BusinessValue):C0}")
            .AppendLine($"- Guardian decisions recorded: {decisions.Count}")
            .AppendLine($"- Recommendations accepted: {accepted}{(decisions.Count == 0 ? "" : $" ({accepted * 100d / decisions.Count:0}%)")}")
            .AppendLine($"- Tracked additional time: {overtime.TotalLabel}")
            .AppendLine($"- Additional-time wage equivalent: {overtime.EquivalentCost:C2}")
            .AppendLine($"- Additional-time evidence: {FormatOvertimeBreakdown(overtime)}")
            .AppendLine()
            .AppendLine("## Executive value portfolio")
            .AppendLine()
            .AppendLine($"- Recorded investment: {WeeklyOvertimeSummary.FormatMinutes(executiveValue.TotalMinutes)}")
            .AppendLine($"- Labour cost: {executiveValue.LabourCost:C2}")
            .AppendLine($"- Associated financial value (deduplicated): {executiveValue.AssociatedFinancialValue:C0}")
            .AppendLine($"- Associated value/hour: {executiveValue.ValuePerHour:C0} (context, not realised return)")
            .AppendLine($"- High strategic-relevance episodes: {executiveValue.HighStrategicRelevanceCount}")
            .AppendLine($"- Value evidence: {executiveValue.MeasuredCount} measured · {executiveValue.EstimatedCount} estimated · {executiveValue.ProxyCount} proxy · {executiveValue.NonFinancialCount} non-financial")
            .AppendLine();
        foreach (var area in executiveValue.Areas)
            report.AppendLine($"- **{area.Name}:** {FormatInvestmentArea(area)}");
        if (executiveValue.Judgements.Count > 0)
        {
            report.AppendLine().AppendLine("### Guardian judgement").AppendLine();
            foreach (var judgement in executiveValue.Judgements) report.AppendLine($"- {judgement}");
        }
        report.AppendLine().AppendLine("## Role alignment").AppendLine()
            .AppendLine($"- Role alignment ({roleAlignment.RoleName}): {(roleAlignment.HasEvidence ? $"{roleAlignment.Score}%" : "insufficient evidence")}");
        foreach (var band in roleAlignment.Bands)
            report.AppendLine($"- **{band.Name}:** {band.ActualPercent:0.#}% · target {band.TargetLabel} · {band.Status}");
        foreach (var insight in roleAlignment.Insights)
            report.AppendLine($"- Guardian: {insight}");
        report
            .AppendLine()
            .AppendLine("## What Guardian learned")
            .AppendLine();
        foreach (var insight in insights) report.AppendLine($"- **{insight.Title}:** {insight.Detail}");
        report.AppendLine().AppendLine("## Meaningful timeline").AppendLine();
        if (!significant.Any()) report.AppendLine("No high-significance Chronicle events were recorded.");
        foreach (var item in significant)
            report.AppendLine($"- {item.OccurredAt:ddd HH:mm} — **{item.Title}**: {item.Narrative}");
        return report.ToString();
    }

    private async Task<WeeklyOvertimeSummary> GetWeeklyOvertimeAsync()
    {
        IReadOnlyList<MissionWorkSegment> active = [];
        if (MissionActive && MissionStartedAt is DateTime startedAt)
            active = CaptureMissionWorkSegments(startedAt, GetCurrentMissionElapsed());
        return await _services.GetRequiredService<WeeklyOvertimeService>().GetCurrentWeekAsync(DateTime.Now, active);
    }

    private static string FormatOvertimeSummary(WeeklyOvertimeSummary summary)
    {
        var days = summary.Days.Count == 0
            ? "No additional-time work has been recorded this week."
            : string.Join(" · ", summary.Days.Select(item => $"{item.Date:ddd} {WeeklyOvertimeSummary.FormatMinutes(item.Minutes)}"));
        return $"Tracked additional time this week: {summary.TotalLabel} ({summary.EquivalentCost:C2} at the configured wage rate).\n" +
               $"{days}\n{FormatOvertimeBreakdown(summary)}\n" +
               "This counts recorded mission work before 08:00, during the 12:00–13:00 lunch break, after 16:30 and at weekends; unrecorded work is not inferred.";
    }

    private static string FormatOvertimeBreakdown(WeeklyOvertimeSummary summary) =>
        $"Before 08:00 {WeeklyOvertimeSummary.FormatMinutes(summary.BeforeHoursMinutes)} · lunch {WeeklyOvertimeSummary.FormatMinutes(summary.LunchMinutes)} · " +
        $"after 16:30 {WeeklyOvertimeSummary.FormatMinutes(summary.AfterHoursMinutes)} · weekend {WeeklyOvertimeSummary.FormatMinutes(summary.WeekendMinutes)}";

    private async Task ShowWeeklyOvertimeAsync() =>
        ShowPaletteResponse(FormatOvertimeSummary(await GetWeeklyOvertimeAsync()));

    private async Task<ExecutiveValueSummary> GetCurrentWeekExecutiveValueAsync()
    {
        var now = DateTime.Now;
        var monday = now.Date.AddDays(-((7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        return await _services.GetRequiredService<ExecutiveValueEngine>().BuildAsync(monday, now);
    }

    private static string FormatExecutiveValueSummary(ExecutiveValueSummary summary)
    {
        if (summary.Episodes.Count == 0)
            return "No completed or deferred work episodes have been recorded this week yet. Nekomata will build the portfolio from recorded missions without inventing value.";
        var areas = string.Join("\n", summary.Areas.Select(area =>
            $"• {area.Name}: {FormatInvestmentArea(area)}"));
        var judgement = summary.Judgements.Count == 0 ? "" : "\n\nGuardian judgement:\n" + string.Join("\n", summary.Judgements.Select(x => "• " + x));
        return $"Executive value portfolio · {summary.From:dd MMM}–{summary.Through:dd MMM}\n" +
               $"{WeeklyOvertimeSummary.FormatMinutes(summary.TotalMinutes)} recorded investment · {WeeklyOvertimeSummary.FormatMinutes(summary.AdditionalMinutes)} additional time · {summary.LabourCost:C2} labour cost.\n" +
               $"Associated financial value: {summary.AssociatedFinancialValue:C0} (deduplicated, not represented as realised cash).\n" +
               $"Associated value/hour: {summary.ValuePerHour:C0} · high strategic-relevance episodes: {summary.HighStrategicRelevanceCount}.\n" +
               $"Evidence quality: {summary.MeasuredCount} measured · {summary.EstimatedCount} estimated · {summary.ProxyCount} proxy · {summary.NonFinancialCount} non-financial.\n\n{areas}{judgement}";
    }

    private async Task ShowExecutiveValueAsync() =>
        ShowPaletteResponse(FormatExecutiveValueSummary(await GetCurrentWeekExecutiveValueAsync()));

    private async Task ShowStopDoingAsync() =>
        ShowPaletteResponse(FormatStopDoing(await GetCurrentWeekExecutiveValueAsync()));

    private async Task ShowRoleAlignmentAsync() =>
        ShowPaletteResponse(FormatRoleAlignment(await _services.GetRequiredService<RoleAlignmentEngine>().BuildCurrentWeekAsync(DateTime.Now)));

    private async Task ProtectMyRoleAsync() =>
        ShowPaletteResponse(FormatRoleProtection(await _services.GetRequiredService<RoleAlignmentEngine>().BuildCurrentWeekAsync(DateTime.Now)));

    private static string FormatRoleAlignment(RoleAlignmentSummary summary)
    {
        if (!summary.HasEvidence)
            return $"Role alignment · {summary.RoleName}\nNo recorded work episodes are available this week, so Guardian will not manufacture an alignment score.";
        var bands = string.Join("\n", summary.Bands.Select(band =>
            $"• {band.Name}: {band.ActualPercent:0.#}% · target {band.TargetLabel} · {band.Status}"));
        var insights = summary.Insights.Count == 0 ? "" : "\n\nGuardian's reading:\n" + string.Join("\n", summary.Insights.Select(x => "• " + x));
        var trends = summary.Trends.Count == 0 ? "" : "\n\nThree-week signals:\n" + string.Join("\n", summary.Trends.Select(x => "• " + x));
        return $"Role alignment: {summary.Score}% · {summary.RoleName}\n{bands}{insights}{trends}";
    }

    private static string FormatRoleProtection(RoleAlignmentSummary summary)
    {
        if (!summary.HasEvidence)
            return "Guardian cannot yet justify challenging work on role-alignment grounds; there is no recorded evidence this week.";
        var challenges = summary.Bands.Where(band => band.Status == "Above target" && band.Key is "reactive" or "admin" or "technical")
            .OrderByDescending(band => band.ActualPercent - band.MaximumPercent).ToList();
        if (challenges.Count == 0)
            return $"Role alignment is {summary.Score}%. No work category currently justifies a blanket challenge. Guardian will still test each interruption for repetition, leverage, capacity and a suitable delegate.";
        return $"Protect-my-role mode · alignment {summary.Score}%\n" + string.Join("\n", challenges.Select(band =>
            $"• Challenge additional {band.Name.ToLowerInvariant()} work: already {band.ActualPercent:0.#}% against a {band.TargetLabel} target.")) +
            "\n\nDuring focus, Guardian will recommend queueing or a named handoff only when the incoming request is routine or repeated and the evidence is strong enough. Urgent/high-impact work still wins.";
    }

    private static string FormatStopDoing(ExecutiveValueSummary summary)
    {
        if (summary.Judgements.Count > 0)
            return "What Guardian would challenge from this week's evidence:\n\n" + string.Join("\n\n", summary.Judgements.Select(x => "• " + x));
        var candidate = summary.Clusters.OrderBy(x => x.AverageLeverageScore).ThenByDescending(x => x.Minutes).FirstOrDefault();
        return candidate is null
            ? "There is not enough recorded episode evidence yet to recommend stopping or delegating anything."
            : $"No stop/delegate recommendation has enough repeat evidence yet. The next pattern to watch is {candidate.Name}: " +
              $"{candidate.EpisodeCount} episode(s), {WeeklyOvertimeSummary.FormatMinutes(candidate.Minutes)}, leverage {candidate.AverageLeverageScore}/100.";
    }

    private static string FormatInvestmentArea(InvestmentArea area) =>
        $"{area.CapacityPercent:0.#}% · {WeeklyOvertimeSummary.FormatMinutes(area.Minutes)} invested · " +
        $"{WeeklyOvertimeSummary.FormatMinutes(area.AdditionalMinutes)} additional" +
        (area.AssociatedFinancialValue > 0 ? $" · {area.AssociatedFinancialValue:C0} associated value" : "") +
        $" · {area.ValueProfile}";

    private async Task ExportWeeklyReportAsync()
    {
        try
        {
            var report = await BuildWeeklyReportAsync();
            var dialog = new SaveFileDialog
            {
                Title = "Export Guardian weekly report",
                FileName = $"Guardian-weekly-{DateTime.Today:yyyy-MM-dd}.md",
                DefaultExt = ".md",
                Filter = "Markdown report (*.md)|*.md|Text file (*.txt)|*.txt"
            };
            if (dialog.ShowDialog() != true) return;
            await File.WriteAllTextAsync(dialog.FileName, report);
            ShowPaletteResponse($"Weekly Guardian report exported to {dialog.FileName}.");
        }
        catch (Exception ex) { ShowPaletteResponse($"Guardian could not export the weekly report: {ex.Message}"); }
    }

    private async Task EmailWeeklyReportAsync()
    {
        try
        {
            var recipient = (await _services.GetRequiredService<IMicrosoftAuthenticationService>().GetTokenAsync()).AccountName;
            if (string.IsNullOrWhiteSpace(recipient)) throw new InvalidOperationException("Connect your Microsoft work account first.");
            await _services.GetRequiredService<IEmailService>().SendMessageAsync(
                recipient, $"Guardian weekly report — {DateTime.Today:dd MMM yyyy}", await BuildWeeklyReportAsync());
            await RecordWorkdayEventAsync(new WorkdayEvent
            {
                EventType = "WeeklyReportSent", Category = "Communication", Title = "Weekly report emailed",
                Narrative = "Guardian emailed the weekly working-state report to the signed-in account.", Outcome = "Sent", Significance = 55
            });
            ShowPaletteResponse($"Weekly Guardian report emailed to {recipient}.");
        }
        catch (Exception ex) { ShowPaletteResponse($"Guardian could not email the weekly report: {ex.Message}"); }
    }
}
