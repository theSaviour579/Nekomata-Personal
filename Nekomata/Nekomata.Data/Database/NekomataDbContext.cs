using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Nekomata.Data.Database;

public class NekomataDbContext
{
    private readonly IConfiguration _configuration;

    public NekomataDbContext(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public NpgsqlConnection Create()
    {
        var section = _configuration.GetSection("Database");

        var host = section["Host"];
        var database = section["Database"];
        var username = section["Username"];
        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(database) ||
            string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException(
                "Database configuration is incomplete. Set Database:Host, Database:Database and Database:Username.");
        }

        if (!int.TryParse(section["Port"], out var port))
            port = 5432;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = section["Password"] ?? string.Empty,
            Timeout = 10,
            CommandTimeout = 30,
            ApplicationName = "Nekomata"
        };

        return new NpgsqlConnection(builder.ConnectionString);
    }
}
