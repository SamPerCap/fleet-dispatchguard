using System.Globalization;
using DispatchGuard.API.Endpoints;

namespace Infrastructure.Tests.Json;

/// <summary>A date of a query is a Madrid day (SPEC 0071 R4); with a time it is an instant (SPEC 0067).</summary>
public class DayOrInstantTests
{
    [Theory]
    [InlineData("2026-11-23", "2026-11-22T23:00:00")] // CET
    [InlineData("2026-07-06", "2026-07-05T22:00:00")] // CEST
    [InlineData("2026-03-29", "2026-03-28T23:00:00")] // the spring change is later that night
    [InlineData("2026-03-30", "2026-03-29T22:00:00")]
    [InlineData("2026-10-25", "2026-10-24T22:00:00")] // the autumn change is later that night
    [InlineData("2026-10-26", "2026-10-25T23:00:00")]
    [InlineData(" 2026-11-23 ", "2026-11-22T23:00:00")]
    [InlineData("2026-11-23T00:00:00", "2026-11-23T00:00:00")] // with a time and no zone, UTC (SPEC 0067)
    [InlineData("2026-11-23T00:00:00Z", "2026-11-23T00:00:00")]
    [InlineData("2026-11-22T23:00:00.000Z", "2026-11-22T23:00:00")] // what the rest of the web sends (toISOString)
    [InlineData("2026-11-23T00:00:00+01:00", "2026-11-22T23:00:00")]
    [InlineData("2026-10-25T02:30:00+02:00", "2026-10-25T00:30:00")] // repeated hour, first pass
    [InlineData("2026-10-25T02:30:00+01:00", "2026-10-25T01:30:00")] // and second
    public void TryParse_IsTheUtcInstant(string text, string expected)
    {
        Assert.True(DayOrInstant.TryParse(text, null, out var value));
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture), value.Utc);
        Assert.Equal(DateTimeKind.Utc, value.Utc.Kind);
    }

    [Theory]
    [InlineData("2026-03-23", "2026-03-30", 167)]
    [InlineData("2026-10-19", "2026-10-26", 169)]
    [InlineData("2026-11-23", "2026-11-30", 168)]
    public void TryParse_Week_LastsWhatTheMadridWeekLasts(string from, string to, int hours)
    {
        DayOrInstant.TryParse(from, null, out var start);
        DayOrInstant.TryParse(to, null, out var end);

        Assert.Equal(hours, (end.Utc - start.Utc).TotalHours);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("2026-02-30")]
    public void TryParse_NotADate_Fails(string? text) => Assert.False(DayOrInstant.TryParse(text, null, out _));

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("9999-12-31")]
    public void TryParse_Extremes_DoNotThrow(string text) => Assert.True(DayOrInstant.TryParse(text, null, out _));
}
