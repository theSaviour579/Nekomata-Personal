using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Integrations.MicrosoftGraph.Mail;

namespace Nekomata.UI.ViewModels;
public partial class MainViewModel
{
    private HashSet<string> _meetingBriefsShown=[];
    private bool _meetingBriefsLoaded;
    private async Task CheckPersonalMeetingBriefAsync()
    {
        if(!_meetingBriefsLoaded)
        {
            var path=PersonalFile("meeting-brief-prompts.json");
            if(File.Exists(path))_meetingBriefsShown=JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(path))??[];
            _meetingBriefsLoaded=true;
        }
        var now=DateTimeOffset.Now;
        if(!CalendarLoaded||CalendarBusy||!CanPresentRoutinePrompt(GuardianPromptKind.MeetingBrief))return;
        var meeting=CalendarEvents.Where(x=>MeetingPreparationPolicy.IsDue(x,now)&&!_meetingBriefsShown.Contains(MeetingPreparationPolicy.Key(x))).OrderBy(x=>x.Start).FirstOrDefault();
        if(meeting==null)return;
        var lines=new List<string>{$"{meeting.Subject}\n{meeting.Start.LocalDateTime:g}–{meeting.End.LocalDateTime:t}\nParticipants: {string.Join(", ",meeting.Attendees)}",meeting.BodyPreview};
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var emails=await _services.GetRequiredService<IEmailService>().SearchInboxAndSentAsync(meeting.Subject,timeout.Token);
            lines.Add("POSSIBLE EMAIL CONTEXT · subject search, not confirmed links\n"+string.Join("\n\n",emails.Take(4).Select(x=>$"{x.Subject} · {x.SenderAddress} · {x.ReceivedAt.LocalDateTime:g}\n{x.BodyPreview}")));
        }catch(Exception ex){lines.Add("Email context unavailable: "+ex.Message);}
        lines.Add("SUGGESTED AGENDA\n• Agree the intended outcome.\n• Review outstanding decisions, owners and dates.\n• Capture outcomes in meeting notes afterwards. No outcomes have been inferred.");
        if(meeting.Start<=DateTimeOffset.Now||!CanPresentRoutinePrompt(GuardianPromptKind.MeetingBrief))return;
        var panel=new StackPanel{Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock{Text="YOUR MEETING BRIEF",FontSize=24,FontWeight=FontWeights.Bold});
        panel.Children.Add(new TextBlock{Text=string.Join("\n\n",lines),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,16)});
        var notesPath=PersonalFile("meeting-notes-"+MeetingBriefEvidence.Signature(meeting.Id,[meeting.Start.ToString("O")])+".json");
        var notes=new TextBox{Text=File.Exists(notesPath)?JsonSerializer.Deserialize<string>(File.ReadAllText(notesPath))??"":"",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=140,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="YOUR NOTES · saved locally, not sent",Margin=new Thickness(0,8,0,8)});panel.Children.Add(notes);
        var ready=new Button{Content="SAVE NOTES / READY",Margin=new Thickness(0,12,0,0)};panel.Children.Add(ready);
        var window=new Window{Title="Personal · meeting brief",Width=800,Height=720,ShowActivated=false,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        window.SetResourceReference(Control.BackgroundProperty,"NekoBackgroundBrush");window.SetResourceReference(Control.ForegroundProperty,"NekoTextBrush");
        ready.Click+=(_,_)=>{try{SavePersonalJson(notesPath,notes.Text);window.Close();}catch(Exception ex){MessageBox.Show(ex.Message);}};
        window.Closing+=(_,e)=>{try{SavePersonalJson(notesPath,notes.Text);}catch(Exception ex){e.Cancel=true;MessageBox.Show(ex.Message);}};
        _meetingBriefsShown.Add(MeetingPreparationPolicy.Key(meeting));SavePersonalJson(PersonalFile("meeting-brief-prompts.json"),_meetingBriefsShown);
        _meetingPreparationWindow=window;window.Closed+=(_,_)=>{_meetingPreparationWindow=null;RoutinePromptClosed();};window.Show();
    }
    [RelayCommand] private async Task ShowPersonalMeetingBriefAsync()
    {
        try
        {
            var now=DateTimeOffset.Now;foreach(var item in CalendarEvents.Where(x=>MeetingPreparationPolicy.IsDue(x,now)))_meetingBriefsShown.Remove(MeetingPreparationPolicy.Key(item));
            await CheckPersonalMeetingBriefAsync();
            if(_meetingPreparationWindow==null)MessageBox.Show("No eligible meeting in the next 15 minutes, or another prompt/focus session is active. Your existing Meeting Analyzer remains available for notes and transcripts.");
        }catch(Exception ex){MessageBox.Show(ex.Message);}
    }
}
