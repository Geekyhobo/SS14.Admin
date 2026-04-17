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
using NpgsqlTypes;

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

    // Column visibility toggles
    private bool _showIpColumn = false;
    private bool _showHwidColumn = false;
    private bool _showGuidColumn = false;

    private bool ShowIdentityColumn => _showIpColumn || _showHwidColumn || _showGuidColumn;

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

    private static string FormatAddress(NpgsqlInet address)
    {
        return address.FormatCidr().ToString();
    }

    private static string FormatLabel(string label, string value)
    {
        return string.Concat(label, ": ", value);
    }

    private string RedactIdentity(string label, string value)
    {
        var displayValue = label switch
        {
            "IP" => RedactIp(value),
            "HWID" => RedactHwid(value),
            _ => value
        };

        return FormatLabel(label, displayValue);
    }

    private static string GetStatusBadgeClass(BanViewModel ban)
    {
        if (ban.Active)
            return "bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-200 ring-red-200 dark:ring-red-800/60";

        if (ban.IsRepealed)
            return "bg-emerald-100 text-emerald-800 dark:bg-emerald-900/30 dark:text-emerald-200 ring-emerald-200 dark:ring-emerald-800/60";

        return "bg-gray-100 text-gray-700 dark:bg-gray-700/60 dark:text-gray-200 ring-gray-200 dark:ring-gray-600";
    }

    private static string GetHitBadgeClass(int hitCount)
    {
        return hitCount > 0
            ? "bg-sky-100 text-sky-800 hover:bg-sky-200 dark:bg-sky-900/30 dark:text-sky-200 dark:hover:bg-sky-900/50 ring-sky-200 dark:ring-sky-800/60"
            : "bg-gray-100 text-gray-500 dark:bg-gray-700/60 dark:text-gray-400 ring-gray-200 dark:ring-gray-600";
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
                (x.ban.Players != null && x.ban.Players.Any(bp =>
                    bp.UserId.ToString().ToLower().Contains(search) ||
                    playerMap.TryGetValue(bp.UserId, out var matchedPlayer) && matchedPlayer.LastSeenUserName.ToLower().Contains(search))) ||
                (x.ban.Reason != null && x.ban.Reason.ToLower().Contains(search)) ||
                (x.admin != null && x.admin.LastSeenUserName.ToLower().Contains(search)) ||
                (x.ban.Addresses != null && x.ban.Addresses.Any(address => FormatAddress(address.Address).ToLower().Contains(search))) ||
                (x.ban.Hwids != null && x.ban.Hwids.Any(hwid => hwid.HWId.ToImmutable().ToString().ToLower().Contains(search)))
            ).ToList();
        }

        return result;
    }

    // Refresh the cache and update the UI.
    private async Task Refresh()
    {
        await using var context = await ContextFactory!.CreateDbContextAsync();
        var entities = await GetBansQueryEntities(context);

        var playerIds = entities
            .Where(x => x.ban.Players != null)
            .SelectMany(x => x.ban.Players!)
            .Select(player => player.UserId)
            .Distinct()
            .ToList();

        var playerNameMap = playerIds.Count > 0
            ? await context.Player.AsNoTracking()
                .Where(player => playerIds.Contains(player.UserId))
                .ToDictionaryAsync(player => player.UserId, player => player.LastSeenUserName)
            : new Dictionary<Guid, string>();

        var now = DateTime.UtcNow;

        // Map entities to view models
        _bansList = entities.Select(x => new BanViewModel
        {
            Id = x.ban.Id,
            PlayerUserIds = x.ban.Players?.Select(player => player.UserId.ToString()).ToArray() ?? [],
            PlayerNames = x.ban.Players?
                .Select(player => playerNameMap.GetValueOrDefault(player.UserId))
                .OfType<string>()
                .Distinct()
                .ToArray() ?? [],
            IPAddresses = x.ban.Addresses?.Select(address => FormatAddress(address.Address)).ToArray() ?? [],
            Hwids = x.ban.Hwids?.Select(hwid => hwid.HWId.ToImmutable().ToString()).ToArray() ?? [],
            Reason = x.ban.Reason,
            BanTime = x.ban.BanTime,
            ExpirationTime = x.ban.ExpirationTime,
            HitCount = x.ban.BanHits?.Count ?? 0,
            Admin = x.admin?.LastSeenUserName ?? "",
            Active = x.ban.Unban == null && (!x.ban.ExpirationTime.HasValue || x.ban.ExpirationTime > now),
            IsRepealed = x.ban.Unban != null
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

    private string GetRowClass(BanViewModel ban) => ban.Active
        ? "border-l-4 border-red-400/70 bg-red-50/50 hover:bg-red-50 dark:border-red-500/50 dark:bg-red-900/10 dark:hover:bg-red-900/20"
        : ban.IsRepealed
            ? "border-l-4 border-emerald-300/70 bg-emerald-50/40 hover:bg-emerald-50 dark:border-emerald-500/40 dark:bg-emerald-900/10 dark:hover:bg-emerald-900/20"
            : "border-l-4 border-gray-200 bg-gray-50/50 hover:bg-gray-50 dark:border-gray-700 dark:bg-gray-800/40 dark:hover:bg-gray-800/70";

    public class BanViewModel
    {
        public int Id { get; set; }
        public string Reason { get; set; } = "";
        public DateTime BanTime { get; set; }
        public DateTime? ExpirationTime { get; set; }
        public int HitCount { get; set; }
        public string Admin { get; set; } = "";
        public string[] PlayerNames { get; set; } = [];
        public bool Active { get; set; }
        public bool IsRepealed { get; set; }

        //PII
        public string[] IPAddresses { get; set; } = [];
        public string[] Hwids { get; set; } = [];
        public string[] PlayerUserIds { get; set; } = [];

        public string StatusLabel => Active ? "Active" : IsRepealed ? "Unbanned" : "Expired";
        public string ExpirationLabel => ExpirationTime.HasValue ? ExpirationTime.Value.ToString("yyyy-MM-dd HH:mm") : "Permanent";
        public string[] VisibleIdentityItems(bool showGuid, bool showIp, bool showHwid)
        {
            var items = new List<string>();

            if (showGuid)
                items.AddRange(PlayerUserIds.Select(value => FormatLabel("GUID", value)));

            if (showIp)
                items.AddRange(IPAddresses.Select(value => FormatLabel("IP", value)));

            if (showHwid)
                items.AddRange(Hwids.Select(value => FormatLabel("HWID", value)));

            return items.ToArray();
        }

        public string PrimaryName => PlayerNames.FirstOrDefault() ?? "Unknown player";
        public int AdditionalPlayerCount => Math.Max(PlayerNames.Length - 1, 0);
    }
}
