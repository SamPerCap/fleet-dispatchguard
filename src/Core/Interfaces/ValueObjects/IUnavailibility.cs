namespace Core.Interfaces.ValueObjects;

using DispatchGuard.Core.ValueObjects;

public interface IUnavailability
{
    TimeWindow Period { get; }
    bool OverlapsWith(TimeWindow window);
}