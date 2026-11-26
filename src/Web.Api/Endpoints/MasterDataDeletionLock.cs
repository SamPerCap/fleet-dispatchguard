namespace DispatchGuard.API.Endpoints;

/// <summary>
/// In the public demo every visitor shares the same data and there are no roles, so one could leave the others without
/// fleet. The operation stays: it is only turned off by configuration (SPEC 0088).
/// </summary>
public sealed record MasterDataDeletionLock(bool IsOn)
{
    private const string Setting = "Demo:LockMasterDataDeletion";

    /// <summary>Read at start-up: a value that is not a boolean must stop it, not leave the demo unlocked.</summary>
    public static MasterDataDeletionLock From(IConfiguration configuration) => new(configuration.GetValue(Setting, false));
}

internal static class MasterDataDeletionLockExtensions
{
    public static RouteHandlerBuilder BlockedByMasterDataDeletionLock(this RouteHandlerBuilder endpoint) =>
        endpoint
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddEndpointFilter(async (context, next) =>
                context.HttpContext.RequestServices.GetRequiredService<MasterDataDeletionLock>().IsOn
                    ? TypedResults.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: Texts.DeletionLockedTitle().In(CurrentLanguage.Value),
                        detail: Texts.DeletionLocked().In(CurrentLanguage.Value))
                    : await next(context));
}
