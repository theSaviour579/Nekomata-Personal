using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Mapping;
using Nekomata.Core.Personalization;
using Xunit;

namespace Nekomata.Tests;

public sealed class PersonalIdentityTests
{
    [Fact]
    public void GuardianTaskMapper_UsesConfiguredDisplayName()
    {
        var mapper = new GuardianTaskMapper(new TestIdentity("Alex"));

        var task = mapper.Map(new ProposedTask { Title = "Plan the week" }, null);

        Assert.Equal("Alex", task.Owner);
    }

    private sealed class TestIdentity(string displayName) : IUserIdentity
    {
        public string DisplayName { get; } = displayName;
        public string FirstName => DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        public string Description => DisplayName;
        public bool Is(string? nameOrAddress) => !string.IsNullOrWhiteSpace(nameOrAddress) &&
            nameOrAddress.Equals(DisplayName, StringComparison.OrdinalIgnoreCase);
    }
}
