using System.Security.Claims;
using AcxiomCRM.Data;
using AcxiomCRM.Models;

namespace AcxiomCRM.Services;

public interface IAuditService
{
    Task LogAsync(string action, string entityName, string? entityId = null, string? details = null);
}

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(ApplicationDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task LogAsync(string action, string entityName, string? entityId = null, string? details = null)
    {
        var user = _http.HttpContext?.User;
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = user?.FindFirstValue(ClaimTypes.NameIdentifier),
            UserName = user?.Identity?.Name ?? "System",
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Details = details,
            Timestamp = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}