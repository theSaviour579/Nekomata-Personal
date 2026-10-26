using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Missions;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;

namespace Nekomata.UI.ViewModels;
public partial class MainViewModel
{
    private DecisionLearningState _decisionLearning = new([],[]);
    private async Task<DecisionLearningState> ReadDecisionLearningAsync() =>
        _decisionLearning=await _services.GetRequiredService<IDecisionLearningStore>().LoadAsync();
    private async Task RecordDecisionFeedbackAsync(MissionCandidate candidate, string reason)
    {
        if (reason == "No feedback") return;
        var key = NextActionPolicy.Key(candidate);
        await _services.GetRequiredService<IDecisionLearningStore>().RecordFeedbackAsync(
            new() { ObjectiveKey = key, Title = candidate.Title, Reason = reason, Day = DateTime.Today },"Desktop");
        await ReadDecisionLearningAsync();
    }
    [RelayCommand]
    private async Task ShowDecisionLearning()
    {
        try
        {
            var state = await ReadDecisionLearningAsync();
            IReadOnlyList<GuardianDecisionOutcome> outcomes;
            try { outcomes = await _services.GetRequiredService<IGuardianDecisionOutcomeRepository>().GetRecentAsync(100); }
            catch { outcomes = []; }
            var panel = new StackPanel { Margin = new Thickness(24) };
            var window = new Window { Title = "Guardian · decision learning", Width = 780, Height = 680, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            window.SetResourceReference(Control.BackgroundProperty, "NekoBackgroundBrush");
            window.SetResourceReference(Control.ForegroundProperty, "NekoTextBrush");
            void Render()
            {
                panel.Children.Clear();
                panel.Children.Add(new TextBlock { Text = "WHAT GUARDIAN HAS LEARNED", FontSize = 23, FontWeight = FontWeights.Bold });
                panel.Children.Add(new TextBlock { Text = "Feedback from next-action suggestions only. Nothing changes ranking until you approve. Three separate days of ‘Wrong priority’ within 30 days can propose a seven-day, 25-point reduction for this objective in next-action suggestions only. Urgent work is exempt; source tasks are unchanged.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,18) });
                panel.Children.Add(new TextBlock { Text = "DECISION OUTCOMES", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,8) });
                if (outcomes.Count == 0)
                    panel.Children.Add(new TextBlock { Text = "No authority outcomes have been observed yet.", Margin = new Thickness(0,0,0,18) });
                else
                {
                    var completed = outcomes.Where(x => x.Outcome != GuardianOutcomeDisposition.Pending).ToList();
                    var successful = completed.Count(x => x.Outcome == GuardianOutcomeDisposition.Successful);
                    var accepted = outcomes.Count(x => x.Decision is GuardianDecisionDisposition.Accepted or GuardianDecisionDisposition.AutoExecuted);
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"{accepted} accepted or automatic · {completed.Count} outcomes observed · " +
                               $"{successful} successful{(completed.Count > 0 ? $" · {(successful * 100d / completed.Count):0}% observed success" : "")}",
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10)
                    });
                    foreach (var outcome in outcomes.Take(8))
                    {
                        var text = new TextBlock
                        {
                            Text = $"{outcome.Recommendation}\n{outcome.Authority} · {outcome.Confidence}% confidence · {outcome.Decision} → {outcome.Outcome}" +
                                   (string.IsNullOrWhiteSpace(outcome.OutcomeEvidence) ? "" : $"\n{outcome.OutcomeEvidence}"),
                            TextWrapping = TextWrapping.Wrap
                        };
                        var card = new Border { Child = text, Padding = new Thickness(12), CornerRadius = new CornerRadius(10), Margin = new Thickness(0,0,0,8) };
                        card.SetResourceReference(Border.BackgroundProperty, "NekoSurfaceAltBrush");
                        panel.Children.Add(card);
                    }
                    panel.Children.Add(new Separator { Margin = new Thickness(0,8,0,18) });
                }
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
                        approve.Click += async (_, _) =>
                        {
                            try { await _services.GetRequiredService<IDecisionLearningStore>().SetPreferenceAsync(new() { ObjectiveKey = key, Until = DateTimeOffset.Now.AddDays(7) },Environment.UserName); state=await ReadDecisionLearningAsync(); Render(); }
                            catch (Exception ex) { MessageBox.Show("Could not save preference: " + ex.Message); }
                        };
                        buttons.Children.Add(approve);
                    }
                    var forget = new Button { Content = "FORGET THIS OBJECTIVE'S FEEDBACK" };
                    forget.Click += async (_, _) =>
                    {
                        try { await _services.GetRequiredService<IDecisionLearningStore>().ForgetAsync(key); state=await ReadDecisionLearningAsync(); Render(); }
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
