namespace DispatchGuard.Core.Interfaces;

/// <summary>
/// The anonymous session of whoever made the request (SPEC 0084). It identifies a browser, not a person: the name that goes
/// with an attempt is declared, this is what the server recognised.
/// </summary>
public interface ICurrentSession
{
    /// <summary>Null outside a request, or in one that has no session.</summary>
    string? Id { get; }
}
