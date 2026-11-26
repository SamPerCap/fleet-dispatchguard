namespace DispatchGuard.Core.Enums;

public enum AttemptOrigin
{
    /// <summary>A person chose the pair, also when changing a line of a proposal. Every attempt before SPEC 0062.</summary>
    Manual = 0,

    /// <summary>Applied exactly as proposed.</summary>
    Recommended = 1
}
