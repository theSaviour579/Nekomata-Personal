using System.Text.Json;
using Nekomata.AI.Privacy;
using Xunit;

namespace Nekomata.Tests;

[CollectionDefinition("PrivacyDirectory", DisableParallelization = true)]
public sealed class PrivacyDirectoryCollection;

[Collection("PrivacyDirectory")]
public sealed class PrivacyGuardTests : IDisposable
{
    public PrivacyGuardTests()
    {
        PrivacyDirectory.Clear();
        PrivacyDirectory.AddPerson("Jones, Sam (Accounts)", "sam.jones@example.co.uk");
        PrivacyDirectory.AddPerson("Kev Hart");
        PrivacyDirectory.AddOrganisation("Brightsmile Dental");
        PrivacyDirectory.Exempt("Alex Morgan", "Alex", "alex.morgan@example.com");
    }

    public void Dispose() => PrivacyDirectory.Clear();

    [Fact]
    public void Hides_people_addresses_numbers_customers_and_amounts_and_puts_them_back()
    {
        var guard = new Pseudonymiser();
        const string text = "Sam Jones (sam.jones@example.co.uk, 07700 900123) says Brightsmile Dental's renewal is £12,400 a year. Sam wants it by Friday.";
        var masked = guard.Mask(text);

        Assert.DoesNotContain("Sam", masked);
        Assert.DoesNotContain("example.co.uk", masked);
        Assert.DoesNotContain("07700", masked);
        Assert.DoesNotContain("Brightsmile", masked);
        Assert.DoesNotContain("12,400", masked);
        Assert.Contains("[Person 1]", masked);
        Assert.Contains("[Person 1 first name] wants", masked);
        Assert.Contains("[Amount 1]", masked);
        Assert.Equal(text, guard.Unmask(masked));
        Assert.Equal(1, guard.Counts["Person"]);
    }

    [Fact]
    public void Catches_people_who_are_not_in_the_directory()
    {
        var guard = new Pseudonymiser();
        var masked = guard.Mask("New starter\nName: Priya Shah\nLine manager: Oliver Grantham\nStarts Monday.\n\nHi Jo,\nCan Mrs Patel have a laptop?\nKind regards,\nAoife");

        foreach (var name in new[] { "Priya", "Shah", "Oliver", "Grantham", "Jo,", "Patel", "Aoife" })
            Assert.DoesNotContain(name, masked);
        Assert.Contains("Starts Monday.", masked);
        Assert.Contains("Name: [Person", masked);
    }

    [Fact]
    public void Leaves_the_user_and_ordinary_words_alone()
    {
        var guard = new Pseudonymiser();
        const string text = "Alex Morgan (alex.morgan@example.com): Will you grant access? Grant the May licence in Microsoft Teams. Item #123456 on 2026-10-18 at 09:30.";
        Assert.Equal(text, guard.Mask(text));
        Assert.False(guard.Masked);
    }

    [Fact]
    public void Restores_values_inside_json_safely()
    {
        PrivacyDirectory.AddPerson("Siobhan O'Neill");
        var guard = new Pseudonymiser();
        var masked = guard.Mask("Reply to Siobhan O'Neill about \"the printer\".");
        Assert.Equal("Reply to [Person 1] about \"the printer\".", masked);
        var json = guard.Unmask("""{"to":"[Person 1]","first":"[Person 1 first name]"}""", json: true);
        var parsed = JsonDocument.Parse(json).RootElement;
        Assert.Equal("Siobhan O'Neill", parsed.GetProperty("to").GetString());
        Assert.Equal("Siobhan", parsed.GetProperty("first").GetString());
    }

    [Fact]
    public void Unknown_placeholders_are_left_as_written()
    {
        var guard = new Pseudonymiser();
        guard.Mask("Kev Hart called.");
        Assert.Equal("Kev Hart and [Person 9]", guard.Unmask("[Person 1] and [Person 9]"));
    }

    [Fact]
    public void Directory_takes_display_names_in_any_usual_shape()
    {
        PrivacyDirectory.AddPerson("Reception");
        PrivacyDirectory.AddPerson("helpdesk@example.co.uk");
        PrivacyDirectory.AddPerson("Smith, Jane | Sales");
        var names = PrivacyDirectory.Snapshot().People.Select(p => p.Name).ToList();
        Assert.Contains("Jane Smith", names);
        Assert.Contains("Sam Jones", names);
        Assert.DoesNotContain("Reception", names);
        Assert.True(Pseudonymiser.ContainsRestricted("Ask Jane Smith"));
        Assert.False(Pseudonymiser.ContainsRestricted("Backups are healthy and the desk is quiet."));
    }
}
