using DispatchGuard.Core.ValueObjects;
using Core.Interfaces.ValueObjects;

namespace DispatchGuard.Core.Entities.Base;

public abstract class EntityWithUnavailabilities<T> : EntityWithHomeLocation
    where T : IUnavailability
{
    private readonly List<T> _unavailabilities = [];
    public IReadOnlyCollection<T> Unavailabilities => _unavailabilities.AsReadOnly();

    public virtual void AddUnavailability(T unavailability)
    {
        if (unavailability == null)
        {
            throw new ArgumentNullException(nameof(unavailability));
        }

        _unavailabilities.Add(unavailability);
        MarkAsUpdated();
    }

    /// <summary>Through the resource, so that its own timestamp changes too.</summary>
    public virtual void ChangeUnavailability(T unavailability, Action<T> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!_unavailabilities.Contains(unavailability))
        {
            throw new ArgumentException("The unavailability belongs to another resource", nameof(unavailability));
        }

        change(unavailability);
        MarkAsUpdated();
    }

    public virtual void RemoveUnavailability(T unavailability)
    {
        if (unavailability == null)
        {
            throw new ArgumentNullException(nameof(unavailability));
        }

        _unavailabilities.Remove(unavailability);
        MarkAsUpdated();
    }

    public virtual IEnumerable<T> GetUnavailabilitiesInRange(DateTime start, DateTime end)
    {
        var range = new TimeWindow(start, end);
        return _unavailabilities.Where(u => u.OverlapsWith(range));
    }

    public virtual bool IsAvailableDuring(TimeWindow window)
    {
        if (!IsActive)
        {
            return false;
        }

        return !GetUnavailabilitiesInRange(window.Start, window.End).Any();
    }

    public int GetUnavailabilityCount() => _unavailabilities.Count;

    public bool HasUnavailabilities() => _unavailabilities.Count > 0;

    public virtual void ClearUnavailabilities()
    {
        if (_unavailabilities.Count > 0)
        {
            _unavailabilities.Clear();
            MarkAsUpdated();
        }
    }
}