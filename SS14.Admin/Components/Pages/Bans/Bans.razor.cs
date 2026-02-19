using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Content.Server.Database;
using Content.Shared.Database;
using Microsoft.EntityFrameworkCore;
using SS14.Admin.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Authorization;
using SS14.Admin.Helpers;
using System.Security.Claims;
using SS14.Admin.Services;

namespace SS14.Admin.Components.Pages.Bans;

public partial class Bans : IDisposable
{
    [Inject]
    private IDbContextFactory<PostgresServerDbContext>? ContextFactory { get; set; }

    [Inject]
    private AuthenticationStateProvider? AuthStateProvider { get; set; }

    [Inject]
    private ClientPreferencesService? ClientPreferences { get; set; }

    [Inject]
    private IPiiRedactor? PiiRedactor { get; set; }

    private ClaimsPrincipal? _user;
    private bool _shouldCensorPii;

    [SupplyParameterFromForm(FormName = "banFilter")]
    public BansFilterModel _model { get; set; } = new();

    public QuickGrid<BanViewModel> Grid { get; set; }

    private PaginationState _pagination = new() { ItemsPerPage = 13 };

    // Cache of ban data.
    private List<BanViewModel> _bansList = new();

    // Tracks confirmation state for each ban.
    private Dictionary<int, bool> _confirmations = new();

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateProvider!.GetAuthenticationStateAsync();
        _user = authState.User;

        // Initially censor PII until we can load client preferences
        var hasPiiPermission = _user.IsInRole(Constants.PIIRole);
        _shouldCensorPii = !hasPiiPermission;

        ClientPreferences!.OnChange += OnPreferencesChanged;

        await Refresh();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Now we can safely call JavaScript interop to get client preferences
            var hasPiiPermission = _user?.IsInRole(Constants.PIIRole) ?? false;
            var clientPrefs = await ClientPreferences!.GetClientPreferences();
            _shouldCensorPii = !hasPiiPermission || clientPrefs.censorPii;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void OnPreferencesChanged(ClientPreferencesService.ClientPreferences preferences)
    {
        var hasPiiPermission = _user?.IsInRole(Constants.PIIRole) ?? false;
        _shouldCensorPii = !hasPiiPermission || preferences.censorPii;
        InvokeAsync(StateHasChanged);
    }

    private string RedactIp(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || !_shouldCensorPii)
            return ipAddress;

        if (System.Net.IPAddress.TryParse(ipAddress, out var ip))
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return PiiRedactor!.RedactIPv4(ipAddress);
            else
                return PiiRedactor!.RedactIPv6(ipAddress);
        }
        return ipAddress;
    }

    private string RedactHwid(string hwid)
    {
        if (string.IsNullOrWhiteSpace(hwid) || !_shouldCensorPii)
            return hwid;
        return PiiRedactor!.RedactHardwareId(hwid);
    }

    public void Dispose()
    {
        if (ClientPreferences != null)
        {
            ClientPreferences.OnChange -= OnPreferencesChanged;
        }
    }

    private async Task<List<(Ban ban, Player? player, Player? admin)>> GetBansQueryEntities(PostgresServerDbContext context)
    {
        var now = DateTime.UtcNow;

        // Load server bans with all related data using split query
        IQueryable<Ban> bansQuery = context.Ban
            .AsNoTracking()
            .AsSplitQuery()
            .Where(b => b.Type == BanType.Server)
            .Include(b => b.BanHits)
            .Include(b => b.Unban)
            .Include(b => b.Players)
            .Include(b => b.Addresses)
            .Include(b => b.Hwids);

        // Apply date filters at the database level
        if (_model.DateFrom.HasValue)
            bansQuery = bansQuery.Where(x => x.BanTime >= _model.DateFrom.Value);

        if (_model.DateTo.HasValue)
        {
            var dateTo = _model.DateTo.Value.AddDays(1);
            bansQuery = bansQuery.Where(x => x.BanTime < dateTo);
        }

        if (_model.ExpiresFrom.HasValue)
            bansQuery = bansQuery.Where(x => x.ExpirationTime >= _model.ExpiresFrom.Value);

        if (_model.ExpiresTo.HasValue)
        {
            var expiresTo = _model.ExpiresTo.Value.AddDays(1);
            bansQuery = bansQuery.Where(x => x.ExpirationTime < expiresTo);
        }

        // Apply status filters at the database level
        if (_model.ShowActive && !_model.ShowExpired)
        {
            bansQuery = bansQuery.Where(x => x.Unban == null && (!x.ExpirationTime.HasValue || x.ExpirationTime > now));
        }
        else if (!_model.ShowActive && _model.ShowExpired)
        {
            bansQuery = bansQuery.Where(x => x.Unban != null || (x.ExpirationTime.HasValue && x.ExpirationTime <= now));
        }
        else if (!_model.ShowActive && !_model.ShowExpired)
        {
            return new List<(Ban, Player?, Player?)>();
        }

        var bans = await bansQuery.OrderByDescending(x => x.BanTime).ToListAsync();

        // Collect all user IDs needed for player lookups
        var playerUserIds = bans
            .Where(b => b.Players != null)
            .SelectMany(b => b.Players!)
            .Select(bp => bp.UserId)
            .ToHashSet();

        var adminUserIds = bans
            .Where(b => b.BanningAdmin.HasValue)
            .Select(b => b.BanningAdmin!.Value)
            .ToHashSet();

        var allUserIds = playerUserIds.Union(adminUserIds).ToList();

        // Load all relevant players in a single query
        var playerMap = allUserIds.Count > 0
            ? await context.Player.AsNoTracking()
                .Where(p => allUserIds.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId)
            : new Dictionary<Guid, Player>();

        // Map bans to results
        var result = bans.Select(ban =>
        {
            var firstPlayerId = ban.Players?.FirstOrDefault()?.UserId;
            Player? player = firstPlayerId.HasValue && playerMap.TryGetValue(firstPlayerId.Value, out var p) ? p : null;
            Player? admin = ban.BanningAdmin.HasValue && playerMap.TryGetValue(ban.BanningAdmin.Value, out var a) ? a : null;
            return (ban, player, admin);
        }).ToList();

        // Apply search filter in memory (after loading related data)
        if (!string.IsNullOrWhiteSpace(_model.Search))
        {
            var search = _model.Search.ToLower();
            result = result.Where(x =>
                (x.player != null && x.player.LastSeenUserName.ToLower().Contains(search)) ||
                (x.ban.Players != null && x.ban.Players.Any(bp => bp.UserId.ToString().ToLower().Contains(search))) ||
                (x.ban.Reason != null && x.ban.Reason.ToLower().Contains(search)) ||
                (x.admin != null && x.admin.LastSeenUserName.ToLower().Contains(search))
            ).ToList();
        }

        return result;
    }

    // Refresh the cache and update the UI.
    private async Task Refresh()
    {
        await using var context = await ContextFactory!.CreateDbContextAsync();
        var entities = await GetBansQueryEntities(context);

        var now = DateTime.UtcNow;

        // Map entities to view models
        _bansList = entities.Select(x => new BanViewModel
        {
            Id = x.ban.Id,
            PlayerUserId = x.ban.Players?.FirstOrDefault()?.UserId.ToString() ?? "",
            PlayerName = x.player?.LastSeenUserName ?? "",
            IPAddress = x.ban.Addresses?.FirstOrDefault()?.Address.ToString() ?? "",
            Hwid = x.ban.Hwids?.FirstOrDefault()?.HWId.ToImmutable().ToString() ?? "",
            Reason = x.ban.Reason,
            BanTime = x.ban.BanTime,
            ExpirationTime = x.ban.ExpirationTime,
            HitCount = x.ban.BanHits?.Count ?? 0,
            Admin = x.admin?.LastSeenUserName ?? "",
            Active = x.ban.Unban == null && (!x.ban.ExpirationTime.HasValue || x.ban.ExpirationTime > now)
        }).ToList();

        _confirmations.Clear();
        await InvokeAsync(StateHasChanged);
    }

    // Handle filter submission
    private async Task OnFilterSubmit(EditContext context)
    {
        await Refresh();
    }

    // When a user clicks the action button, show confirmation.
    private async Task ShowConfirmation(int banId, bool active)
    {
        _confirmations[banId] = true;
        await InvokeAsync(StateHasChanged);
        await Task.Delay(3000);
        _confirmations[banId] = false;
        await InvokeAsync(StateHasChanged);
    }

    // When the user confirms, unban the ban and refresh
    private async Task ConfirmAction(int banId, bool active)
    {
        _confirmations[banId] = false;
        await InvokeAsync(StateHasChanged);

        // Only active bans can be unbanned
        if (active)
        {
            await UnbanBan(banId);
        }

        await Refresh();
    }

    // Unban a ban by creating an Unban entity
    private async Task UnbanBan(int banId)
    {
        await using var context = await ContextFactory!.CreateDbContextAsync();

        var ban = await context.Ban
            .Include(b => b.Unban)
            .SingleOrDefaultAsync(b => b.Id == banId);

        if (ban == null)
            return;

        // Check if already unbanned
        if (ban.Unban != null)
            return;

        // Get the current admin's user ID
        var authState = await AuthStateProvider!.GetAuthenticationStateAsync();
        var user = authState.User;
        var adminId = user.Claims.GetUserId();

        // Create the unban record
        ban.Unban = new Unban
        {
            Ban = ban,
            UnbanningAdmin = adminId,
            UnbanTime = DateTime.UtcNow
        };

        await context.SaveChangesAsync();
    }

    public class BanViewModel
    {
        public int Id { get; set; }
        public string Reason { get; set; } = "";
        public DateTime BanTime { get; set; }
        public int? Round { get; set; }
        public DateTime? ExpirationTime { get; set; }
        public int HitCount { get; set; }
        public string Admin { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public bool Active { get; set; }

        //PII
        public string IPAddress { get; set; } = "";
        public string Hwid { get; set; } = "";
        public string PlayerUserId { get; set; } = "";
    }
}
