using DispatchGuard.Core.Interfaces;

namespace DispatchGuard.API.Session;

public sealed class HttpCurrentSession(IHttpContextAccessor accessor) : ICurrentSession
{
    public string? Id => AnonymousSession.IdOf(accessor.HttpContext?.User);
}
