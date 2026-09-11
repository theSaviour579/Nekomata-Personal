using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.People;
using Nekomata.Models.People;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

public partial class TeamSkillsViewModel(ITeamProfileRepository repository, IMicrosoftPeopleService people, TeamRoleSkillService roleSkills) : ObservableObject
{
    public ObservableCollection<TeamMemberProfile> Members { get; } = [];
    public ObservableCollection<TeamMemberSkill> Skills { get; } = [];
    public string[] AvailabilityOptions { get; } = ["Available", "Limited", "Unavailable", "Leave"];
    [ObservableProperty] private TeamMemberProfile? selectedMember;
    [ObservableProperty] private TeamMemberSkill? selectedSkill;
    [ObservableProperty] private string status = "Sync your direct reports from Microsoft 365 or add a profile manually.";
    [ObservableProperty] private bool busy;

    public async Task LoadAsync()
    {
        Members.Clear();
        foreach (var member in await repository.GetAllAsync()) Members.Add(member);
        SelectedMember = Members.FirstOrDefault();
    }

    partial void OnSelectedMemberChanged(TeamMemberProfile? value)
    {
        Skills.Clear();
        if (value is not null) foreach (var skill in value.Skills) Skills.Add(skill);
    }

    [RelayCommand]
    private void AddMember()
    {
        var member = new TeamMemberProfile { Name = "New team member" };
        Members.Add(member); SelectedMember = member;
    }

    [RelayCommand] private void AddSkill() => Skills.Add(new TeamMemberSkill { Name = "New skill", Proficiency = 2 });
    [RelayCommand] private void RemoveSkill() { if (SelectedSkill is not null) Skills.Remove(SelectedSkill); }

    [RelayCommand]
    private async Task SyncMicrosoft365Async()
    {
        if (Busy) return;
        Busy = true; Status = "Reading your direct reports and their job titles from Microsoft 365…";
        try
        {
            var discovered = await people.GetDirectReportsAsync();
            var saved = (await repository.GetAllAsync()).ToList();
            var now = DateTimeOffset.Now;
            var added = 0;
            foreach (var person in discovered)
            {
                var member = saved.FirstOrDefault(x => person.Id.Length > 0 && x.ExternalId.Equals(person.Id, StringComparison.OrdinalIgnoreCase))
                    ?? saved.FirstOrDefault(x => person.Email.Length > 0 && x.Email.Equals(person.Email, StringComparison.OrdinalIgnoreCase));
                if (member is null) { member = new TeamMemberProfile(); saved.Add(member); added++; }
                member.ExternalId = person.Id; member.Name = person.DisplayName; member.Email = person.Email;
                if (!string.IsNullOrWhiteSpace(person.JobTitle)) member.Role = person.JobTitle;
                member.Source = "Microsoft 365"; member.LastSyncedAt = now;
                await repository.SaveAsync(member);
            }
            var directReportKeys = discovered.Select(x => x.Id).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var directReportEmails = discovered.Select(x => x.Email).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in saved.Where(x => x.Source == "Microsoft 365" &&
                         !directReportKeys.Contains(x.ExternalId) && !directReportEmails.Contains(x.Email)).ToList())
                await repository.DeleteAsync(stale.Id);
            await LoadAsync();
            Status = discovered.Count == 0
                ? "Microsoft 365 returned no direct reports. Previously imported people were removed; manual profiles remain."
                : $"Microsoft 365 sync complete · {discovered.Count} direct report(s) · {added} new profile(s). Skills and availability remain editable.";
        }
        catch (Exception ex) { Status = "Microsoft 365 team sync could not complete: " + ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task SuggestRoleSkillsAsync()
    {
        if (SelectedMember is null || Busy) return;
        Busy = true; Status = $"Suggesting skills for {SelectedMember.Role}…";
        try
        {
            foreach (var skill in await roleSkills.SuggestAsync(SelectedMember.Role))
                if (!Skills.Any(x => x.Name.Equals(skill.Name, StringComparison.OrdinalIgnoreCase))) Skills.Add(skill);
            Status = "Role-based suggestions added for review. Edit or remove them before saving.";
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedMember is null || string.IsNullOrWhiteSpace(SelectedMember.Name)) return;
        SelectedMember.Skills = Skills.ToList();
        await repository.SaveAsync(SelectedMember);
        Status = $"Saved {SelectedMember.Name}. Proficiency: 1 Awareness · 2 Working · 3 Advanced · 4 Expert.";
    }
}
