using System.Data;
using System.Text.Json;
using Content.Server.Database;
using Content.Shared.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SS14.Admin.AdminLogs;
using SS14.Admin.Models;

namespace SS14.Admin.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AdminLogsController : ControllerBase
{
    private readonly IDbContextFactory<PostgresServerDbContext> _dbContextFactory;

    public AdminLogsController(IDbContextFactory<PostgresServerDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    [HttpGet("search")]
    public async Task<ActionResult<AdminLogSearchResult>> SearchLogs(
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? search,
        [FromQuery] LogType? type,
        [FromQuery] LogImpact? impact,
        [FromQuery] int? roundId,
        [FromQuery] int? serverId,
        [FromQuery] string? playerName,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 500);

        var filter = new LogsFilterModel
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Search = search,
            Type = type,
            Impact = impact,
            RoundId = roundId,
            ServerId = serverId,
            PlayerName = playerName,
        };

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var conn = dbContext.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync();

        var npgsqlConn = (NpgsqlConnection)conn;
        var query = AdminLogSearchQuery.Build(filter, limit, offset);

        await using var cmd = new NpgsqlCommand(query.Sql, npgsqlConn);
        cmd.Parameters.AddRange(query.Parameters.ToArray());

        var rows = new List<AdminLogSearchRowDto>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new AdminLogSearchRowDto
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

        return Ok(new AdminLogSearchResult
        {
            Rows = rows,
            Offset = offset,
            Limit = limit,
        });
    }

    [HttpGet("{roundId}/{logId}")]
    public async Task<ActionResult<AdminLogDetailsDto>> GetLogDetails(int roundId, int logId)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var adminLog = await dbContext.AdminLog
            .Include(log => log.Round)
                .ThenInclude(r => r.Server)
            .Include(log => log.Players)
                .ThenInclude(p => p.Player)
            .FirstOrDefaultAsync(log => log.RoundId == roundId && log.Id == logId);

        if (adminLog == null)
        {
            return NotFound(new { message = "Log not found" });
        }

        var dto = new AdminLogDetailsDto
        {
            RoundId = adminLog.RoundId,
            Id = adminLog.Id,
            Type = adminLog.Type,
            Impact = adminLog.Impact,
            Date = adminLog.Date,
            Message = adminLog.Message,
            Json = adminLog.Json,
            ServerName = adminLog.Round.Server?.Name,
            Players = adminLog.Players.Select(p => new AdminLogPlayerDto
            {
                PlayerUserId = p.PlayerUserId,
                PlayerUsername = p.Player.LastSeenUserName
            }).ToList()
        };

        return Ok(dto);
    }

}

public class AdminLogSearchRowDto
{
    public int Id { get; set; }
    public int RoundId { get; set; }
    public LogType Type { get; set; }
    public LogImpact Impact { get; set; }
    public DateTime Date { get; set; }
    public string Message { get; set; } = "";
    public string ServerName { get; set; } = "";
}

public class AdminLogSearchResult
{
    public List<AdminLogSearchRowDto> Rows { get; set; } = new();
    public int Offset { get; set; }
    public int Limit { get; set; }
}
