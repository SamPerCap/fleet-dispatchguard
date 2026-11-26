using DispatchGuard.Application.Services.Rules;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class RulesEndpoints
{
    public static void MapRulesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/rules", GetRules).WithName("Rules").WithTags("Rules");
    }

    private static Ok<IReadOnlyList<RuleGroupDto>> GetRules() => TypedResults.Ok(RulesCatalog.Build());
}
