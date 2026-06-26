using Content.Shared.Database;
using Npgsql;
using SS14.Admin.AdminLogs;
using SS14.Admin.Models;

namespace SS14.Admin.Tests;

/// <summary>
/// Tests every filter at increasing time intervals: 1 hour, 1 day, 1 week, 1 month.
/// If a query can't complete at a given interval, the SQL needs optimization.
/// </summary>
[Collection("Database")]
public class AdminLogSearchIntervalTests(DatabaseFixture db)
{
    private async Task<List<Row>> Execute(LogsFilterModel filter, int limit = 50)
    {
        var query = AdminLogSearchQuery.Build(filter, limit, 0);

        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(query.Sql, conn);
        cmd.Parameters.AddRange(query.Parameters.ToArray());

        var rows = new List<Row>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new Row
            {
                Id = reader.GetInt32(0),
                RoundId = reader.GetInt32(1),
                Type = (LogType)reader.GetInt32(2),
                Impact = (LogImpact)reader.GetInt32(3),
                Date = reader.GetDateTime(4),
                Message = reader.GetString(5),
                ServerName = reader.GetString(6),
            });
        }

        return rows;
    }

    private record Row
    {
        public int Id { get; init; }
        public int RoundId { get; init; }
        public LogType Type { get; init; }
        public LogImpact Impact { get; init; }
        public DateTime Date { get; init; }
        public string Message { get; init; } = "";
        public string ServerName { get; init; } = "";
    }

    // ──────────────────────────────────────────────
    //  Baseline — no search, just date range
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(1)]     // 1 hour
    [InlineData(24)]    // 1 day
    [InlineData(168)]   // 1 week
    [InlineData(720)]   // 1 month
    public async Task Baseline_NoSearch(int hours)
    {
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-hours),
        });
        Assert.NotNull(rows);
        Assert.True(rows.Count <= 50);
    }

    // ──────────────────────────────────────────────
    //  Text search (message + json)
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    [InlineData(720)]
    public async Task Search_Hello(int hours)
    {
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-hours),
            Search = "hello",
        });
        Assert.NotNull(rows);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    [InlineData(720)]
    public async Task Search_CommonWord(int hours)
    {
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-hours),
            Search = "picked up",
        });
        Assert.NotNull(rows);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    [InlineData(720)]
    public async Task Search_SpecialChars(int hours)
    {
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-hours),
            Search = "100% test_value",
        });
        Assert.NotNull(rows);
    }

    // ──────────────────────────────────────────────
    //  Type filter
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    [InlineData(720)]
    public async Task TypeFilter(int hours)
    {
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-hours),
            Type = LogType.Chat,
        });
        foreach (var r in rows)
            Assert.Equal(LogType.Chat, r.Type);
    }

    // ──────────────────────────────────────────────
    //  Impact filter
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    [InlineData(720)]
    public async Task ImpactFilter(int hours)
    {
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-hours),
            Impact = LogImpact.High,
        });
        foreach (var r in rows)
            Assert.Equal(LogImpact.High, r.Impact);
    }

    // ──────────────────────────────────────────────
    //  Player filter
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    [InlineData(720)]
    public async Task PlayerFilter(int hours)
    {
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-hours),
            PlayerName = "admin",
        });
        Assert.NotNull(rows);
    }

    // ──────────────────────────────────────────────
    //  Server filter
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    [InlineData(720)]
    public async Task ServerFilter(int hours)
    {
        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT server_id FROM server LIMIT 1", conn);
        var serverId = await cmd.ExecuteScalarAsync();
        if (serverId == null) return;

        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-hours),
            ServerId = (int)serverId,
        });
        Assert.NotNull(rows);
    }

    // ──────────────────────────────────────────────
    //  Combined: search + type
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    [InlineData(720)]
    public async Task SearchPlusType(int hours)
    {
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-hours),
            Search = "hello",
            Type = LogType.Chat,
        });
        Assert.NotNull(rows);
    }

    // ──────────────────────────────────────────────
    //  Combined: search + player
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    [InlineData(720)]
    public async Task SearchPlusPlayer(int hours)
    {
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-hours),
            Search = "hello",
            PlayerName = "admin",
        });
        Assert.NotNull(rows);
    }

    // ──────────────────────────────────────────────
    //  Combined: all filters
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    [InlineData(720)]
    public async Task AllFilters(int hours)
    {
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-hours),
            DateTo = DateTime.UtcNow,
            Search = "hello",
            Type = LogType.Chat,
            Impact = LogImpact.Low,
            PlayerName = "admin",
            ServerId = 1,
        });
        Assert.NotNull(rows);
    }
}
