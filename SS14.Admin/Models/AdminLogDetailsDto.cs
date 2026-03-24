using System.Text.Json;
using Content.Shared.Database;

namespace SS14.Admin.Models;

public class AdminLogDetailsDto
{
    public int RoundId { get; set; }
    public int Id { get; set; }
    public LogType Type { get; set; }
    public LogImpact Impact { get; set; }
    public DateTime Date { get; set; }
    public string Message { get; set; } = string.Empty;
    public JsonDocument Json { get; set; } = JsonDocument.Parse("{}");
    public string? ServerName { get; set; }
    public List<AdminLogPlayerDto> Players { get; set; } = new();
}

public class AdminLogPlayerDto
{
    public Guid PlayerUserId { get; set; }
    public string PlayerUsername { get; set; } = string.Empty;
}
