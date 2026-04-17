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

namespace SS14.Admin.Components.Pages.RoleBans;

public partial class RoleBans : IDisposable
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

    [SupplyParameterFromForm(FormName = "roleBanFilter")]
    public RoleBansFilterModel _model { get; set; } = new();

    public QuickGrid<RoleBanViewModel> Grid { get; set; }

    private PaginationState _pagination = new() { ItemsPerPage = 13 };

    // Cache of role ban data.
    private List<RoleBanViewModel> _roleBansList = new();

    // Tracks confirmation state for each ban.
    private Dictionary<int, bool> _confirmations = new();

    // Tracks which ban rows have their roles expanded
    private HashSet<int> _expandedRoleRows = new();

    // Column visibility toggles
    private bool _showIpColumn = false;
    private bool _showHwidColumn = false;
    private bool _showGuidColumn = false;

    private bool ShowIdentityColumn => _showIpColumn || _showHwidColumn || _showGuidColumn;

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateProvider!.GetAuthenticationStateAsync();
        _user = authState.User;

        var hasPiiPermission = _user.IsInRole(Constants.PIIRole);
        _shouldCensorPii = !hasPiiPermission;

        ClientPreferences!.OnChange += OnPreferencesChanged;

        await Refresh();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
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

    public void Dispose()
    {
        if (ClientPreferences != null)
        {
            ClientPreferences.OnChange -= OnPreferencesChanged;
        }
    }

    // ───────────────────────────────────────────────
    // PII helpers
    // ───────────────────────────────────────────────

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

    private string RedactIps(IEnumerable<string> ipAddresses)
    {
        return string.Join(", ", ipAddresses.Select(RedactIp).Where(ip => !string.IsNullOrWhiteSpace(ip)));
    }

    private string RedactHwids(IEnumerable<string> hwids)
    {
        return string.Join(", ", hwids.Select(RedactHwid).Where(hwid => !string.IsNullOrWhiteSpace(hwid)));
    }

    private string RedactIdentity(string label, string value)
    {
        var displayValue = label switch
        {
            "IP" => RedactIp(value),
            "HWID" => RedactHwid(value),
            _ => value
        };
        return $"{label}: {displayValue}";
    }

    // ───────────────────────────────────────────────
    // Format helpers
    // ───────────────────────────────────────────────

    private static string FormatRole(BanRole role) => $"{role.RoleType}:{role.RoleId}";

    private static string FormatAddress(NpgsqlInet address) => address.FormatCidr().ToString();

    private static string JoinValues<T>(IEnumerable<T> values) => string.Join(", ", values);

    // ───────────────────────────────────────────────
    // Row styling
    // ───────────────────────────────────────────────

    private static string GetStatusBadgeClass(RoleBanViewModel ban)
    {
        if (ban.Active)
            return "bg-orange-100 text-orange-800 dark:bg-orange-500/20 dark:text-orange-300 ring-orange-300 dark:ring-orange-500/40";

        if (ban.IsRepealed)
            return "bg-emerald-100 text-emerald-800 dark:bg-emerald-500/20 dark:text-emerald-300 ring-emerald-300 dark:ring-emerald-500/40";

        return "bg-gray-100 text-gray-600 dark:bg-gray-600/30 dark:text-gray-400 ring-gray-200 dark:ring-gray-600";
    }

    private string GetRowClass(RoleBanViewModel ban) => ban.Active
        ? "border-l-4 border-orange-400 bg-orange-50/60 hover:bg-orange-50 dark:border-orange-500/70 dark:bg-orange-950/25 dark:hover:bg-orange-950/40"
        : ban.IsRepealed
            ? "border-l-4 border-emerald-400 bg-emerald-50/40 hover:bg-emerald-50 dark:border-emerald-500/60 dark:bg-emerald-950/20 dark:hover:bg-emerald-950/35"
            : "border-l-4 border-gray-200 bg-gray-50/40 hover:bg-gray-100/60 dark:border-gray-600 dark:bg-gray-800/50 dark:hover:bg-gray-750/70";

    // ───────────────────────────────────────────────
    // Role expansion
    // ───────────────────────────────────────────────

    private bool IsRoleExpanded(int banId) => _expandedRoleRows.Contains(banId);

    private void ToggleRoleExpansion(int banId)
    {
        if (!_expandedRoleRows.Add(banId))
            _expandedRoleRows.Remove(banId);
    }

    // ───────────────────────────────────────────────
    // Department definitions & grouping
    // ───────────────────────────────────────────────

    public enum RoleDepartment
    {
        Other,
        Security,
        Cargo,
        Science,
        Engineering,
        Service,
        Command,
        Medical
    }

    private static readonly Dictionary<RoleDepartment, HashSet<string>> DepartmentRoles = new()
    {
        [RoleDepartment.Security] = ["SecurityOfficer", "Detective", "Warden", "HeadOfSecurity", "Brigmedic", "Cadet"],
        [RoleDepartment.Cargo] = ["Quartermaster", "CargoTechnician", "SalvageSpecialist"],
        [RoleDepartment.Science] = ["ResearchDirector", "Scientist", "ResearchAssistant", "Roboticist"],
        [RoleDepartment.Engineering] = ["ChiefEngineer", "StationEngineer", "AtmosphericTechnician", "TechnicalAssistant"],
        [RoleDepartment.Command] = ["HeadOfPersonnel", "Captain", "NanotrasenRepresentative", "BlueShieldOfficer"],
        [RoleDepartment.Medical] = ["ChiefMedicalOfficer", "MedicalDoctor", "Chemist", "Paramedic", "MedicalIntern", "Psychologist"],
        [RoleDepartment.Service] = ["Barkeep", "Botanist", "Chef", "Clown", "Chaplain", "Janitor", "Lawyer", "Librarian", "Mime", "Musician", "Passenger", "Reporter", "ServiceWorker"],
    };

    private static readonly Dictionary<string, RoleDepartment> RoleToDepartment =
        DepartmentRoles
            .SelectMany(kv => kv.Value.Select(role => (role, dept: kv.Key)))
            .ToDictionary(x => x.role, x => x.dept);

    private static readonly Dictionary<RoleDepartment, string> DepartmentLabels = new()
    {
        [RoleDepartment.Security] = "Security",
        [RoleDepartment.Cargo] = "Cargo",
        [RoleDepartment.Science] = "Science",
        [RoleDepartment.Engineering] = "Engineering",
        [RoleDepartment.Command] = "Command",
        [RoleDepartment.Medical] = "Medical",
        [RoleDepartment.Service] = "Service",
    };

    private static string ExtractRoleId(string role)
    {
        var colonIdx = role.IndexOf(':');
        return colonIdx >= 0 ? role[(colonIdx + 1)..] : role;
    }

    private static RoleDepartment GetRoleDepartment(string roleId)
    {
        return RoleToDepartment.GetValueOrDefault(roleId, RoleDepartment.Other);
    }

    /// <summary>
    /// Analyzes a ban's roles and produces a grouped display:
    /// - Full departments collapse into a single department entry
    /// - Remaining individual roles stay as-is
    /// </summary>
    private static RoleDisplayInfo GetRoleDisplayInfo(string[] roles)
    {
        var roleIds = roles.Select(ExtractRoleId).ToHashSet();

        var fullDepartments = new List<RoleDepartment>();
        var coveredRoleIds = new HashSet<string>();

        foreach (var (dept, members) in DepartmentRoles)
        {
            if (members.IsSubsetOf(roleIds))
            {
                fullDepartments.Add(dept);
                coveredRoleIds.UnionWith(members);
            }
        }

        var remainingRoles = roles
            .Where(r => !coveredRoleIds.Contains(ExtractRoleId(r)))
            .ToArray();

        return new RoleDisplayInfo
        {
            FullDepartments = fullDepartments.ToArray(),
            IndividualRoles = remainingRoles,
            TotalCount = fullDepartments.Count + remainingRoles.Length,
        };
    }

    // ───────────────────────────────────────────────
    // Role chip styling
    // ───────────────────────────────────────────────

    private static string GetDepartmentBadgeStyle(RoleDepartment dept)
    {
        return dept switch
        {
            RoleDepartment.Security => "background-color: color-mix(in srgb, var(--ss14-red) 18%, transparent); color: var(--ss14-red-dark); border-color: color-mix(in srgb, var(--ss14-red) 45%, transparent);",
            RoleDepartment.Cargo => "background-color: color-mix(in srgb, var(--color-warning) 18%, var(--ss14-white)); color: #8A5A2B; border-color: color-mix(in srgb, var(--color-warning) 40%, transparent);",
            RoleDepartment.Science => "background-color: color-mix(in srgb, #8B5CF6 18%, transparent); color: #6D28D9; border-color: color-mix(in srgb, #8B5CF6 40%, transparent);",
            RoleDepartment.Engineering => "background-color: color-mix(in srgb, var(--color-warning) 22%, transparent); color: #92400E; border-color: color-mix(in srgb, var(--color-warning) 45%, transparent);",
            RoleDepartment.Service => "background-color: color-mix(in srgb, var(--color-success) 18%, transparent); color: #047857; border-color: color-mix(in srgb, var(--color-success) 40%, transparent);",
            RoleDepartment.Command => "background-color: color-mix(in srgb, var(--color-info) 18%, #0F172A 10%); color: #1E3A8A; border-color: color-mix(in srgb, var(--color-info) 45%, transparent);",
            RoleDepartment.Medical => "background-color: color-mix(in srgb, var(--ss14-white) 88%, #E5E7EB); color: #1F2937; border-color: color-mix(in srgb, var(--border-light) 85%, transparent);",
            _ => "background-color: color-mix(in srgb, var(--ss14-light-gray) 82%, transparent); color: var(--ss14-text-primary-light); border-color: color-mix(in srgb, var(--border-light) 85%, transparent);",
        };
    }

    private static string GetRoleBadgeStyle(string role)
    {
        var roleId = ExtractRoleId(role);
        var department = GetRoleDepartment(roleId);
        return GetDepartmentBadgeStyle(department);
    }

    // ───────────────────────────────────────────────
    // Query
    // ───────────────────────────────────────────────

    private async Task<List<(Ban ban, Player? player, Player? admin)>> GetRoleBansQueryEntities(PostgresServerDbContext context)
    {
        var now = DateTime.UtcNow;

        IQueryable<Ban> bansQuery = context.Ban
            .AsNoTracking()
            .AsSplitQuery()
            .Where(b => b.Type == BanType.Role)
            .Include(b => b.Unban)
            .Include(b => b.Players)
            .Include(b => b.Addresses)
            .Include(b => b.Hwids)
            .Include(b => b.Roles)
            .Include(b => b.Rounds);

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

        var playerMap = allUserIds.Count > 0
            ? await context.Player.AsNoTracking()
                .Where(p => allUserIds.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId)
            : new Dictionary<Guid, Player>();

        var result = bans.Select(ban =>
        {
            var firstPlayerId = ban.Players?.FirstOrDefault()?.UserId;
            Player? player = firstPlayerId.HasValue && playerMap.TryGetValue(firstPlayerId.Value, out var p) ? p : null;
            Player? admin = ban.BanningAdmin.HasValue && playerMap.TryGetValue(ban.BanningAdmin.Value, out var a) ? a : null;
            return (ban, player, admin);
        }).ToList();

        if (!string.IsNullOrWhiteSpace(_model.Search))
        {
            var search = _model.Search.ToLower();
            result = result.Where(x =>
                (x.ban.Players != null && x.ban.Players.Any(bp =>
                    bp.UserId.ToString().ToLower().Contains(search) ||
                    playerMap.TryGetValue(bp.UserId, out var matchedPlayer) && matchedPlayer.LastSeenUserName.ToLower().Contains(search))) ||
                (x.ban.Reason != null && x.ban.Reason.ToLower().Contains(search)) ||
                (x.admin != null && x.admin.LastSeenUserName.ToLower().Contains(search)) ||
                (x.ban.Roles != null && x.ban.Roles.Any(r =>
                    r.RoleType.ToLower().Contains(search) ||
                    r.RoleId.ToLower().Contains(search) ||
                    FormatRole(r).ToLower().Contains(search))) ||
                (x.ban.Addresses != null && x.ban.Addresses.Any(a => FormatAddress(a.Address).ToLower().Contains(search))) ||
                (x.ban.Hwids != null && x.ban.Hwids.Any(h => h.HWId.ToImmutable().ToString().ToLower().Contains(search)))
            ).ToList();
        }

        if (!string.IsNullOrWhiteSpace(_model.RoleFilter))
        {
            var roleFilter = _model.RoleFilter.ToLower();
            result = result.Where(x =>
                x.ban.Roles != null && x.ban.Roles.Any(r =>
                    r.RoleType.ToLower().Contains(roleFilter) ||
                    r.RoleId.ToLower().Contains(roleFilter) ||
                    FormatRole(r).ToLower().Contains(roleFilter))
            ).ToList();
        }

        return result;
    }

    // ───────────────────────────────────────────────
    // Refresh
    // ───────────────────────────────────────────────

    private async Task Refresh()
    {
        await using var context = await ContextFactory!.CreateDbContextAsync();
        var entities = await GetRoleBansQueryEntities(context);

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

        _roleBansList = entities.Select(x =>
        {
            var roles = x.ban.Roles?.Select(FormatRole).ToArray() ?? [];
            return new RoleBanViewModel
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
                Admin = x.admin?.LastSeenUserName ?? "",
                Roles = roles,
                DisplayInfo = GetRoleDisplayInfo(roles),
                Rounds = x.ban.Rounds?.Select(round => round.RoundId).ToArray() ?? [],
                Active = x.ban.Unban == null && (!x.ban.ExpirationTime.HasValue || x.ban.ExpirationTime > now),
                IsRepealed = x.ban.Unban != null,
            };
        }).ToList();

        _confirmations.Clear();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnFilterSubmit(EditContext context)
    {
        await Refresh();
    }

    // ───────────────────────────────────────────────
    // Unban actions
    // ───────────────────────────────────────────────

    private async Task ShowConfirmation(int banId, bool active)
    {
        _confirmations[banId] = true;
        await InvokeAsync(StateHasChanged);
        await Task.Delay(3000);
        _confirmations[banId] = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ConfirmAction(int banId, bool active)
    {
        _confirmations[banId] = false;
        await InvokeAsync(StateHasChanged);

        if (active)
        {
            await UnbanRoleBan(banId);
        }

        await Refresh();
    }

    private async Task UnbanRoleBan(int banId)
    {
        await using var context = await ContextFactory!.CreateDbContextAsync();

        var ban = await context.Ban
            .Include(b => b.Unban)
            .SingleOrDefaultAsync(b => b.Id == banId);

        if (ban == null)
            return;

        if (ban.Unban != null)
            return;

        var authState = await AuthStateProvider!.GetAuthenticationStateAsync();
        var user = authState.User;
        var adminId = user.Claims.GetUserId();

        ban.Unban = new Unban
        {
            Ban = ban,
            UnbanningAdmin = adminId,
            UnbanTime = DateTime.UtcNow
        };

        await context.SaveChangesAsync();
    }

    // ───────────────────────────────────────────────
    // View model
    // ───────────────────────────────────────────────

    public class RoleBanViewModel
    {
        public int Id { get; set; }
        public string Reason { get; set; } = "";
        public DateTime BanTime { get; set; }
        public int[] Rounds { get; set; } = [];
        public DateTime? ExpirationTime { get; set; }
        public string Admin { get; set; } = "";
        public string[] PlayerNames { get; set; } = [];
        public string[] Roles { get; set; } = [];
        public RoleDisplayInfo DisplayInfo { get; set; } = new();
        public bool Active { get; set; }
        public bool IsRepealed { get; set; }

        //PII
        public string[] IPAddresses { get; set; } = [];
        public string[] Hwids { get; set; } = [];
        public string[] PlayerUserIds { get; set; } = [];

        public string PlayerNameDisplay => JoinValues(PlayerNames);
        public string RoleDisplay => JoinValues(Roles);
        public string RoundDisplay => JoinValues(Rounds);
        public string PlayerUserIdDisplay => JoinValues(PlayerUserIds);
        public string StatusLabel => Active ? "Active" : IsRepealed ? "Unbanned" : "Expired";
        public string ExpirationLabel => ExpirationTime.HasValue ? ExpirationTime.Value.ToString("yyyy-MM-dd HH:mm") : "Permanent";
        public string PrimaryName => PlayerNames.FirstOrDefault() ?? "Unknown player";
        public int AdditionalPlayerCount => Math.Max(PlayerNames.Length - 1, 0);
    }

    public class RoleDisplayInfo
    {
        public RoleDepartment[] FullDepartments { get; set; } = [];
        public string[] IndividualRoles { get; set; } = [];
        public int TotalCount { get; set; }
    }
}
