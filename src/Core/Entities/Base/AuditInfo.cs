namespace DispatchGuard.Core.Entities.Base;

public sealed class AuditInfo
{
    public DateTime CreatedAt { get; }
    public DateTime? UpdatedAt { get; private set; }

    private AuditInfo()
    {
        CreatedAt = DateTime.UtcNow;
    }
    public AuditInfo(DateTime? createdAt = null, DateTime? updatedAt = null)
    {
        CreatedAt = createdAt ?? DateTime.UtcNow;
        UpdatedAt = updatedAt;
    }

    public void MarkAsUpdated() => UpdatedAt = DateTime.UtcNow;

    public override string ToString() => $"Created: {CreatedAt:dd-MM-yyyy HH:mm:ss}, Updated: {UpdatedAt:dd-MM-yyyy HH:mm:ss}";
}