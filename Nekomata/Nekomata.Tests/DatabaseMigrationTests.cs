using Microsoft.Extensions.Configuration;
using Nekomata.Data.Database;
using Xunit;

namespace Nekomata.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public void Migrations_are_ordered_and_have_unique_versions()
    {
        var migrations = DatabaseMigrations.All;

        Assert.NotEmpty(migrations);
        Assert.Equal(
            migrations.Select(migration => migration.Version).Order(),
            migrations.Select(migration => migration.Version));
        Assert.Equal(
            migrations.Count,
            migrations.Select(migration => migration.Version).Distinct().Count());
    }

    [Theory]
    [InlineData("assistant.tasks")]
    [InlineData("assistant.projects")]
    [InlineData("assistant.guardian_memory")]
    [InlineData("assistant.guardian_audit")]
    [InlineData("assistant.mission_sessions")]
    [InlineData("estimated_business_value")]
    [InlineData("completed_at")]
    public void Migrations_cover_repository_schema(string expectedSql)
    {
        var sql = string.Join(Environment.NewLine, DatabaseMigrations.All.Select(migration => migration.Sql));

        Assert.Contains(expectedSql, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Database_context_reports_missing_required_configuration()
    {
        var configuration = new ConfigurationBuilder().Build();
        var context = new NekomataDbContext(configuration);

        var exception = Assert.Throws<InvalidOperationException>(context.Create);

        Assert.Contains("Database configuration is incomplete", exception.Message);
    }

    [Fact]
    public void Database_context_uses_safe_connection_timeouts()
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Host"] = "localhost",
            ["Database:Database"] = "nekomata",
            ["Database:Username"] = "postgres"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var context = new NekomataDbContext(configuration);

        using var connection = context.Create();

        Assert.Equal(10, connection.ConnectionTimeout);
        Assert.Contains("Command Timeout=30", connection.ConnectionString);
        Assert.DoesNotContain("Password=postgres", connection.ConnectionString);
    }
}
