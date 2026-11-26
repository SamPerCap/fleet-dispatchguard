using System.Data.Common;
using System.Diagnostics;
using DispatchGuard.Core.Interfaces;
using DispatchGuard.DemoData;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.API.Demo;

/// <summary>Leaves the demo of the day: deletes what there is and loads it again, through the API itself (SPEC 0086, ADR 0025).</summary>
public sealed class DemoRebuild(IDemoDataReset demoData, IVisitLog visits, OwnApi api, ILogger<DemoRebuild> logger)
{
    private const int Attempts = 2;

    /// <summary>The host ends the request at 300 s: an attempt that starts later would be cut in the middle.</summary>
    private static readonly TimeSpan LatestRetry = TimeSpan.FromSeconds(130);

    /// <returns>Null if another one is running.</returns>
    /// <param name="caller">Its host and its secret are the ones of the calls to the API.</param>
    public async Task<SeedReport?> RunAsync(HttpRequest caller, CancellationToken cancellationToken)
    {
        await using var reservation = await demoData.TryReserveAsync(cancellationToken);
        if (reservation is null)
        {
            return null;
        }

        await DeleteExpiredRecordsAsync(cancellationToken);
        var clock = Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await demoData.ResetAsync(cancellationToken);
                using var client = api.CreateClient(caller);
                var report = await new ApiSeeder(client).SeedAsync(DateTime.UtcNow, cancellationToken);
                logger.LogInformation("The demo was rebuilt in {Seconds:0} s: {Report}", clock.Elapsed.TotalSeconds, report);
                return report;
            }
            catch (Exception exception) when (CanBeTemporary(exception) && attempt < Attempts && clock.Elapsed < LatestRetry)
            {
                // From the beginning: someone who was using the demo may have taken what the plan needs.
                logger.LogWarning(exception, "The demo could not be rebuilt; trying again");
            }
        }
    }

    /// <summary>Here because it is the only thing that runs every day (SPEC 0085 R8). The demo does not depend on it.</summary>
    private async Task DeleteExpiredRecordsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await visits.DeleteExpiredAsync(cancellationToken);
        }
        catch (Exception exception) when (CanBeTemporary(exception))
        {
            logger.LogError(exception, "What expired of the visits could not be deleted");
        }
    }

    private static bool CanBeTemporary(Exception exception) =>
        exception is InvalidOperationException or HttpRequestException or DbException or DbUpdateException or TimeoutException;
}
