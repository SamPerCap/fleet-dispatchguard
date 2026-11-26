namespace DispatchGuard.Core.Enums;

public enum ViolationSeverity
{
    /// <summary>Blocks the allocation.</summary>
    Critical = 0,

    /// <summary>Does not block: the trip proceeds and the violation is recorded.</summary>
    Warning = 1,
}
