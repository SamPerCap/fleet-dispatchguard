// Demo data loader. Usage:
//   dotnet run --project tools/SampleData -- [--api http://localhost:5171] [--now 2026-11-18T10:00:00Z | --week 2026-11-16] [--reset-key <k>]
// Loads three bases and five weeks around "now" (default: the current instant) with today's incidents relative to it.
// --week <monday> anchors "now" on the Thursday of that week at 10:00 Madrid time.
// --reset-key wipes the demo data first (POST /api/admin/demo/reset, ADR 0014); the key is the API's
// Demo:ResetKey, best passed from an environment variable, never committed. It also exempts the load from the request limits
// (SPEC 0089): without it, only an API in Development or Testing on this machine accepts the whole demo.
// Safe to run more than once on the same day; on another day, run it with --reset-key (the timetable moves with today).
// --check loads nothing: it checks that a deployed demo is up and closed (SPEC 0091), and ends with an error if it is not.
using System.Globalization;
using DispatchGuard.DemoData;
using DispatchGuard.SampleData;

var api = "http://localhost:5171";
string? resetKey = null;
var now = DateTime.UtcNow;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--api") api = args[i + 1];
    if (args[i] == "--reset-key") resetKey = args[i + 1];
    if (args[i] == "--now")
    {
        if (!DateTime.TryParse(args[i + 1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out now))
        {
            Console.Error.WriteLine($"--now is not a valid date: {args[i + 1]}");
            return 1;
        }
    }
    if (args[i] == "--week")
    {
        if (!DateOnly.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var monday) || monday.DayOfWeek != DayOfWeek.Monday)
        {
            Console.Error.WriteLine($"--week must be a Monday (yyyy-mm-dd): {args[i + 1]}");
            return 1;
        }
        now = DemoClock.Utc(monday.AddDays(3), new TimeOnly(10, 0));
    }
}

using var http = new HttpClient { BaseAddress = new Uri(api), Timeout = TimeSpan.FromSeconds(60) };
if (args.Contains("--check"))
{
    // The firewall of Vercel takes this for a bot: the project gives a secret to let it through (deploy/README.md).
    if (Environment.GetEnvironmentVariable("VERCEL_AUTOMATION_BYPASS_SECRET") is { Length: > 0 } bypass)
    {
        http.DefaultRequestHeaders.Add("x-vercel-protection-bypass", bypass);
    }

    Console.WriteLine($"Checking {api}…");
    var results = await new DeploymentCheck(http).RunAsync();
    foreach (var result in results) Console.WriteLine($"{(result.Passed ? "OK  " : "FAIL")} {result.Name}: {result.Detail}");
    return results.All(result => result.Passed) ? 0 : 1;
}

Console.WriteLine($"Seeding 3 bases around {DemoClock.Local(now):yyyy-MM-dd HH:mm} (Madrid time) at {api}…");
Console.WriteLine($"Source of the stops: {DemoDataset.Source}");
try
{
    if (resetKey is not null)
    {
        http.DefaultRequestHeaders.Add("X-Demo-Reset-Key", resetKey);
        using var response = await http.PostAsync("/api/admin/demo/reset", null);
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"The reset failed: {(int)response.StatusCode}");
            return 1;
        }
        Console.WriteLine("Demo data deleted.");
    }
    var started = System.Diagnostics.Stopwatch.StartNew();
    var report = await new ApiSeeder(http).SeedAsync(now);
    Console.WriteLine($"Created: {report.BasesCreated} bases, {report.LocationsCreated} locations, {report.DriversCreated} drivers, {report.VehiclesCreated} vehicles, " +
        $"{report.BookingsCreated} bookings, {report.TripsCreated} services. Allocated: {report.TripsAllocated}. Rejected attempts: {report.AttemptsRejected}. " +
        $"Leaves and workshops: {report.LeavesCreated}. ({started.Elapsed.TotalSeconds:0} s)");
    return 0;
}
catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
