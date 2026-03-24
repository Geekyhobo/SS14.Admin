using System.Text;
using Content.Shared.Database;
using Npgsql;
using NpgsqlTypes;
using SS14.Admin.Models;

namespace SS14.Admin.AdminLogs;

/// <summary>
/// Builds a raw SQL query for searching admin logs.
/// Extracted so it can be shared between the Blazor page and the API controller,
/// and independently tested.
/// </summary>
public static class AdminLogSearchQuery
{
    /// <summary>
    /// Builds the parameterized SQL and its NpgsqlParameters for an admin log search.
    /// </summary>
    public static QueryResult Build(LogsFilterModel filter, int limit, int offset)
    {
        var where = new StringBuilder();
        var ctes = new StringBuilder();
        var parameters = new List<NpgsqlParameter>();
        var hasCte = false;

        // Date range — uses IX_admin_log_date to narrow scan
        if (filter.DateFrom != null)
        {
            where.Append(" AND a.date >= @date_from");
            parameters.Add(new NpgsqlParameter("date_from", NpgsqlDbType.TimestampTz)
                { Value = NormalizeToUtc(filter.DateFrom.Value) });
        }

        if (filter.DateTo != null)
        {
            where.Append(" AND a.date < @date_to");
            parameters.Add(new NpgsqlParameter("date_to", NpgsqlDbType.TimestampTz)
                { Value = NormalizeToUtc(filter.DateTo.Value.AddDays(1)) });
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            where.Append(" AND a.message ILIKE @search_pattern");
            parameters.Add(new NpgsqlParameter("search_pattern", $"%{EscapeLike(filter.Search)}%"));
        }

        // Type filter — uses IX_admin_log_type index
        if (filter.Type != null)
        {
            where.Append(" AND a.type = @type");
            parameters.Add(new NpgsqlParameter("type", (int)filter.Type.Value));
        }

        // Impact filter
        if (filter.Impact != null)
        {
            where.Append(" AND a.impact = @impact");
            parameters.Add(new NpgsqlParameter("impact", (int)filter.Impact.Value));
        }

        // Round ID
        if (filter.RoundId != null)
        {
            where.Append(" AND a.round_id = @round_id");
            parameters.Add(new NpgsqlParameter("round_id", filter.RoundId.Value));
        }

        if (filter.ServerId != null)
        {
            where.Append(" AND a.round_id IN (SELECT round_id FROM round WHERE server_id = @server_id)");
            parameters.Add(new NpgsqlParameter("server_id", filter.ServerId.Value));
        }

        if (!string.IsNullOrWhiteSpace(filter.PlayerName))
        {
            hasCte = true;
            ctes.Append("matched_players AS (SELECT user_id FROM player WHERE last_seen_user_name ILIKE @player_pattern)");
            where.Append(@" AND EXISTS (
                SELECT 1 FROM admin_log_player alp
                WHERE alp.round_id = a.round_id AND alp.log_id = a.admin_log_id
                AND alp.player_user_id IN (SELECT user_id FROM matched_players)
            )");
            parameters.Add(new NpgsqlParameter("player_pattern", $"%{EscapeLike(filter.PlayerName)}%"));
        }

        parameters.Add(new NpgsqlParameter("limit", limit));
        parameters.Add(new NpgsqlParameter("offset", offset));

        var ctePrefix = hasCte ? $"WITH {ctes}\n" : "";

        // Main query: only selects columns needed for the list view.
        // Deliberately skips json
        var sql = $@"{ctePrefix}SELECT
    a.admin_log_id,
    a.round_id,
    a.type,
    a.impact,
    a.date,
    a.message,
    COALESCE(s.name, '') AS server_name
FROM admin_log a
INNER JOIN round r ON r.round_id = a.round_id
LEFT JOIN server s ON s.server_id = r.server_id
WHERE TRUE{where}
ORDER BY a.date DESC
LIMIT @limit OFFSET @offset";

        return new QueryResult(sql, parameters);
    }

    public static string EscapeLike(string input)
        => input.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    //it only like utc
    public static DateTime NormalizeToUtc(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => dt,
        DateTimeKind.Local => dt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
    };

    public record QueryResult(string Sql, List<NpgsqlParameter> Parameters)
    {
        public NpgsqlParameter? FindParameter(string name)
            => Parameters.FirstOrDefault(p => p.ParameterName == name);
    }
}
