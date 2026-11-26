namespace DispatchGuard.Core.Entities.Base;

public abstract class EntityWithHomeLocation : Entity
{
    public int? HomeLocationId { get; protected set; }
    public Location? HomeLocation { get; protected set; }

    public virtual void SetHomeLocation(Location location)
    {
        ValidateLocation(location);
        HomeLocation = location;
        HomeLocationId = location.Id;
        MarkAsUpdated();
    }

    public virtual void ClearHomeLocation()
    {
        HomeLocation = null;
        HomeLocationId = null;
        MarkAsUpdated();
    }

    private static void ValidateLocation(Location? location)
    {
        EntityValidator.ValidateNotNull(location, nameof(location));
    }
}