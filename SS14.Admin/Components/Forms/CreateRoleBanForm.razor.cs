using Content.Server.Database;
using Content.Shared.Database;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using SS14.Admin.Helpers;

namespace SS14.Admin.Components.Forms;

public partial class CreateRoleBanForm : ComponentBase
{
    public static readonly (int Minutes, string Label)[] DurationAdjustments =
    [
        (60, "1h"),
        (1440, "1d"),
        (10080, "7d"),
        (43200, "30d")
    ];

    [Inject]
    private PostgresServerDbContext DbContext { get; set; } = default!;

    [Inject]
    private BanHelper BanHelper { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    private CreateRoleBanModel _model = new();
    private List<string> _selectedRoles = new();
    private string? _manualRoleInput;
    private string? _roleError;
    private string? _errorMessage;
    private string? _successMessage;

    private string DurationSummary => BanDurationAdjuster.FormatMinutes(_model.LengthMinutes);

    // ───────────────────────────────────────────────
    // Department catalog for the role picker
    // ───────────────────────────────────────────────

    private static readonly Dictionary<string, string[]> _departmentCatalog = new()
    {
        ["Security"] = ["SecurityOfficer", "Detective", "Warden", "HeadOfSecurity", "Brigmedic", "Cadet"],
        ["Cargo"] = ["Quartermaster", "CargoTechnician", "SalvageSpecialist"],
        ["Science"] = ["ResearchDirector", "Scientist", "ResearchAssistant", "Roboticist"],
        ["Engineering"] = ["ChiefEngineer", "StationEngineer", "AtmosphericTechnician", "TechnicalAssistant"],
        ["Command"] = ["Captain", "HeadOfPersonnel", "NanotrasenRepresentative", "BlueShieldOfficer"],
        ["Medical"] = ["ChiefMedicalOfficer", "MedicalDoctor", "Chemist", "Paramedic", "MedicalIntern", "Psychologist"],
        ["Service"] = ["Barkeep", "Botanist", "Chef", "Clown", "Chaplain", "Janitor", "Lawyer", "Librarian", "Mime", "Musician", "Passenger", "Reporter", "ServiceWorker"],
    };

    // ───────────────────────────────────────────────
    // Role selection
    // ───────────────────────────────────────────────

    private void ToggleRole(string role)
    {
        _roleError = null;
        if (!_selectedRoles.Remove(role))
            _selectedRoles.Add(role);
    }

    private void RemoveRole(string role)
    {
        _selectedRoles.Remove(role);
    }

    private void ToggleDepartment(string department)
    {
        _roleError = null;
        if (!_departmentCatalog.TryGetValue(department, out var roles))
            return;

        if (IsDepartmentFullySelected(department))
        {
            foreach (var roleId in roles)
                _selectedRoles.Remove($"Job:{roleId}");
        }
        else
        {
            foreach (var roleId in roles)
            {
                var formatted = $"Job:{roleId}";
                if (!_selectedRoles.Contains(formatted))
                    _selectedRoles.Add(formatted);
            }
        }
    }

    private bool IsDepartmentFullySelected(string department)
    {
        if (!_departmentCatalog.TryGetValue(department, out var roles))
            return false;

        return roles.All(roleId => _selectedRoles.Contains($"Job:{roleId}"));
    }

    private void AddManualRole()
    {
        _roleError = null;
        if (string.IsNullOrWhiteSpace(_manualRoleInput))
        {
            _roleError = "Enter a role in the format RoleType:RoleId (e.g. Job:Captain)";
            return;
        }

        var input = _manualRoleInput.Trim();
        var parts = input.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            _roleError = "Role must be in the format RoleType:RoleId (e.g. Job:Captain or Antag:Traitor)";
            return;
        }

        var formatted = $"{parts[0]}:{parts[1]}";
        if (_selectedRoles.Contains(formatted))
        {
            _roleError = $"Role '{formatted}' is already added.";
            return;
        }

        _selectedRoles.Add(formatted);
        _manualRoleInput = null;
    }

    // ───────────────────────────────────────────────
    // Duration
    // ───────────────────────────────────────────────

    private void AdjustDuration(int minutesDelta)
    {
        _model.LengthMinutes = BanDurationAdjuster.AdjustMinutes(_model.LengthMinutes, minutesDelta);
    }

    // ───────────────────────────────────────────────
    // Badge styling
    // ───────────────────────────────────────────────

    private static string GetRoleBadgeStyle(string role)
    {
        var roleId = role.Contains(':') ? role[(role.IndexOf(':') + 1)..] : role;

        var dept = roleId switch
        {
            "SecurityOfficer" or "Detective" or "Warden" or "HeadOfSecurity" or "Brigmedic" or "Cadet"
                => "Security",
            "Quartermaster" or "CargoTechnician" or "SalvageSpecialist"
                => "Cargo",
            "ResearchDirector" or "Scientist" or "ResearchAssistant" or "Roboticist"
                => "Science",
            "ChiefEngineer" or "StationEngineer" or "AtmosphericTechnician" or "TechnicalAssistant"
                => "Engineering",
            "Captain" or "HeadOfPersonnel" or "NanotrasenRepresentative" or "BlueShieldOfficer"
                => "Command",
            "ChiefMedicalOfficer" or "MedicalDoctor" or "Chemist" or "Paramedic" or "MedicalIntern" or "Psychologist"
                => "Medical",
            "Barkeep" or "Botanist" or "Chef" or "Clown" or "Chaplain" or "Janitor" or "Lawyer" or "Librarian" or "Mime" or "Musician" or "Passenger" or "Reporter" or "ServiceWorker"
                => "Service",
            _ => "Other"
        };

        return dept switch
        {
            "Security" => "background-color: color-mix(in srgb, var(--ss14-red) 18%, transparent); color: var(--ss14-red-dark); border-color: color-mix(in srgb, var(--ss14-red) 45%, transparent);",
            "Cargo" => "background-color: color-mix(in srgb, var(--color-warning) 18%, var(--ss14-white)); color: #8A5A2B; border-color: color-mix(in srgb, var(--color-warning) 40%, transparent);",
            "Science" => "background-color: color-mix(in srgb, #8B5CF6 18%, transparent); color: #6D28D9; border-color: color-mix(in srgb, #8B5CF6 40%, transparent);",
            "Engineering" => "background-color: color-mix(in srgb, var(--color-warning) 22%, transparent); color: #92400E; border-color: color-mix(in srgb, var(--color-warning) 45%, transparent);",
            "Service" => "background-color: color-mix(in srgb, var(--color-success) 18%, transparent); color: #047857; border-color: color-mix(in srgb, var(--color-success) 40%, transparent);",
            "Command" => "background-color: color-mix(in srgb, var(--color-info) 18%, #0F172A 10%); color: #1E3A8A; border-color: color-mix(in srgb, var(--color-info) 45%, transparent);",
            "Medical" => "background-color: color-mix(in srgb, var(--ss14-white) 88%, #E5E7EB); color: #1F2937; border-color: color-mix(in srgb, var(--border-light) 85%, transparent);",
            _ => "background-color: color-mix(in srgb, var(--ss14-light-gray) 82%, transparent); color: var(--ss14-text-primary-light); border-color: color-mix(in srgb, var(--border-light) 85%, transparent);",
        };
    }

    // ───────────────────────────────────────────────
    // Submit
    // ───────────────────────────────────────────────

    private async Task Submit(EditContext editContext)
    {
        _errorMessage = null;
        _successMessage = null;
        _roleError = null;

        // Validate roles
        if (_selectedRoles.Count == 0)
        {
            _errorMessage = "Must select at least one role.";
            return;
        }

        // Validate "use latest" requires an identifier
        if ((_model.UseLatestHwid || _model.UseLatestIp) &&
            (_model.IP == null && _model.NameOrUid == null && _model.HWid == null))
        {
            _errorMessage = "When using latest HWID or IP, you must specify at least one of IP, HWID, or Name/UserID.";
            return;
        }

        // Resolve latest IP/HWID
        string? ipAddr = _model.IP;
        string? hwid = _model.HWid;

        if (_model.UseLatestIp || _model.UseLatestHwid)
        {
            var lastInfo = await BanHelper.GetLastPlayerInfo(_model.NameOrUid);
            if (lastInfo == null)
            {
                _errorMessage = "Unable to retrieve latest player info for the provided Name/UID.";
                return;
            }
            if (_model.UseLatestIp)
                ipAddr = lastInfo.Value.address.ToString();
            if (_model.UseLatestHwid)
                hwid = lastInfo.Value.hwid?.ToString();
        }

        // Build BanRole entities from selected roles
        var banRoles = new List<BanRole>();
        foreach (var role in _selectedRoles)
        {
            var parts = role.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                _errorMessage = $"Invalid role format: '{role}'. Expected RoleType:RoleId.";
                return;
            }
            banRoles.Add(new BanRole { RoleType = parts[0], RoleId = parts[1] });
        }

        // Create ban entity
        var ban = new Ban
        {
            Type = BanType.Role,
            Roles = banRoles,
            Hidden = _model.Hidden,
            Severity = _model.Severity,
        };

        // Fill common fields
        var error = await BanHelper.FillBanCommon(
            ban,
            _model.NameOrUid,
            ipAddr,
            hwid,
            _model.LengthMinutes,
            _model.Reason);

        if (error != null)
        {
            _errorMessage = error;
            return;
        }

        // Verify banning admin exists in player table
        var isAdminReal = await BanHelper.GetLastPlayerInfo(ban.BanningAdmin.ToString());
        if (isAdminReal == null)
        {
            _errorMessage = "Banning admin is not in the player database.";
            return;
        }

        // Persist
        DbContext.Ban.Add(ban);
        await DbContext.SaveChangesAsync();

        _successMessage = "Role ban created successfully.";
        NavigationManager.NavigateTo("/rolebans");
    }

    // ───────────────────────────────────────────────
    // Model
    // ───────────────────────────────────────────────

    private class CreateRoleBanModel
    {
        public string? NameOrUid { get; set; }
        public string? IP { get; set; }
        public string? HWid { get; set; }
        public bool UseLatestIp { get; set; }
        public bool UseLatestHwid { get; set; }
        public int LengthMinutes { get; set; }
        public string Reason { get; set; } = "";
        public bool Hidden { get; set; }
        public NoteSeverity Severity { get; set; }
    }
}
