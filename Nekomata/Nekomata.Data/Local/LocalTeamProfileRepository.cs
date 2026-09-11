using Nekomata.Data.Repositories;
using Nekomata.Models.People;

namespace Nekomata.Data.Local;

public sealed class LocalTeamProfileRepository(LocalWorkspaceStore store) : ITeamProfileRepository
{
    public Task<IReadOnlyList<TeamMemberProfile>> GetAllAsync() => store.ReadAsync<IReadOnlyList<TeamMemberProfile>>(data =>
        data.TeamMembers.OrderByDescending(x => x.Active).ThenBy(x => x.Name).Select(Clone).ToList());

    public Task<long> SaveAsync(TeamMemberProfile profile) => store.UpdateAsync(data =>
    {
        if (profile.Id <= 0) profile.Id = data.TeamMembers.Count == 0 ? 1 : data.TeamMembers.Max(x => x.Id) + 1;
        profile.CapacityPercent = Math.Clamp(profile.CapacityPercent, 0, 100);
        var index = data.TeamMembers.FindIndex(x => x.Id == profile.Id);
        if (index < 0) data.TeamMembers.Add(Clone(profile)); else data.TeamMembers[index] = Clone(profile);
        return profile.Id;
    });

    private static TeamMemberProfile Clone(TeamMemberProfile value) => new()
    {
        Id = value.Id, ExternalId = value.ExternalId, Name = value.Name, Email = value.Email, Role = value.Role,
        Source = value.Source, LastSyncedAt = value.LastSyncedAt, Active = value.Active,
        AvailabilityStatus = value.AvailabilityStatus, UnavailableUntil = value.UnavailableUntil,
        CapacityPercent = value.CapacityPercent, Responsibilities = value.Responsibilities, Exclusions = value.Exclusions,
        Skills = value.Skills.Select(x => new TeamMemberSkill { SkillId = x.SkillId, Name = x.Name, Proficiency = x.Proficiency, IsPrimary = x.IsPrimary, Aliases = x.Aliases }).ToList()
    };
}
