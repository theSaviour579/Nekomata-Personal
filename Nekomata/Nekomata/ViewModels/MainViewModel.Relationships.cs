using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Relationships;
using Nekomata.Models.Guardian;
using System.Windows;
using System.Windows.Controls;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task ShowRelationshipInsightsAsync()
    {
        var intelligence = _services.GetRequiredService<GuardianRelationshipIntelligenceService>();
        var window = new Window
        {
            Title="Guardian · relationship intelligence",Width=860,Height=650,
            Owner=Application.Current.MainWindow,WindowStartupLocation=WindowStartupLocation.CenterOwner
        };
        window.SetResourceReference(Control.BackgroundProperty,"NekoBackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty,"NekoTextBrush");

        var root = new Grid { Margin=new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text="RELATIONSHIP INTELLIGENCE",FontSize=25,FontWeight=FontWeights.Bold });
        heading.Children.Add(new TextBlock
        {
            Text="Evidence-based response patterns from Outlook and Teams. Message content is not stored. Excluded contacts cannot influence Guardian decisions.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,7,0,18)
        });
        if (_relationshipDelayInsights.Count > 0)
            heading.Children.Add(new TextBlock
            {
                Text="UNUSUAL RESPONSE DELAYS\n"+string.Join("\n",_relationshipDelayInsights.Take(5)
                    .Select(x=>$"• {x.ContactName}: {x.Subject} · expected by {x.ExpectedBy.LocalDateTime:ddd dd MMM HH:mm} ({x.Confidence}% confidence)")),
                TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18),Foreground=System.Windows.Media.Brushes.Goldenrod
            });
        root.Children.Add(heading);

        var body = new Grid(); Grid.SetRow(body,1);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(0.42,GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(0.58,GridUnitType.Star) });
        var list = new ListBox { DisplayMemberPath=nameof(GuardianRelationshipProfile.DisplayLabel) };
        list.SetResourceReference(Control.BackgroundProperty,"NekoSurfaceBrush");
        list.SetResourceReference(Control.ForegroundProperty,"NekoTextBrush");
        body.Children.Add(list);
        var detail = new TextBox
        {
            IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(16)
        };
        detail.SetResourceReference(Control.BackgroundProperty,"NekoSurfaceAltBrush");
        detail.SetResourceReference(Control.ForegroundProperty,"NekoTextBrush");
        Grid.SetColumn(detail,2); body.Children.Add(detail); root.Children.Add(body);

        var controls = new Grid { Margin=new Thickness(0,16,0,0) }; Grid.SetRow(controls,2);
        controls.ColumnDefinitions.Add(new ColumnDefinition());
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var note = new TextBox { MinWidth=300,Margin=new Thickness(0,0,10,0),ToolTip="Correction or exclusion note" };
        controls.Children.Add(note);
        var saveNote = new Button { Content="SAVE NOTE",Margin=new Thickness(0,0,10,0) }; Grid.SetColumn(saveNote,1); controls.Children.Add(saveNote);
        var exclude = new Button { Content="EXCLUDE FROM LEARNING" }; Grid.SetColumn(exclude,2); controls.Children.Add(exclude);
        root.Children.Add(controls); window.Content=root;

        void ShowProfile(GuardianRelationshipProfile? profile)
        {
            if (profile is null) { detail.Text="Select a contact to review the evidence behind Guardian's learned pattern."; return; }
            note.Text=profile.PreferenceNote;
            exclude.Content=profile.IsExcluded ? "INCLUDE IN LEARNING" : "EXCLUDE FROM LEARNING";
            var replyRate=profile.SubstantiveReplyCount==0?0:(int)Math.Round(profile.ResponseCount*100d/profile.SubstantiveReplyCount);
            detail.Text=$"{profile.ContactName}\n{profile.ContactAddress}\nChannel: {profile.Channel}\n\n{profile.EvidenceSummary}\n\n"+
                        $"Conversations: {profile.ConversationCount}\nMessages observed: {profile.MessageCount}\nSubstantive replies: {profile.SubstantiveReplyCount}\n"+
                        $"Acknowledgements: {profile.AcknowledgementCount}\nMeasured responses: {profile.ResponseCount} ({replyRate}% of substantive replies)\n"+
                        $"Responses after a chase: {profile.ChaseResponseCount}\nPromise signals: {profile.PromiseCount}\nLast observed: {profile.LastMessageAt?.LocalDateTime:g}\n\n"+
                        (profile.IsExcluded?"EXCLUDED — this profile is visible for review but cannot influence timing or drafts.":"ACTIVE — established evidence may influence timing and draft length.");
        }
        async Task ReloadAsync(string? selectedKey=null)
        {
            var profiles=await intelligence.GetProfilesAsync(); list.ItemsSource=profiles;
            list.SelectedItem=profiles.FirstOrDefault(x=>x.ContactAddress.Equals(selectedKey,StringComparison.OrdinalIgnoreCase))??profiles.FirstOrDefault();
            ShowProfile(list.SelectedItem as GuardianRelationshipProfile);
        }
        list.SelectionChanged+=(_,_)=>ShowProfile(list.SelectedItem as GuardianRelationshipProfile);
        saveNote.Click+=async (_,_)=>
        {
            if(list.SelectedItem is not GuardianRelationshipProfile profile)return;
            await intelligence.SetExcludedAsync(profile,profile.IsExcluded,note.Text.Trim());
            await ReloadAsync(profile.ContactAddress);
        };
        exclude.Click+=async (_,_)=>
        {
            if(list.SelectedItem is not GuardianRelationshipProfile profile)return;
            await intelligence.SetExcludedAsync(profile,!profile.IsExcluded,note.Text.Trim());
            await ReloadAsync(profile.ContactAddress);
        };
        await ReloadAsync(); window.Show();
    }
}
