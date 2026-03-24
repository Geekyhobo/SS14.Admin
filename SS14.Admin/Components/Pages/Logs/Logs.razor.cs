using System.Data;
using Content.Server.Database;
using Content.Shared.Database;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SS14.Admin.AdminLogs;
using SS14.Admin.Models;

namespace SS14.Admin.Components.Pages.Logs;

public partial class Logs
{
    [Inject]
    private IDbContextFactory<PostgresServerDbContext>? ContextFactory { get; set; }

    private readonly LogsFilterModel _filter = new()
    {
        // Default to past 24 hours
        DateFrom = DateTime.UtcNow.AddDays(-1)
    };

    public QuickGrid<AdminLogSearchRow>? Grid { get; set; }

    private PaginationState _pagination = new() { ItemsPerPage = 50 };

    private GridItemsProvider<AdminLogSearchRow>? _logsProvider;

    // Server list for filter dropdown
    private List<Server> _servers = new();

    // Query error state
    private string? _queryError;

    // Drawer state
    private bool _isDrawerOpen;
    private bool _isLoadingLogDetails;
    private bool _hasLogDetailsError;
    private string? _logDetailsError;
    private AdminLogDetailsDto? _selectedLogDetails;

    protected override async Task OnInitializedAsync()
    {
        // Load available servers for the dropdown
        await using (var context = await ContextFactory!.CreateDbContextAsync())
        {
            _servers = await context.Server
                .AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        _logsProvider = async request =>
        {
            try
            {
                var offset = request.StartIndex;
                // Fetch one extra row to detect if there's a next page
                var limit = (request.Count ?? 50) + 1;

                var rows = await ExecuteLogSearch(_filter, limit, offset);

                if (rows.Count == 0)
                    return GridItemsProviderResult.From(rows, request.StartIndex);

                var hasNextPage = rows.Count > (request.Count ?? 50);

                // Trim the extra detection row before returning to the grid
                if (hasNextPage && rows.Count > (request.Count ?? 50))
                    rows.RemoveAt(rows.Count - 1);

                // QuickGrid uses totalItemCount to decide if next button is enabled.
                // We don't know the true count, so we fake it:
                // current offset + rows * 2 if there's a next page, otherwise offset + rows.
                var totalItemCount = request.StartIndex + (hasNextPage ? rows.Count * 2 : rows.Count);

                return GridItemsProviderResult.From(rows, totalItemCount);
            }
            catch (Exception ex)
            {
                _queryError = ex.Message;
                await InvokeAsync(StateHasChanged);
                return GridItemsProviderResult.From(new List<AdminLogSearchRow>(), 0);
            }
        };
    }

    private async Task<List<AdminLogSearchRow>> ExecuteLogSearch(
        LogsFilterModel filter, int limit, int offset)
    {
        await using var context = await ContextFactory!.CreateDbContextAsync();
        var conn = context.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync();

        var npgsqlConn = (NpgsqlConnection)conn;
        var query = AdminLogSearchQuery.Build(filter, limit, offset);

        await using var cmd = new NpgsqlCommand(query.Sql, npgsqlConn);
        cmd.Parameters.AddRange(query.Parameters.ToArray());

        var rows = new List<AdminLogSearchRow>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new AdminLogSearchRow
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

    private async Task Refresh()
    {
    }

    private async Task RefreshFilter()
    {
        _queryError = null;
        // Reset to first page when filter changes
        await _pagination.SetCurrentPageIndexAsync(0);
        if (Grid != null)
            await Grid.RefreshDataAsync();
    }

    private async Task OpenLogDetails(AdminLogSearchRow row)
    {
        _isDrawerOpen = true;
        _isLoadingLogDetails = true;
        _hasLogDetailsError = false;
        _logDetailsError = null;
        _selectedLogDetails = null;

        try
        {
            await using var context = await ContextFactory!.CreateDbContextAsync();

            var logDetails = await context.AdminLog
                .Include(l => l.Round)
                    .ThenInclude(r => r!.Server)
                .Include(l => l.Players)
                    .ThenInclude(p => p.Player)
                .FirstOrDefaultAsync(l => l.RoundId == row.RoundId && l.Id == row.Id);

            if (logDetails == null)
            {
                _hasLogDetailsError = true;
                _logDetailsError = "Log not found";
                return;
            }

            _selectedLogDetails = new AdminLogDetailsDto
            {
                RoundId = logDetails.RoundId,
                Id = logDetails.Id,
                Type = logDetails.Type,
                Impact = logDetails.Impact,
                Date = logDetails.Date,
                Message = logDetails.Message,
                Json = logDetails.Json,
                ServerName = logDetails.Round?.Server?.Name,
                Players = logDetails.Players?.Select(p => new AdminLogPlayerDto
                {
                    PlayerUserId = p.PlayerUserId,
                    PlayerUsername = p.Player?.LastSeenUserName ?? "Unknown"
                }).ToList() ?? new List<AdminLogPlayerDto>()
            };
        }
        catch (Exception ex)
        {
            _hasLogDetailsError = true;
            _logDetailsError = ex.Message;
        }
        finally
        {
            _isLoadingLogDetails = false;
        }
    }

    private async Task DrawerOpenChanged(bool isOpen)
    {
        _isDrawerOpen = isOpen;
        if (!isOpen)
        {
            _selectedLogDetails = null;
        }
    }

    public class AdminLogSearchRow
    {
        public int Id { get; set; }
        public int RoundId { get; set; }
        public LogType Type { get; set; }
        public LogImpact Impact { get; set; }
        public DateTime Date { get; set; }
        public string Message { get; set; } = "";
        public string ServerName { get; set; } = "";
    }
}
