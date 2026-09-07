using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using Nekomata.Core.Missions;
using Nekomata.Models.Missions;

namespace Nekomata.UI.ViewModels;
public partial class MainViewModel
{
    private sealed class DecisionLearningState
    {
        public List<DecisionFeedback> Feedback { get; set; } = [];
        public List<LearnedPreference> Preferences { get; set; } = [];
    }
    private static string DecisionLearningPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "decision-learning.json");
    private DecisionLearningState ReadDecisionLearning() => File.Exists(DecisionLearningPath)
        ? JsonSerializer.Deserialize<DecisionLearningState>(File.ReadAllText(DecisionLearningPath)) ?? new() : new();
    private void SaveDecisionLearning(DecisionLearningState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DecisionLearningPath)!);
        File.WriteAllText(DecisionLearningPath + ".tmp", JsonSerializer.Serialize(state));
        File.Move(DecisionLearningPath + ".tmp", DecisionLearningPath, true);
    }
    private void RecordDecisionFeedback(MissionCandidate candidate, string reason)
    {
        if (reason == "No feedback") return;
        var state = ReadDecisionLearning();
        var key = NextActionPolicy.Key(candidate);
        if (!state.Feedback.Any(x => x.ObjectiveKey == key && x.Reason == reason && x.Day.Date == DateTime.Today))
            state.Feedback.Add(new() { ObjectiveKey = key, Title = candidate.Title, Reason = reason, Day = DateTime.Today });
        SaveDecisionLearning(state);
    }
    [RelayCommand]
    private void ShowDecisionLearning()
    {
        try
        {
            var state = ReadDecisionLearning();
            var panel = new StackPanel { Margin = new Thickness(24) };
            var window = new Window { Title = "Guardian · decision learning", Width = 780, Height = 680, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            window.SetResourceReference(Control.BackgroundProperty, "NekoBackgroundBrush");
            window.SetResourceReference(Control.ForegroundProperty, "NekoTextBrush");
            void Render()
            {
                panel.Children.Clear();
                panel.Children.Add(new TextBlock { Text = "WHAT GUARDIAN HAS LEARNED", FontSize = 23, FontWeight = FontWeights.Bold });
                panel.Children.Add(new TextBlock { Text = "Feedback from next-action suggestions only. Nothing changes ranking until you approve. Three separate days of ‘Wrong priority’ within 30 days can propose a seven-day, 25-point reduction for this objective in next-action suggestions only. Urgent work is exempt; source tasks are unchanged.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,18) });
                if (state.Feedback.Count == 0) panel.Children.Add(new TextBlock { Text = "No feedback recorded yet. Reasons are optional on next-action prompts." });
                foreach (var group in state.Feedback.GroupBy(x => x.ObjectiveKey).ToList())
                {
                    var key = group.Key;
                    var active = state.Preferences.FirstOrDefault(x => x.ObjectiveKey == key && x.Until > DateTimeOffset.Now);
                    var content = new StackPanel();
                    content.Children.Add(new TextBlock { Text = group.Last().Title, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
                    content.Children.Add(new TextBlock { Text = string.Join("\n", group.GroupBy(x => x.Reason).Select(x => $"{x.Key}: {x.Select(y => y.Day.Date).Distinct().Count()} day(s)")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) });
                    content.Children.Add(new TextBlock { Text = active != null ? $"Approved reduction until {active.Until.LocalDateTime:g}" : "Observation only — no active preference.", TextWrapping = TextWrapping.Wrap });
                    var buttons = new WrapPanel { Margin = new Thickness(0,10,0,0) };
                    if (active == null && DecisionLearning.CanPropose(state.Feedback, key, DateTime.Today))
                    {
                        var approve = new Button { Content = "APPROVE 7-DAY PRIORITY REDUCTION", Margin = new Thickness(0,0,8,0) };
                        approve.Click += (_, _) =>
                        {
                            try { state.Preferences.RemoveAll(x => x.ObjectiveKey == key); state.Preferences.Add(new() { ObjectiveKey = key, Until = DateTimeOffset.Now.AddDays(7) }); SaveDecisionLearning(state); Render(); }
                            catch (Exception ex) { MessageBox.Show("Could not save preference: " + ex.Message); }
                        };
                        buttons.Children.Add(approve);
                    }
                    var forget = new Button { Content = "FORGET THIS OBJECTIVE'S FEEDBACK" };
                    forget.Click += (_, _) =>
                    {
                        try { state.Feedback.RemoveAll(x => x.ObjectiveKey == key); state.Preferences.RemoveAll(x => x.ObjectiveKey == key); SaveDecisionLearning(state); Render(); }
                        catch (Exception ex) { MessageBox.Show("Could not forget feedback: " + ex.Message); }
                    };
                    buttons.Children.Add(forget); content.Children.Add(buttons);
                    var card = new Border { Child = content, Padding = new Thickness(16), CornerRadius = new CornerRadius(12), Margin = new Thickness(0,0,0,12) };
                    card.SetResourceReference(Border.BackgroundProperty, "NekoSurfaceAltBrush"); panel.Children.Add(card);
                }
            }
            Render(); window.Show();
        }
        catch (Exception ex) { MessageBox.Show("Decision learning unavailable: " + ex.Message); }
    }
}
