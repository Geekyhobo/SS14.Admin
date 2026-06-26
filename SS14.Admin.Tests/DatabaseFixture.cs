using Microsoft.Extensions.Configuration;
using Npgsql;

namespace SS14.Admin.Tests;

//Grabs the Db form appsettings
public abstract class DatabaseFixture : IAsyncLifetime
{
    private NpgsqlConnection Connection { get; set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(FindRepoRoot())
            .AddYamlFile("SS14.Admin/appsettings.yml", optional: true)
            .AddYamlFile("SS14.Admin/appsettings.Development.yml", optional: true)
            .AddEnvironmentVariables()
            .Build();

        ConnectionString = config.GetConnectionString("DefaultConnection")
                           ?? throw new InvalidOperationException(
                               "No DefaultConnection found. Ensure appsettings.Development.yml exists with a ConnectionStrings:DefaultConnection entry.");

        if (!ConnectionString.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
            ConnectionString += ";Timeout=10;Command Timeout=30";

        Connection = new NpgsqlConnection(ConnectionString);
        await Connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await Connection.DisposeAsync();
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "SS14.Admin.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find repo root (SS14.Admin.sln). Are you running tests from within the repo?");
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
}
