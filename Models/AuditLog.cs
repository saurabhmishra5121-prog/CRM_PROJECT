namespace AcxiomCRM.Models;

public class AuditLog
{
    public int AuditLogId { get; set; }
    public string? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;      // e.g. "Create Lead"
    public string EntityName { get; set; } = string.Empty;  // Lead / Customer / Opportunity / Auth
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}