using Content.Server.Database;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.EntityFrameworkCore;
using SS14.Admin.Helpers;
using Microsoft.AspNetCore.Components.Authorization;
using SS14.Admin.Services;
using System.Security.Claims;

namespace SS14.Admin.Components.Pages.Connections;

public partial class ConnectionHits : IDisposable
{
    [Inject]
    private IDbContextFactory<PostgresServerDbContext>? ContextFactory { get; set; }

    [Inject]
    private BanHelper? _banHelper { get; set; }

    [Inject]
    private ClientPreferencesService? ClientPreferences { get; set; }

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    private ClaimsPrincipal? _user;

    [Parameter]
    public int ConnectionId { get; set; }

    public QuickGrid<BanHitViewModel>? Grid { get; set; }

    private PaginationState _pagination = new() { ItemsPerPage = 50 };

    private ConnectionViewModel? Connection { get; set; }

    private List<BanHitViewModel> _banHits = new();

    private string _searchText = "";

    private bool ShowPII { get; set; }

    private bool _loading = true;

    // Tracks confirmation state for each ban
    private Dictionary<int, bool> _confirmations = new();

    protected override async Task OnInitializedAsync()
    {
        // Check if user has PII flag
        var authState = await AuthenticationState!;
        _user = authState.User;
        var hasPiiPermission = _user.IsInRole(Constants.PIIRole);

        // Initially hide PII until we can load client preferences
        ShowPII = hasPiiPermission;

        ClientPreferences!.OnChange += OnPreferencesChanged;

        await LoadData();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Now we can safely call JavaScript interop to get client preferences
            var hasPiiPermission = _user?.IsInRole(Constants.PIIRole) ?? false;
            var clientPrefs = await ClientPreferences!.GetClientPreferences();
            ShowPII = hasPiiPermission && !clientPrefs.censorPii;
            await InvokeAsync(StateHasChanged);
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Connection?.Id != ConnectionId)
        {
            await LoadData();
        }
    }

    private async Task LoadData()
    {
        _loading = true;

        await using var context = await ContextFactory!.CreateDbContextAsync();

        // Load connection information
        var connectionLog = await context.ConnectionLog
            .AsNoTracking()
            .Include(c => c.Server)
            .SingleOrDefaultAsync(c => c.Id == ConnectionId);

        if (connectionLog != null)
        {
            Connection = new ConnectionViewModel
            {
                Id = connectionLog.Id,
                UserName = connectionLog.UserName,
                UserId = connectionLog.UserId,
                Address = connectionLog.Address?.ToString() ?? "",
                HWId = connectionLog.HWId != null ? BanHelper.FormatHwid(connectionLog.HWId) ?? "" : "",
                Time = connectionLog.Time,
                ServerName = connectionLog.Server.Name,
                Denied = connectionLog.Denied
            };
        }

        if (Connection != null)
        {
            var now = DateTime.UtcNow;

            // Load ban IDs hit by this connection
            var hitBanIds = await context.ServerBanHit
                .AsNoTracking()
                .Where(hit => hit.ConnectionId == ConnectionId)
                .Select(hit => hit.BanId)
                .ToListAsync();

            if (hitBanIds.Count > 0)
            {
                // Load the bans with all related data
                var banEntities = await context.Ban
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Where(b => hitBanIds.Contains(b.Id))
                    .Include(b => b.Unban)
                    .Include(b => b.BanHits)
                    .Include(b => b.Players)
                    .Include(b => b.Addresses)
                    .Include(b => b.Hwids)
                    .Include(b => b.Rounds)
                    .ToListAsync();

                // Collect all relevant user IDs for player lookups
                var playerIds = banEntities
                    .Where(b => b.Players != null)
                    .SelectMany(b => b.Players!)
                    .Select(p => p.UserId)
                    .ToHashSet();

                var adminIds = banEntities
                    .Where(b => b.BanningAdmin.HasValue)
                    .Select(b => b.BanningAdmin!.Value)
                    .ToHashSet();

                var allIds = playerIds.Union(adminIds).ToList();

                var playerMap = allIds.Count > 0
                    ? await context.Player.AsNoTracking()
                        .Where(p => allIds.Contains(p.UserId))
                        .ToDictionaryAsync(p => p.UserId)
                    : new Dictionary<Guid, Player>();

                // Map to view models
                _banHits = banEntities.Select(ban =>
                {
                    var firstPlayerId = ban.Players?.FirstOrDefault()?.UserId;
                    Player? player = firstPlayerId.HasValue && playerMap.TryGetValue(firstPlayerId.Value, out var p) ? p : null;
                    Player? admin = ban.BanningAdmin.HasValue && playerMap.TryGetValue(ban.BanningAdmin.Value, out var a) ? a : null;

                    return new BanHitViewModel
                    {
                        Id = ban.Id,
                        PlayerUserId = ban.Players?.FirstOrDefault()?.UserId.ToString() ?? "",
                        PlayerName = player?.LastSeenUserName ?? "",
                        IPAddress = ban.Addresses?.FirstOrDefault()?.Address.ToString() ?? "",
                        Hwid = BanHelper.FormatHwid(ban.Hwids?.FirstOrDefault()?.HWId.ToImmutable()) ?? "",
                        Reason = ban.Reason,
                        BanTime = ban.BanTime,
                        ExpirationTime = ban.ExpirationTime,
                        RoundId = ban.Rounds?.FirstOrDefault()?.RoundId,
                        Admin = admin?.LastSeenUserName ?? "",
                        Unban = ban.Unban,
                        Active = ban.Unban == null && (ban.ExpirationTime == null || ban.ExpirationTime > now),
                        HitCount = ban.BanHits?.Count ?? 0
                    };
                }).ToList();
            }
            else
            {
                _banHits = new List<BanHitViewModel>();
            }
        }

        _loading = false;
        await InvokeAsync(StateHasChanged);
    }

    private IQueryable<BanHitViewModel> GetFilteredBans()
    {
        var query = _banHits.AsQueryable();

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var searchLower = _searchText.ToLower();
            query = query.Where(b =>
                (b.PlayerName != null && b.PlayerName.ToLower().Contains(searchLower)) ||
                b.PlayerUserId.ToLower().Contains(searchLower) ||
                (b.Reason != null && b.Reason.ToLower().Contains(searchLower)) ||
                (b.Admin != null && b.Admin.ToLower().Contains(searchLower))
            );
        }

        return query;
    }

    private async Task RefreshGrid()
    {
        await LoadData();
    }

    // When a user clicks the unban button, show confirmation
    private async Task ShowConfirmation(int banId)
    {
        _confirmations[banId] = true;
        await InvokeAsync(StateHasChanged);
        await Task.Delay(3000);
        _confirmations[banId] = false;
        await InvokeAsync(StateHasChanged);
    }

    // When the user confirms, unban the ban and refresh
    private void OnPreferencesChanged(ClientPreferencesService.ClientPreferences preferences)
    {
        var hasPiiPermission = _user?.IsInRole(Constants.PIIRole) ?? false;
        ShowPII = hasPiiPermission && !preferences.censorPii;
        InvokeAsync(StateHasChanged);
    }

    private async Task ConfirmUnban(int banId)
    {
        _confirmations[banId] = false;
        await InvokeAsync(StateHasChanged);

        await UnbanBan(banId);
        await LoadData();
    }

    public void Dispose()
    {
        if (ClientPreferences != null)
        {
            ClientPreferences.OnChange -= OnPreferencesChanged;
        }
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
        var authState = await AuthenticationState!;
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

    public class ConnectionViewModel
    {
        public int Id { get; set; }
        public string UserName { get; set; } = "";
        public Guid UserId { get; set; }
        public string Address { get; set; } = "";
        public string HWId { get; set; } = "";
        public DateTime Time { get; set; }
        public string ServerName { get; set; } = "";
        public ConnectionDenyReason? Denied { get; set; }
    }

    public class BanHitViewModel
    {
        public int Id { get; set; }
        public string PlayerUserId { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string IPAddress { get; set; } = "";
        public string Hwid { get; set; } = "";
        public string? Reason { get; set; }
        public DateTime BanTime { get; set; }
        public DateTime? ExpirationTime { get; set; }
        public int? RoundId { get; set; }
        public string? Admin { get; set; }
        public Unban? Unban { get; set; }
        public bool Active { get; set; }
        public int HitCount { get; set; }
    }
}
