using Content.Shared.Database;
using Npgsql;
using SS14.Admin.AdminLogs;
using SS14.Admin.Models;

namespace SS14.Admin.Tests;

/// <summary>
/// Integration tests for AdminLogSearchQuery.
/// Every test builds a real SQL query and uses the connection defined in appsettings to do testing on a real db
/// </summary>
[Collection("Database")]
public class AdminLogSearchQueryTests
{
    private readonly DatabaseFixture _db;

    public AdminLogSearchQueryTests(DatabaseFixture db)
    {
        _db = db;
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private async Task<List<SearchRow>> Execute(LogsFilterModel filter, int limit = 50, int offset = 0)
    {
        var query = AdminLogSearchQuery.Build(filter, limit, offset);

        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(query.Sql, conn);
        cmd.Parameters.AddRange(query.Parameters.ToArray());

        var rows = new List<SearchRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new SearchRow
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

    private record SearchRow
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
    //  No filters — baseline
    // ──────────────────────────────────────────────

    // All tests use a date range to ensure the IX_admin_log_date index is used.
    // Without a date bound, queries scan the entire table and time out.
    private static LogsFilterModel Last24h() => new() { DateFrom = DateTime.UtcNow.AddDays(-1) };

    // Search queries need a tighter window — FTS without a GIN index is O(rows in range).
    private static LogsFilterModel LastHour() => new() { DateFrom = DateTime.UtcNow.AddHours(-1) };

    [Fact]
    public async Task NoFilters_ExecutesSuccessfully()
    {
        var rows = await Execute(Last24h(), limit: 10);
        Assert.NotNull(rows);
    }

    [Fact]
    public async Task ResultsOrderedByDateDescending()
    {
        var rows = await Execute(Last24h(), limit: 50);
        for (var i = 1; i < rows.Count; i++)
            Assert.True(rows[i - 1].Date >= rows[i].Date,
                $"Row {i - 1} ({rows[i - 1].Date:O}) should be >= row {i} ({rows[i].Date:O})");
    }

    [Fact]
    public async Task Limit_RespectsMaxRows()
    {
        var rows = await Execute(Last24h(), limit: 5);
        Assert.True(rows.Count <= 5);
    }

    [Fact]
    public async Task Offset_SkipsRows()
    {
        var page1 = await Execute(Last24h(), limit: 5, offset: 0);
        var page2 = await Execute(Last24h(), limit: 5, offset: 5);

        if (page1.Count == 5 && page2.Count > 0)
        {
            var ids1 = page1.Select(r => (r.RoundId, r.Id)).ToHashSet();
            var ids2 = page2.Select(r => (r.RoundId, r.Id)).ToHashSet();
            Assert.Empty(ids1.Intersect(ids2));
        }
    }

    // ──────────────────────────────────────────────
    //  Date range filter
    // ──────────────────────────────────────────────

    [Fact]
    public async Task DateFrom_OnlyReturnsNewerLogs()
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        var rows = await Execute(new LogsFilterModel { DateFrom = cutoff }, limit: 50);
        foreach (var row in rows)
            Assert.True(row.Date >= cutoff);
    }

    [Fact]
    public async Task DateTo_OnlyReturnsOlderLogs()
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        var effectiveCutoff = cutoff.AddDays(1); // Build() adds +1 day
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddDays(-2), // Bound both sides
            DateTo = cutoff,
        }, limit: 50);
        foreach (var row in rows)
            Assert.True(row.Date < effectiveCutoff);
    }

    [Fact]
    public async Task DateRange_BothBounds_ExecutesSuccessfully()
    {
        var filter = new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddDays(-7),
            DateTo = DateTime.UtcNow,
        };
        var rows = await Execute(filter, limit: 50);
        foreach (var row in rows)
            Assert.True(row.Date >= filter.DateFrom.Value);
    }

    [Fact]
    public async Task DateFrom_UnspecifiedKind_DoesNotCrashNpgsql()
    {
        // This was the original Npgsql InvalidCastException bug
        var unspecified = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var rows = await Execute(new LogsFilterModel { DateFrom = unspecified }, limit: 5);
        Assert.NotNull(rows);
    }

    // ──────────────────────────────────────────────
    //  Search — message FTS + json ILIKE
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Search_SimpleWord_ExecutesSuccessfully()
    {
        var filter = LastHour();
        filter.Search = "the";
        var rows = await Execute(filter, limit: 10);
        Assert.NotNull(rows);
    }

    [Fact]
    public async Task Search_Phrase_ExecutesSuccessfully()
    {
        var filter = LastHour();
        filter.Search = "picked up";
        var rows = await Execute(filter, limit: 10);
        Assert.NotNull(rows);
    }

    [Fact]
    public async Task Search_IlikeMetacharacters_DoesNotThrow()
    {
        var filter = LastHour();
        filter.Search = "100% test_value";
        var rows = await Execute(filter, limit: 5);
        Assert.NotNull(rows);
    }

    [Fact]
    public async Task Search_Backslash_DoesNotThrow()
    {
        var filter = LastHour();
        filter.Search = @"path\to\thing";
        var rows = await Execute(filter, limit: 5);
        Assert.NotNull(rows);
    }

    [Fact]
    public async Task Search_SqlInjectionAttempt_DoesNotThrow()
    {
        var filter = LastHour();
        filter.Search = "'; DROP TABLE admin_log; --";
        var rows = await Execute(filter, limit: 5);
        Assert.NotNull(rows);
    }

    [Fact]
    public async Task Search_EmptyString_EquivalentToNoSearch()
    {
        var withEmpty = LastHour();
        withEmpty.Search = "";
        var withEmptyRows = await Execute(withEmpty, limit: 10);

        var withNull = LastHour();
        var withNullRows = await Execute(withNull, limit: 10);

        Assert.Equal(withNullRows.Count, withEmptyRows.Count);
    }

    // ──────────────────────────────────────────────
    //  Type filter
    // ──────────────────────────────────────────────

    [Fact]
    public async Task TypeFilter_ReturnsOnlyMatchingType()
    {
        var rows = await Execute(new LogsFilterModel
        {
            Type = LogType.Chat,
            DateFrom = DateTime.UtcNow.AddDays(-7),
        }, limit: 20);
        foreach (var row in rows)
            Assert.Equal(LogType.Chat, row.Type);
    }

    [Fact]
    public async Task TypeFilter_DifferentTypes_ReturnCorrectRows()
    {
        var chatRows = await Execute(new LogsFilterModel
        {
            Type = LogType.Chat,
            DateFrom = DateTime.UtcNow.AddDays(-7),
        }, limit: 5);
        var actionRows = await Execute(new LogsFilterModel
        {
            Type = LogType.Action,
            DateFrom = DateTime.UtcNow.AddDays(-7),
        }, limit: 5);

        foreach (var r in chatRows) Assert.Equal(LogType.Chat, r.Type);
        foreach (var r in actionRows) Assert.Equal(LogType.Action, r.Type);
    }

    // ──────────────────────────────────────────────
    //  Impact filter
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ImpactFilter_ReturnsOnlyMatchingImpact()
    {
        var rows = await Execute(new LogsFilterModel
        {
            Impact = LogImpact.High,
            DateFrom = DateTime.UtcNow.AddDays(-7),
        }, limit: 20);
        foreach (var row in rows)
            Assert.Equal(LogImpact.High, row.Impact);
    }

    // ──────────────────────────────────────────────
    //  Round ID filter
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RoundIdFilter_ReturnsOnlyMatchingRound()
    {
        // Get a real round ID from the database first
        var anyRow = await Execute(new LogsFilterModel { DateFrom = DateTime.UtcNow.AddDays(-7) }, limit: 1);
        if (anyRow.Count == 0) return; // No data

        var roundId = anyRow[0].RoundId;
        var rows = await Execute(new LogsFilterModel { RoundId = roundId, DateFrom = DateTime.UtcNow.AddDays(-7) }, limit: 50);
        foreach (var row in rows)
            Assert.Equal(roundId, row.RoundId);
    }

    // ──────────────────────────────────────────────
    //  Server filter
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ServerIdFilter_ReturnsConsistentServerName()
    {
        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT server_id FROM server LIMIT 1", conn);
        var serverId = await cmd.ExecuteScalarAsync();
        if (serverId == null) return;

        var rows = await Execute(new LogsFilterModel
        {
            ServerId = (int)serverId,
            DateFrom = DateTime.UtcNow.AddHours(-1),
        }, limit: 10);

        if (rows.Count > 1)
        {
            var name = rows[0].ServerName;
            foreach (var row in rows)
                Assert.Equal(name, row.ServerName);
        }
    }

    // ──────────────────────────────────────────────
    //  Player filter
    // ──────────────────────────────────────────────

    [Fact]
    public async Task PlayerFilter_ExecutesSuccessfully()
    {
        var rows = await Execute(new LogsFilterModel
        {
            PlayerName = "admin",
            DateFrom = DateTime.UtcNow.AddHours(-1),
        }, limit: 10);
        Assert.NotNull(rows);
    }

    [Fact]
    public async Task PlayerFilter_SpecialCharacters_DoesNotThrow()
    {
        var rows = await Execute(new LogsFilterModel
        {
            PlayerName = "test_user%",
            DateFrom = DateTime.UtcNow.AddHours(-1),
        }, limit: 5);
        Assert.NotNull(rows);
    }

    // ──────────────────────────────────────────────
    //  Combined filters
    // ──────────────────────────────────────────────

    [Fact]
    public async Task AllFiltersCombined_ExecutesSuccessfully()
    {
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = DateTime.UtcNow.AddHours(-1),
            DateTo = DateTime.UtcNow,
            Search = "the",
            Type = LogType.Chat,
            Impact = LogImpact.Low,
            RoundId = 1,
            ServerId = 1,
            PlayerName = "admin",
        }, limit: 10);
        Assert.NotNull(rows);
    }

    [Fact]
    public async Task SearchPlusDateRange_RespectsDateBound()
    {
        var from = DateTime.UtcNow.AddHours(-1);
        var rows = await Execute(new LogsFilterModel
        {
            DateFrom = from,
            Search = "the",
        }, limit: 20);
        foreach (var row in rows)
            Assert.True(row.Date >= from);
    }

    [Fact]
    public async Task TypePlusImpact_ReturnsCorrectCombination()
    {
        var rows = await Execute(new LogsFilterModel
        {
            Type = LogType.Chat,
            Impact = LogImpact.Low,
            DateFrom = DateTime.UtcNow.AddDays(-7),
        }, limit: 20);
        foreach (var row in rows)
        {
            Assert.Equal(LogType.Chat, row.Type);
            Assert.Equal(LogImpact.Low, row.Impact);
        }
    }

    // ──────────────────────────────────────────────
    //  Result shape verification
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Results_DatesAreUtc()
    {
        var rows = await Execute(Last24h(), limit: 5);
        foreach (var row in rows)
            Assert.Equal(DateTimeKind.Utc, row.Date.Kind);
    }

    [Fact]
    public async Task Results_ServerNameNeverNull()
    {
        var rows = await Execute(Last24h(), limit: 10);
        foreach (var row in rows)
            Assert.NotNull(row.ServerName);
    }

    [Fact]
    public async Task Results_MessageNeverNull()
    {
        var rows = await Execute(Last24h(), limit: 10);
        foreach (var row in rows)
            Assert.NotNull(row.Message);
    }

    // ──────────────────────────────────────────────
    //  EscapeLike (pure, no DB)
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("100% done", @"100\% done")]
    [InlineData("some_value", @"some\_value")]
    [InlineData(@"path\to\file", @"path\\to\\file")]
    [InlineData(@"a\b%c_d", @"a\\b\%c\_d")]
    public void EscapeLike_EscapesCorrectly(string input, string expected)
    {
        Assert.Equal(expected, AdminLogSearchQuery.EscapeLike(input));
    }

    // ──────────────────────────────────────────────
    //  NormalizeToUtc (pure, no DB)
    // ──────────────────────────────────────────────

    [Fact]
    public void NormalizeToUtc_UtcPassthrough()
    {
        var utc = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var result = AdminLogSearchQuery.NormalizeToUtc(utc);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(utc, result);
    }

    [Fact]
    public void NormalizeToUtc_UnspecifiedBecomesUtc()
    {
        var dt = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Unspecified);
        var result = AdminLogSearchQuery.NormalizeToUtc(dt);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(dt.Ticks, result.Ticks);
    }

    [Fact]
    public void NormalizeToUtc_LocalConverted()
    {
        var local = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Local);
        var result = AdminLogSearchQuery.NormalizeToUtc(local);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(local.ToUniversalTime(), result);
    }
}
