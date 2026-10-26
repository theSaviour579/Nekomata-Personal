using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Models.Planning;
using Nekomata.Core.Planning;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private sealed class PreflightStep
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Status { get; set; } = "Pending";
        public string ObservedMessage { get; set; } = "";
        public Guid? CorrectionId { get; set; }
        public RoleCorrectionType? CorrectionType { get; set; }
    }
    private sealed class PreflightDay
    {
        public DateTime Day { get; set; }
        public bool Presented { get; set; }
        public bool AttentionSnapshotComplete { get; set; }
        public List<PreflightStep> Steps { get; set; } = [];
    }
    private PreflightDay? _preflightDay;
    private Window? _preflightWindow;
    private Action? _refreshPreflight;
    private Action? _refreshPreflightStatus;
    private static string PreflightPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "morning-preflight.json");

    private void SavePreflight()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreflightPath)!);
        File.WriteAllText(PreflightPath + ".tmp", JsonSerializer.Serialize(_preflightDay));
        File.Move(PreflightPath + ".tmp", PreflightPath, true);
    }

    private void LoadPreflight()
    {
        if (_preflightDay?.Day == DateTime.Today) return;
        _preflightWindow?.Close();
        _preflightDay = File.Exists(PreflightPath) ? JsonSerializer.Deserialize<PreflightDay>(File.ReadAllText(PreflightPath)) : null;
        if (_preflightDay?.Day != DateTime.Today) _preflightDay = new() { Day = DateTime.Today };
    }

    private bool PreflightDataReady => CalendarLoaded && !CalendarBusy;

    private string PreflightSourceStatus
    {
        get
        {
            var calendar = CalendarBusy ? "Calendar: refreshing (available data shown)" : CalendarLoaded ? "Calendar: loaded" : "Calendar: not loaded yet";
            return calendar + (!PreflightDataReady ? "\nYou can continue. The checklist may be incomplete while sources are unavailable or refreshing." : "");
        }
    }

    private static string PreviousWrapUpHandover()
    {
        try
        {
            for (var days = 1; days <= 7; days++)
            {
                var date = DateTime.Today.AddDays(-days);
                var path = WrapUpPath(date);
                if (!File.Exists(path)) continue;
                var draft = JsonSerializer.Deserialize<WrapUpDraft>(File.ReadAllText(path));
                return string.IsNullOrWhiteSpace(draft?.Notes) ? "" :
                    $"\n\nHandover from {date:ddd dd MMM} ({(draft.Completed ? "completed" : "draft")}):\n{draft.Notes}";
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Wrap-up handover unavailable: " + ex); }
        return "";
    }

    private void AddAvailablePreflightAttention()
    {
        var day = _preflightDay!;
        var attention = VisibleAttentionItems.ToList();
        var insertAt = 0;
        foreach (var item in attention.Take(8))
            if (!day.Steps.Any(x => x.Id == item.Key))
                day.Steps.Insert(insertAt++, new() { Id = item.Key, Title = item.Title });
        if (attention.Count > 8 && !day.Steps.Any(x => x.Id == "attention"))
            day.Steps.Add(new() { Id = "attention", Title = "Review remaining attention items" });
        day.AttentionSnapshotComplete = PreflightDataReady;
    }

    private void AddWrapUpCarryForward()
    {
        var folder = Path.GetDirectoryName(WrapUpPath(DateTime.Today))!;
        if (!Directory.Exists(folder)) return;
        var latest = new Dictionary<string, Nekomata.Models.Guardian.WrapUpObjective>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json").OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) || date >= DateTime.Today) continue;
            var draft = JsonSerializer.Deserialize<WrapUpDraft>(File.ReadAllText(path));
            foreach (var objective in draft?.Objectives ?? [])
                if (objective.Decision != "No change") latest[objective.Key] = objective;
        }
        foreach (var objective in latest.Values)
        {
            var id = "carry:" + objective.Key;
            var task = Workspace.Tasks.FirstOrDefault(x => x.Id == objective.TaskId);
            var eligible = Nekomata.Core.Guardian.Outcomes.WrapUpPlanning.IsDue(objective, DateTime.Today) &&
                task?.IsCompleted != true && task?.Completed != true;
            if (!eligible)
            {
                // Keep existing completed-task steps for evidence reconciliation rather than silently deleting them.
                if (task?.IsCompleted != true && task?.Completed != true) _preflightDay!.Steps.RemoveAll(x => x.Id == id);
                continue;
            }
            if (!_preflightDay!.Steps.Any(x => x.Id == id))
                _preflightDay.Steps.Add(new() { Id = id, Title = "Carry forward · " + (task?.Title ?? objective.Title) });
        }
    }

    private void AddRolePlanningSteps()
    {
        if (RolePlanning is null) return;
        foreach (var action in RolePlanning.CorrectivePlan?.RankedActions ?? [])
            AddRoleStep(action);
    }

    private void AddRoleStep(RoleCorrectiveAction action)
    {
        var id = "role:" + action.Id;
        if (_preflightDay!.Steps.Any(step => step.Id == id)) return;
        var objectiveIndex = _preflightDay.Steps.FindIndex(step => step.Id == "objective");
        var slot = action.SuggestedBlock is null ? "" :
            $" · suggested {action.SuggestedBlock.Start.LocalDateTime:ddd HH:mm} ({action.SuggestedBlock.Label.ToLowerInvariant()} interruption risk)";
        var step = new PreflightStep
        {
            Id = id, Title = action.Title + slot, CorrectionId = action.Id, CorrectionType = action.Type
        };
        if (objectiveIndex < 0) _preflightDay.Steps.Add(step); else _preflightDay.Steps.Insert(objectiveIndex, step);
    }

    private void CheckMorningPreflight()
    {
        try
        {
            LoadPreflight();
            var changed = false;
            if (_preflightDay!.Presented && !_preflightDay.AttentionSnapshotComplete && PreflightDataReady)
            {
                AddAvailablePreflightAttention();
                changed = true;
            }
            foreach (var step in _preflightDay!.Steps.Where(x => x.Status == "Pending"))
            {
                // Only an active focus session counts as automatic completion.
                if (step.Id == "objective" && MissionActive)
                { step.Status = "Done automatically"; changed = true; }
            }
            if (changed) { SavePreflight(); _refreshPreflight?.Invoke(); }
            _refreshPreflightStatus?.Invoke();
            var now = DateTime.Now;
            var hours = _services.GetRequiredService<WorkingDaySettings>();
            if (!_preflightDay.Presented && PreflightDataReady && !MissionActive &&
                now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
                now >= hours.GetStart(now) && now < hours.GetEnd(now) && now.Hour < 12 &&
                !CalendarEvents.Any(x => x.Start <= DateTimeOffset.Now && x.End > DateTimeOffset.Now))
                if (CanPresentRoutinePrompt(Nekomata.Core.Guardian.Anticipation.GuardianPromptKind.Preflight)) ShowMorningPreflight();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Pre-flight check failed: " + ex); }
    }

    [RelayCommand]
    private void ShowMorningPreflight()
    {
        try
        {
            LoadPreflight();
            if (_preflightWindow != null) { _preflightWindow.Activate(); return; }
            if (_preflightDay!.Steps.Count == 0)
            {
                AddAvailablePreflightAttention();
                _preflightDay.Steps.Add(new() { Id = "email", Title = "Review inbox for replies and requests" });
                _preflightDay.Steps.Add(new() { Id = "calendar", Title = "Check today's calendar and preparation" });
                _preflightDay.Steps.Add(new() { Id = "objective", Title = "Choose and start your first objective" });
            }
            AddWrapUpCarryForward();
            AddRolePlanningSteps();
            var panel = new StackPanel { Margin = new Thickness(28), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Center };
            var window = new Window { Title = $"Guardian · morning pre-flight · {DateTime.Today:ddd dd MMM}", Width = 1050, Height = 820, MinWidth = 620, MinHeight = 450,
                ShowActivated = false, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            window.SetResourceReference(Control.BackgroundProperty, "NekoBackgroundBrush");
            window.SetResourceReference(Control.ForegroundProperty, "NekoTextBrush");
            _preflightWindow = window;
            void Render()
            {
                panel.Children.Clear();
                var steps = _preflightDay.Steps;
                var complete = steps.Count(x => x.Status.StartsWith("Done"));
                var deferred = steps.Count(x => x.Status == "Deferred today");
                var next = steps.FirstOrDefault(x => x.Status == "Pending");
                var headerContent = new StackPanel();
                var eyebrow = new TextBlock { Text = $"GUARDIAN  /  {DateTime.Today:dddd dd MMMM}".ToUpperInvariant(), FontSize = 12, FontWeight = FontWeights.SemiBold };
                eyebrow.SetResourceReference(TextBlock.ForegroundProperty, "NekoAccentBrush");
                headerContent.Children.Add(eyebrow);
                headerContent.Children.Add(new TextBlock { Text = "Your route into the day", FontSize = 30, FontWeight = FontWeights.Bold, Margin = new Thickness(0,8,0,8), TextWrapping = TextWrapping.Wrap });
                headerContent.Children.Add(new TextBlock { Text = next == null ? "Pre-flight reviewed. You're ready to move on." : "Work through the essentials, then start your first objective.", TextWrapping = TextWrapping.Wrap });
                var progress = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, steps.Count), Value = complete, Height = 6, Margin = new Thickness(0,20,0,10) };
                progress.SetResourceReference(Control.ForegroundProperty, "NekoAccentBrush");
                progress.SetResourceReference(Control.BackgroundProperty, "NekoSurfaceBrush");
                headerContent.Children.Add(progress);
                headerContent.Children.Add(new TextBlock { Text = $"{complete} COMPLETE    ·    {steps.Count - complete - deferred} TO DO    ·    {deferred} DEFERRED", FontWeight = FontWeights.SemiBold, FontSize = 12 });
                var header = new Border { Child = headerContent, Padding = new Thickness(24), CornerRadius = new CornerRadius(16), Margin = new Thickness(0,0,0,16) };
                header.SetResourceReference(Border.BackgroundProperty, "NekoHeaderGradientBrush");
                panel.Children.Add(header);
                var handover = PreviousWrapUpHandover();
                if (!string.IsNullOrWhiteSpace(handover))
                {
                    var handoverCard = new Border { Child = new TextBlock { Text = handover.Trim(), TextWrapping = TextWrapping.Wrap }, Padding = new Thickness(20), CornerRadius = new CornerRadius(12), Margin = new Thickness(0,0,0,16) };
                    handoverCard.SetResourceReference(Border.BackgroundProperty, "NekoSurfaceAltBrush");
                    panel.Children.Add(handoverCard);
                }
                var sourceStatus = new TextBlock { Text = PreflightSourceStatus, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) };
                sourceStatus.SetResourceReference(TextBlock.ForegroundProperty, "NekoMutedTextBrush");
                var sources = new Expander { Header = PreflightDataReady ? "SOURCE STATUS · Available data loaded" : "SOURCE STATUS · Some data is refreshing or unavailable", Content = sourceStatus, Margin = new Thickness(4,0,0,18), IsExpanded = !PreflightDataReady };
                panel.Children.Add(sources);
                _refreshPreflightStatus = () => { sourceStatus.Text = PreflightSourceStatus; sources.Header = PreflightDataReady ? "SOURCE STATUS · Available data loaded" : "SOURCE STATUS · Some data is refreshing or unavailable"; };
                var stepNumber = 0;
                foreach (var step in steps)
                {
                    stepNumber++;
                    var done = step.Status.StartsWith("Done");
                    var cardContent = new Grid();
                    cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                    cardContent.ColumnDefinitions.Add(new ColumnDefinition());
                    var badgeText = new TextBlock { Text = done ? "✓" : stepNumber.ToString("00"), FontSize = 19, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    badgeText.SetResourceReference(TextBlock.ForegroundProperty, done ? "NekoSuccessBrush" : "NekoAccentBrush");
                    var badge = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(12), Child = badgeText, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
                    badge.SetResourceReference(Border.BackgroundProperty, "NekoSurfaceAltBrush");
                    cardContent.Children.Add(badge);
                    var body = new StackPanel();
                    Grid.SetColumn(body, 1);
                    cardContent.Children.Add(body);
                    var state = new TextBlock { Text = step == next ? "UP NEXT" : step.Status.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,5) };
                    state.SetResourceReference(TextBlock.ForegroundProperty, done ? "NekoSuccessBrush" : step == next ? "NekoAccentBrush" : "NekoMutedTextBrush");
                    body.Children.Add(state);
                    body.Children.Add(new TextBlock { Text = step.Title, FontSize = 16, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,12) });
                    var actions = new WrapPanel();
                    foreach (var label in step.Status == "Pending" ? new[] { "OPEN", "DONE", "DEFER TODAY" } : new[] { "OPEN", "RESET" })
                    {
                        var button = new Button { Content = label == "OPEN" ? (step == next ? "START HERE →" : "OPEN →") : label == "RESET" ? "UNDO" : label, Margin = new Thickness(0,0,8,0), Padding = new Thickness(14,7,14,7), FontSize = 11 };
                        if (label == "OPEN" && step == next) button.SetResourceReference(Control.BackgroundProperty, "NekoSpiritPurpleBrush");
                        else if (label != "OPEN") button.BorderThickness = new Thickness(0);
                        button.Click += async (_, _) =>
                        {
                            try
                            {
                                if (label == "OPEN")
                                {
                                    Application.Current.MainWindow?.Activate();
                                    if (step.CorrectionType is not null)
                                    {
                                        switch (step.CorrectionType)
                                        {
                                            case RoleCorrectionType.ReserveStrategicBlock:
                                                WorkspaceMode = Nekomata.Models.Workspace.WorkspaceMode.Calendar;
                                                ChatInput = "Prepare the recommended protected strategic block. Preserve fixed meetings and do not apply it without my approval.";
                                                GuardianPanelExpanded = true;
                                                break;
                                            case RoleCorrectionType.DelegateRecurringWork:
                                                ShowDashboard();
                                                ChatInput = "Show the two strongest recurring-work delegation candidates, proposed owners and evidence. Do not reassign anything without my approval.";
                                                GuardianPanelExpanded = true;
                                                break;
                                            default:
                                                await ShowCalendarAsync();
                                                break;
                                        }
                                        window.Close();
                                        return;
                                    }
                                    switch (step.Id)
                                    {
                                        case "email": await ShowEmailAsync(); break;
                                        case "calendar": await ShowCalendarAsync(); break;
                                        case "objective": ShowDashboard(); break;
                                        case "attention": ShowAttention(); break;
                                        case "role:strategic":
                                            WorkspaceMode = Nekomata.Models.Workspace.WorkspaceMode.Calendar;
                                            ChatInput = "Prepare the earliest valid 90-minute high-leverage focus block today, or on the next working day if today has no safe window, to correct the strategic role-alignment shortfall. Preserve fixed meetings and do not apply it without approval.";
                                            GuardianPanelExpanded = true;
                                            break;
                                        case "role:leadership": await ShowCalendarAsync(); break;
                                        case "role:delegate":
                                            ShowDashboard();
                                            ChatInput = "Show the technical and reactive work that is suitable for delegation, with the proposed owner and evidence. Do not reassign anything without approval.";
                                            GuardianPanelExpanded = true;
                                            break;
                                        default:
                                            if (step.Id.StartsWith("carry:", StringComparison.Ordinal))
                                            {
                                                ShowDashboard();
                                                ChatInput = "Review this carried-forward objective and suggest the next step: " + step.Title;
                                                GuardianPanelExpanded = true;
                                                break;
                                            }
                                            var item = AttentionItems.FirstOrDefault(x => x.Key == step.Id);
                                            if (item != null) await ActAttentionItemAsync(item); else ShowAttention();
                                            break;
                                    }
                                    window.Close();
                                }
                                else
                                {
                                    step.Status = label == "DONE" ? "Done" : label == "RESET" ? "Pending" : "Deferred today";
                                    if (step.CorrectionId is Guid correctionId)
                                    {
                                        var decision = label == "DONE" ? RoleCorrectionDecision.Approved :
                                            label == "RESET" ? RoleCorrectionDecision.Proposed : RoleCorrectionDecision.Rejected;
                                        await _services.GetRequiredService<CorrectionOutcomeEvaluator>().RecordDecisionAsync(
                                            correctionId, decision,
                                            label == "DONE" ? "User confirmed the corrective action occurred in morning pre-flight." :
                                            label == "RESET" ? "User reopened the corrective action in morning pre-flight." :
                                            "User deferred the corrective action for today.",
                                            occurred: label == "DONE");
                                        await RefreshRolePlanningAsync();
                                    }
                                    SavePreflight(); Render();
                                }
                            }
                            catch (Exception ex) { MessageBox.Show("Pre-flight action could not be completed: " + ex.Message); }
                        };
                        actions.Children.Add(button);
                    }
                    body.Children.Add(actions);
                    var card = new Border { Child = cardContent, Padding = new Thickness(18), Margin = new Thickness(0,0,0,10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
                    card.SetResourceReference(Border.BackgroundProperty, "NekoSurfaceBrush");
                    card.SetResourceReference(Border.BorderBrushProperty, step == next ? "NekoSpiritPurpleBrush" : "NekoBorderSoftBrush");
                    panel.Children.Add(card);
                }
                var footer = new TextBlock { Text = "Opening a screen doesn't complete a check. Your progress is saved for today; new urgent issues still prompt separately.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4,8,4,12) };
                footer.SetResourceReference(TextBlock.ForegroundProperty, "NekoMutedTextBrush");
                panel.Children.Add(footer);
            }
            _refreshPreflight = Render;
            window.Closed += (_, _) => { _preflightWindow = null; _refreshPreflight = null; _refreshPreflightStatus = null; RoutinePromptClosed(); };
            Render();
            window.Show();
            _preflightDay.Presented = true;
            SavePreflight();
        }
        catch (Exception ex) { MessageBox.Show("Could not open morning pre-flight: " + ex.Message); }
    }
}
