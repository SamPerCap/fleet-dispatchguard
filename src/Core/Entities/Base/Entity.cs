namespace DispatchGuard.Core.Entities.Base;

public abstract class Entity
{
    public int Id { get; protected set; }

    public AuditInfo Audit { get; protected set; } = new();

    public bool IsActive { get; protected set; }

    protected Entity()
    {
        IsActive = true;
    }

    public virtual void Deactivate()
    {
        IsActive = false;
        Audit.MarkAsUpdated();
    }

    public virtual void Reactivate()
    {
        IsActive = true;
        Audit.MarkAsUpdated();
    }

    protected void MarkAsUpdated() => Audit.MarkAsUpdated();
}