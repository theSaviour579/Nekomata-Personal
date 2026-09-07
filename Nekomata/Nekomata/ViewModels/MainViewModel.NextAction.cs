using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Missions;
using Nekomata.Models.Missions;
using Nekomata.Models.Planning;


namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private Window? _nextActionWindow;
    private sealed class NextActionState
    {
        public DateTime Day { get; set; }
        public DateTimeOffset LastPrompt { get; set; }
        public HashSet<string> Dismissed { get; set; } = [];
    }
    private NextActionState? _nextActionState;
    private static string NextActionPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "next-action.json");
    private void SaveNextActionState()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NextActionPath)!);
        File.WriteAllText(NextActionPath + ".tmp", JsonSerializer.Serialize(_nextActionState));
        File.Move(NextActionPath + ".tmp", NextActionPath, true);
    }
    private double NextActionFreeMinutes()
    {
        if (IsInitialLoading || MissionActive || DayReviewVisible || _preflightWindow != null || _meetingPreparationWindow != null ||
            !CalendarLoaded || CalendarBusy || _integrationRefreshBusy) return 0;
        var now = DateTimeOffset.Now;
        var local = now.LocalDateTime;
        var hours = _services.GetRequiredService<WorkingDaySettings>();
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || local < hours.GetStart(local) || local >= hours.GetEnd(local).AddMinutes(-15) ||
            CalendarEvents.Any(x => x.Start <= now && x.End > now)) return 0;
        var boundary = new DateTimeOffset(hours.GetEnd(local).AddMinutes(-15));
        foreach (var entry in CalendarEvents.Where(x => x.Start > now && x.Start < boundary)) boundary = entry.Start;
        if (hours.IncludeLunchBreak)
        {
            if (local >= hours.GetLunchStart(local) && local < hours.GetLunchEnd(local)) return 0;
            var lunch = new DateTimeOffset(hours.GetLunchStart(local));
            if (lunch > now && lunch < boundary) boundary = lunch;
        }
        return Math.Max(0, (boundary - now).TotalMinutes - 2); // Small transition buffer.
    }
    private List<MissionCandidate> NextActionCandidates()
    {
        var excluded = new HashSet<string>(_nextActionState!.Dismissed);
        var decisions = new Dictionary<string, Nekomata.Models.Guardian.WrapUpObjective>();
        var folder = Path.GetDirectoryName(WrapUpPath(DateTime.Today))!;
        if (Directory.Exists(folder))
            foreach (var path in Directory.EnumerateFiles(folder, "*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day) || day > DateTime.Today) continue;
                foreach (var item in JsonSerializer.Deserialize<WrapUpDraft>(File.ReadAllText(path))?.Objectives ?? [])
                    if (item.Decision != "No change") decisions[item.Key] = item;
            }
        foreach (var candidate in Workspace.RankedMissionCandidates)
        {
            var task = Workspace.Tasks.FirstOrDefault(x => candidate.TaskId != null && x.Id == candidate.TaskId);
            var suppressed = task != null && (task.Completed || task.IsCompleted || !string.IsNullOrWhiteSpace(task.BlockingReason));
            if (candidate.TaskId != null && decisions.TryGetValue($"task:{candidate.TaskId}", out var decision))
                suppressed |= decision.Decision == "Defer until" && (decision.DeferUntil == null || decision.DeferUntil.Value.Date > DateTime.Today) ||
                    decision.Decision == "Remove from next plan" && decision.PlanDate.Date <= DateTime.Today ||
                    decision.Decision == "Continue next workday" && decision.PlanDate.Date > DateTime.Today;
            if (suppressed) excluded.Add(NextActionPolicy.Key(candidate));
        }
        var learning = ReadDecisionLearning();
        return NextActionPolicy.Select(Workspace.RankedMissionCandidates, NextActionFreeMinutes(), _personalProfile.Current.DisplayName, excluded)
            .OrderByDescending(x => x.RequiresImmediateAttention || x.IsP1)
            .ThenByDescending(x => x.Score - DecisionLearning.Penalty(learning.Preferences, NextActionPolicy.Key(x), x.RequiresImmediateAttention || x.IsP1, DateTimeOffset.Now)).ToList();
    }
    private void CheckNextAction()
    {
        try
        {
            if (_nextActionWindow != null)
            {
                if (NextActionFreeMinutes() < 10) _nextActionWindow.Close();
                return;
            }
            if (NextActionFreeMinutes() < 10) return;
            if (!CanPresentRoutinePrompt(Nekomata.Core.Guardian.Anticipation.GuardianPromptKind.NextAction)) return;
            _nextActionState ??= File.Exists(NextActionPath) ? JsonSerializer.Deserialize<NextActionState>(File.ReadAllText(NextActionPath)) : null;
            if (_nextActionState?.Day != DateTime.Today) _nextActionState = new() { Day = DateTime.Today };
            if (DateTimeOffset.Now - _nextActionState.LastPrompt < TimeSpan.FromMinutes(45)) return;
            var candidates = NextActionCandidates();
            if (candidates.Count == 0) return;
            _nextActionState.LastPrompt = DateTimeOffset.Now;
            SaveNextActionState();
            ShowNextAction(candidates);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Next action unavailable: " + ex.Message); }
    }
    private void ShowNextAction(List<MissionCandidate> candidates)
    {
        var index = 0;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "YOUR NEXT ACTION", FontSize = 22, FontWeight = FontWeights.Bold });
        var title = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,16,0,10) };
        var reason = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,18) };
        panel.Children.Add(title); panel.Children.Add(reason);
        var start = new Button { Content = "START THIS OBJECTIVE" };
        var another = new Button { Content = "CHOOSE ANOTHER", Margin = new Thickness(0,8,0,0), IsEnabled = candidates.Count > 1 };
        var later = new Button { Content = "NOT NOW", Margin = new Thickness(0,8,0,0) };
        panel.Children.Add(new TextBlock { Text = "OPTIONAL FEEDBACK · for Choose another / Not now", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,6) });
        var feedback = new ComboBox { ItemsSource = new[] { "No feedback", "Wrong priority", "Wrong owner", "Missing context", "Bad timing", "Already handled" }, SelectedIndex = 0, Foreground = System.Windows.Media.Brushes.Black, Background = System.Windows.Media.Brushes.White, Margin = new Thickness(0,0,0,12) };
        panel.Children.Add(feedback);
        panel.Children.Add(start); panel.Children.Add(another); panel.Children.Add(later);
        var window = new Window { Title = "Guardian · next action", Width = 540, SizeToContent = SizeToContent.Height, Content = panel, ShowActivated = false };
        window.SetResourceReference(Control.BackgroundProperty, "NekoBackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "NekoTextBrush");
        _nextActionWindow = window;
        void Render()
        {
            var item = candidates[index];
            title.Text = item.Title;
            reason.Text = $"About {Math.Floor(NextActionFreeMinutes())} usable minutes are available. This objective needs {item.EstimatedMinutes} minutes and has score {item.Score}. " +
                (index == 0 ? "It is the highest-ranked eligible option after approved learning preferences. " : "This is another eligible option. ") +
                "No known waiting/hold flag or conflicting owner recommendation. Starting requires your approval.";
        }
        Render();
        another.Click += (_, _) =>
        {
            try { RecordDecisionFeedback(candidates[index], feedback.SelectedItem?.ToString() ?? "No feedback"); feedback.SelectedIndex = 0; index = (index + 1) % candidates.Count; Render(); }
            catch (Exception ex) { reason.Text = "Could not save feedback: " + ex.Message; }
        };
        later.Click += (_, _) =>
        {
            try { RecordDecisionFeedback(candidates[index], feedback.SelectedItem?.ToString() ?? "No feedback"); _nextActionState!.Dismissed.Add(NextActionPolicy.Key(candidates[index])); SaveNextActionState(); window.Close(); }
            catch (Exception ex) { reason.Text = "Could not save your choice: " + ex.Message; }
        };
        start.Click += async (_, _) =>
        {
            start.IsEnabled = false;
            try
            {
                var selected = NextActionCandidates().FirstOrDefault(x => NextActionPolicy.Key(x) == NextActionPolicy.Key(candidates[index]));
                if (selected == null) { reason.Text = "This option is no longer available or no longer fits. Close this prompt and review your dashboard."; return; }
                Workspace.CurrentMission = _services.GetRequiredService<IMissionFactory>().Create(selected);
                await BeginMissionAsync();
                window.Close();
            }
            catch (Exception ex) { reason.Text = "Could not start: " + ex.Message; }
            finally { start.IsEnabled = true; }
        };
        window.Closed += (_, _) => { _nextActionWindow = null; RoutinePromptClosed(); };
        window.Show();
    }
}
