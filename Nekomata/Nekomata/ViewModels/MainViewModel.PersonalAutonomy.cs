using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Models.Guardian;
using Nekomata.Models.Planning;
using Nekomata.Data.Repositories;

namespace Nekomata.UI.ViewModels;
public partial class MainViewModel
{
    private Window? _preflightWindow;
    private Window? _meetingPreparationWindow;
    private DateTimeOffset _quietUntil;
    private DateTimeOffset _pauseUntil;
    private bool _pauseLoaded;
    private bool _personalAutonomyBusy;
    private static string PersonalFile(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", name);
    private sealed class PersonalChecklist
    {
        public DateTime Day { get; set; }
        public bool Presented { get; set; }
        public Dictionary<string,string> Status { get; set; } = [];
    }
    private PersonalChecklist? _checklist;
    private void SaveChecklist() => SavePersonalJson(PersonalFile("preflight.json"), _checklist);
    private static void SavePersonalJson<T>(string path,T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(value)); File.Move(path+".tmp",path,true);
    }
    private void LoadChecklist()
    {
        if (_checklist?.Day == DateTime.Today) return;
        var path=PersonalFile("preflight.json");
        _checklist=File.Exists(path)?JsonSerializer.Deserialize<PersonalChecklist>(File.ReadAllText(path)):null;
        if (_checklist?.Day != DateTime.Today) _checklist=new(){Day=DateTime.Today};
    }
    private bool CanPresentRoutinePrompt(GuardianPromptKind kind)
    {
        if (!_pauseLoaded)
        {
            var path=PersonalFile("routine-prompt-pause.txt");
            if(File.Exists(path)) DateTimeOffset.TryParse(File.ReadAllText(path),out _pauseUntil);
            _pauseLoaded=true;
        }
        var now=DateTimeOffset.Now;
        var occupied=IsInitialLoading || MissionActive || _preflightWindow!=null || _meetingPreparationWindow!=null || _personalFollowupWindow!=null || _nextActionWindow!=null || DayReviewVisible || _dayReviewLoading ||
            CalendarBusy || !CalendarLoaded || CalendarEvents.Any(x=>x.Start<=now && x.End>now);
        var urgent=VisibleAttentionItems.Any(x=>x.Severity is "High" or "Critical");
        var waiting=new List<GuardianPromptKind>();
        if(CalendarEvents.Any(x=>MeetingPreparationPolicy.IsDue(x,now) && !_meetingBriefsShown.Contains(MeetingPreparationPolicy.Key(x)))) waiting.Add(GuardianPromptKind.MeetingBrief);
        if(_checklist is {Presented:false} && now.LocalDateTime.Hour<12) waiting.Add(GuardianPromptKind.Preflight);
        return GuardianPromptPolicy.CanPresent(kind,waiting,occupied,urgent,now,_pauseUntil,_quietUntil);
    }
    private void RoutinePromptClosed()=>_quietUntil=DateTimeOffset.Now.AddMinutes(2);
    [RelayCommand] private void ResumeRoutinePrompts()
    {
        try
        {
            _pauseUntil=DateTimeOffset.MinValue; _pauseLoaded=true;
            Directory.CreateDirectory(Path.GetDirectoryName(PersonalFile("routine-prompt-pause.txt"))!);
            File.WriteAllText(PersonalFile("routine-prompt-pause.txt"),_pauseUntil.ToString("O"));
            MessageBox.Show("Routine prompts resumed. Focus, meeting and quiet-time safeguards still apply.");
        }catch(Exception ex){MessageBox.Show(ex.Message);}
    }
    partial void OnDayReviewVisibleChanged(bool value){if(!value) RoutinePromptClosed();}
    [RelayCommand] private void PauseRoutinePrompts()
    {
        try
        {
            _pauseUntil=DateTimeOffset.Now.AddMinutes(15); _pauseLoaded=true;
            Directory.CreateDirectory(Path.GetDirectoryName(PersonalFile("routine-prompt-pause.txt"))!);
            File.WriteAllText(PersonalFile("routine-prompt-pause.txt"),_pauseUntil.ToString("O"));
            MessageBox.Show($"Routine prompts paused until {_pauseUntil.LocalDateTime:HH:mm}. Urgent attention and background checks remain available.");
        }catch(Exception ex){MessageBox.Show(ex.Message);}
    }
    private async Task CheckPersonalAutonomyAsync()
    {
        if(_personalAutonomyBusy || IsInitialLoading) return;
        _personalAutonomyBusy=true;
        try
        {
            LoadChecklist();
            await ReconcilePersonalWorkAsync();
            await CheckPersonalMeetingBriefAsync();
            var now=DateTime.Now; var hours=_services.GetRequiredService<WorkingDaySettings>();
            if(now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return;
            if(now>=hours.GetStart(now)) await TrySendDailyBriefingEmailAsync();
            if(now<hours.GetStart(now)) return;
            EvaluateEndOfDayReview();
            if(now>=hours.GetEnd(now)) return;
            if(!_checklist!.Presented && now.Hour<12 && CanPresentRoutinePrompt(GuardianPromptKind.Preflight)) ShowMorningPreflight();
            CheckNextAction();
            await CheckPersonalFollowupsAsync();
        }catch(Exception ex){System.Diagnostics.Debug.WriteLine("Personal autonomy unavailable: "+ex);}
        finally{_personalAutonomyBusy=false;}
    }
    private List<WrapUpObjective> PersonalCarryForward()
    {
        var folder=Path.GetDirectoryName(WrapUpPath(DateTime.Today))!; var latest=new Dictionary<string,WrapUpObjective>();
        if(!Directory.Exists(folder)) return [];
        foreach(var path in Directory.EnumerateFiles(folder,"*.json").OrderBy(x=>x))
        {
            if(!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var day)||day>=DateTime.Today)continue;
            foreach(var objective in JsonSerializer.Deserialize<WrapUpDraft>(File.ReadAllText(path))?.Objectives??[])
                if(objective.Decision!="No change")latest[objective.Key]=objective;
        }
        return latest.Values.Where(x=>WrapUpPlanning.IsDue(x,DateTime.Today)).ToList();
    }
    [RelayCommand] private void ShowMorningPreflight()
    {
        try
        {
            LoadChecklist(); if(_preflightWindow!=null){_preflightWindow.Activate();return;}
            var panel=new StackPanel{Margin=new Thickness(24)};
            var window=new Window{Title="Personal · morning pre-flight",Width=850,Height=720,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
            window.SetResourceReference(Control.BackgroundProperty,"NekoBackgroundBrush");window.SetResourceReference(Control.ForegroundProperty,"NekoTextBrush");
            var steps=new List<(string Key,string Title)>{("email","Review replies and requests"),("calendar","Check today's calendar"),("objective","Choose your first objective")};
            steps.AddRange(PersonalCarryForward().Select(x=>("carry:"+x.Key,x.Title)));
            steps.AddRange(VisibleAttentionItems.Take(5).Select(x=>(x.Key,x.Title)));
            void Render()
            {
                panel.Children.Clear();
                panel.Children.Add(new TextBlock{Text=$"YOUR ROUTE INTO THE DAY · {steps.Count(x=>_checklist!.Status.GetValueOrDefault(x.Key,"").StartsWith("Done"))}/{steps.Count} complete",FontSize=24,TextWrapping=TextWrapping.Wrap});
                for(var d=1;d<=7;d++)
                {
                    var path=WrapUpPath(DateTime.Today.AddDays(-d));if(!File.Exists(path))continue;
                    var draft=JsonSerializer.Deserialize<WrapUpDraft>(File.ReadAllText(path));
                    if(!string.IsNullOrWhiteSpace(draft?.Notes))panel.Children.Add(new TextBlock{Text="Previous handover: "+draft.Notes,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});break;
                }
                foreach(var step in steps)
                {
                    var stack=new StackPanel();
                    stack.Children.Add(new TextBlock{Text=step.Title+" · "+_checklist!.Status.GetValueOrDefault(step.Key,"Pending"),TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.Bold});
                    var actions=new WrapPanel{Margin=new Thickness(0,10,0,0)};stack.Children.Add(actions);
                    foreach(var label in new[]{"OPEN","DONE","DEFER TODAY","RESET"})
                    {
                        var button=new Button{Content=label,Margin=new Thickness(0,0,8,0)};actions.Children.Add(button);
                        button.Click+=async(_,_)=>
                        {
                            try{
                                if(label=="OPEN")
                                {
                                    if(step.Key=="email")await ShowEmailAsync();else if(step.Key=="calendar")await ShowCalendarAsync();
                                    else if(step.Key=="objective"||step.Key.StartsWith("carry:")){ShowDashboard();ChatInput="Review my next step for: "+step.Title;GuardianPanelExpanded=true;}
                                    else {var item=AttentionItems.FirstOrDefault(x=>x.Key==step.Key);if(item!=null)await ActAttentionItemAsync(item);}
                                    window.Close();return;
                                }
                                _checklist.Status[step.Key]=label=="DONE"?"Done":label=="RESET"?"Pending":"Deferred today";SaveChecklist();Render();
                            }catch(Exception ex){MessageBox.Show(ex.Message);}
                        };
                    }
                    var card=new Border{Child=stack,Padding=new Thickness(18),Margin=new Thickness(0,12,0,0),CornerRadius=new CornerRadius(12)};
                    card.SetResourceReference(Border.BackgroundProperty,"NekoSurfaceAltBrush");panel.Children.Add(card);
                }
            }
            _checklist!.Presented=true;SaveChecklist();Render();_preflightWindow=window;
            window.Closed+=(_,_)=>{_preflightWindow=null;RoutinePromptClosed();};window.Show();
        }catch(Exception ex){MessageBox.Show("Pre-flight unavailable: "+ex.Message);}
    }
    private async Task<GuardianDayReview> BuildPersonalDayReviewAsync()
    {
        var sessions=await _missionSessionRepository.GetTodayAsync();var completed=sessions.Where(x=>x.Completed).ToList();
        var open=WrapUpPlanning.Build(Workspace.Tasks,DateTime.Today);
        return new(){ObjectivesPlanned=sessions.Count,ObjectivesCompleted=completed.Count,CompletedMinutes=completed.Sum(x=>x.ActualDurationMinutes),PlannedMinutes=sessions.Sum(x=>x.EstimatedDurationMinutes),UnplannedMinutes=completed.Where(x=>x.SourceType=="AdHoc").Sum(x=>x.ActualDurationMinutes),RollForwardCount=open.Count,TomorrowSummary=$"Review {open.Count} unfinished scheduled/due objectives. Your choices feed the next pre-flight.",LearnedSummary="Only recorded sessions are counted. Review learning preferences separately; interruption metrics are not yet tracked in Personal."};
    }
    private async Task ReconcilePersonalWorkAsync()
    {
        foreach(var objective in PersonalCarryForward())
        {
            if(objective.TaskId is not { } id)continue;
            var task=await _taskRepository.GetByIdAsync(id);
            if(task==null||(!task.Completed&&!WorkReconciliation.IsCompletedStatus(task.Status)))continue;
            _checklist!.Status["carry:"+objective.Key]="Done automatically";
            await _services.GetRequiredService<WorkdayJournalService>().RecordIfAbsentAsync(new(){EventType="WorkReconciled",ExternalId="task:"+id,Title=task.Title,Narrative="Source task confirmed complete; pre-flight updated. No worked time inferred."});
        }
        SaveChecklist();
        var today=await _services.GetRequiredService<IWorkdayEventRepository>().GetBetweenAsync(new(DateTime.Today),new(DateTime.Today.AddDays(1)));
        ReconciledWorkSummary=string.Join("\n",today.Where(x=>x.EventType=="WorkReconciled").Select(x=>x.Title+": "+x.Narrative));
        OnPropertyChanged(nameof(ReconciledWorkSummary));
    }
    public string ReconciledWorkSummary {get;private set;}="No automatic reconciliation recorded today.";
}
