using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Missions;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private AdaptiveReplanSuggestion? GetAdaptiveReplan()
    {
        if (!MissionActive || _activeMission == null || !CalendarLoaded || CalendarBusy || _integrationRefreshBusy) return null;
        var now = DateTimeOffset.Now;
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        var local = now.LocalDateTime;
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || local < settings.GetStart(local) || local >= settings.GetEnd(local) ||
            CalendarEvents.Any(x => !x.IsNekomataManaged && x.Start <= now && x.End > now)) return null;
        var boundary = new DateTimeOffset(settings.GetEnd(local));
        foreach (var entry in CalendarEvents.Where(x => x.Start > now && x.Start < boundary)) boundary = entry.Start;
        if (settings.IncludeLunchBreak)
        {
            if (local >= settings.GetLunchStart(local) && local < settings.GetLunchEnd(local)) return null;
            var lunch = new DateTimeOffset(settings.GetLunchStart(local));
            if (lunch > now && lunch < boundary) boundary = lunch;
        }
        return AdaptiveReplanPolicy.Evaluate(_activeMission, Workspace.RankedMissionCandidates,
            Math.Max(0, (MissionSessionEstimate - GetCurrentMissionElapsed()).TotalMinutes),
            (boundary - now).TotalMinutes, !string.IsNullOrWhiteSpace(MissionBlockerNote));
    }

    private void CheckAdaptiveReplan()
    {
        var suggestion = GetAdaptiveReplan();
        if (suggestion == null) return;
        var identity = $"{DateTime.Today:yyyy-MM-dd}|{_activeMission!.SourceType}|{_activeMission.SourceRecordId}|{_activeMission.TaskId}|{_activeMission.Title}|{suggestion.Candidate.SourceType}|{suggestion.Candidate.SourceRecordId}|{suggestion.Candidate.TaskId}|{suggestion.Candidate.Title}";
        var key = "adaptive:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        RaiseAttention(key, "Guardian re-plan", "Info", $"Consider switching to {suggestion.Candidate.Title}",
            $"{suggestion.Reason} Alternative: {suggestion.Candidate.EstimatedMinutes} minutes · score {suggestion.Candidate.Score}. Your work will only change if you choose to switch.", "adaptive_replan");
    }

    private void ReviewAdaptiveReplan(GuardianAttentionItem item)
    {
        var suggestion = GetAdaptiveReplan();
        if (suggestion == null) { ResolveAttention(item.Key); MessageBox.Show("The situation has changed; no switch is currently recommended."); return; }
        var original = _activeMission;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "A BETTER NEXT STEP", FontSize = 22, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = $"{suggestion.Reason}\n\nCurrent: {original!.Title}\nSuggested: {suggestion.Candidate.Title}\n{suggestion.Candidate.EstimatedMinutes} minutes · score {suggestion.Candidate.Score}\n\nSwitching saves your elapsed work and defers the current objective. It does not complete it or move calendar appointments.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,16,0,16) });
        var accept = new Button { Content = "SAVE CURRENT WORK & SWITCH" };
        var keep = new Button { Content = "KEEP CURRENT OBJECTIVE", Margin = new Thickness(0,8,0,0) };
        panel.Children.Add(accept); panel.Children.Add(keep);
        var window = new Window { Title = "Guardian · adaptive re-plan", Width = 580, SizeToContent = SizeToContent.Height, Content = panel };
        window.SetResourceReference(Control.BackgroundProperty, "NekoBackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "NekoTextBrush");
        keep.Click += (_, _) => { ResolveAttention(item.Key); window.Close(); };
        accept.Click += async (_, _) =>
        {
            accept.IsEnabled = false;
            try
            {
                var fresh = GetAdaptiveReplan();
                if (_activeMission != original || Workspace.CurrentMission != original || fresh == null || fresh.Candidate != suggestion.Candidate)
                { MessageBox.Show("The recommendation has changed. Reopen it to review the latest option."); window.Close(); return; }
                await HoldMissionForLaterAsync();
                if (MissionActive) return; // Saving failed; never replace an active session.
                var now = DateTimeOffset.Now;
                var end = new DateTimeOffset(_services.GetRequiredService<WorkingDaySettings>().GetEnd(now.LocalDateTime));
                var nextBoundary = CalendarEvents.Where(x => x.Start > now).Select(x => x.Start).Append(end).Min();
                if (CalendarEvents.Any(x => !x.IsNekomataManaged && x.Start <= now && x.End > now) ||
                    nextBoundary - now < TimeSpan.FromMinutes(fresh.Candidate.EstimatedMinutes))
                {
                    MessageBox.Show("Your current work was saved, but the available calendar window has changed. Review the calendar before starting another objective.");
                    window.Close(); return;
                }
                Workspace.CurrentMission = _services.GetRequiredService<IMissionFactory>().Create(fresh.Candidate);
                await BeginMissionAsync();
                ResolveAttention(item.Key);
                window.Close();
            }
            catch (Exception ex) { MessageBox.Show("Could not switch objectives: " + ex.Message); }
            finally { accept.IsEnabled = true; }
        };
        window.Show();
    }
}
