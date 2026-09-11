namespace Nekomata.Integrations.MicrosoftGraph.People;

public sealed record MicrosoftPerson(string Id, string DisplayName, string Email, string JobTitle);

public interface IMicrosoftPeopleService
{
    Task<IReadOnlyList<MicrosoftPerson>> GetRelevantPeopleAsync(CancellationToken cancellationToken = default);
}
