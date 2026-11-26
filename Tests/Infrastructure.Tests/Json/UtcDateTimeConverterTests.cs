using System.Text.Json;
using DispatchGuard.API.Json;

namespace Infrastructure.Tests.Json;

/// <summary>Checks the Kind too, so that it fails without the conversion even on a server in UTC (the CI) (SPEC 0067).</summary>
public class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new UtcDateTimeConverter() } };

    private sealed record Body(DateTime At, DateTime? Maybe);

    [Theory]
    [InlineData("2026-11-23T10:00:00Z", 10, 0, DateTimeKind.Utc)]
    [InlineData("2026-11-23T10:00:00", 10, 0, DateTimeKind.Unspecified)]
    [InlineData("2026-11-23T10:00:00+00:00", 10, 0, DateTimeKind.Utc)]
    [InlineData("2026-11-23T11:00:00+01:00", 10, 0, DateTimeKind.Utc)]
    [InlineData("2026-11-23T05:00:00-05:00", 10, 0, DateTimeKind.Utc)]
    [InlineData("2026-11-23T15:45:00+05:45", 10, 0, DateTimeKind.Utc)]
    [InlineData("2026-11-24T00:30:00+14:00", 10, 30, DateTimeKind.Utc)] // another day at its offset
    public void Read_IsTheUtcInstant(string text, int hour, int minute, DateTimeKind kind)
    {
        var body = JsonSerializer.Deserialize<Body>($$"""{"At":"{{text}}","Maybe":"{{text}}"}""", Options)!;

        Assert.Equal((new DateTime(2026, 11, 23, hour, minute, 0), kind), (body.At, body.At.Kind));
        Assert.Equal((body.At, kind), (body.Maybe!.Value, body.Maybe.Value.Kind));
    }

    /// <summary>25/10/2026, 02:30 in Madrid happens twice, one hour apart.</summary>
    [Fact]
    public void Read_RepeatedHour_KeepsBothInstants()
    {
        var first = JsonSerializer.Deserialize<DateTime>("\"2026-10-25T02:30:00+02:00\"", Options);
        var second = JsonSerializer.Deserialize<DateTime>("\"2026-10-25T02:30:00+01:00\"", Options);

        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0), first);
        Assert.Equal(TimeSpan.FromHours(1), second - first);
    }

    [Theory]
    [InlineData("{\"At\":\"23/11/2026\"}")]
    [InlineData("{\"At\":\"2026-11-23T10:00:00+25:00\"}")]
    [InlineData("{\"At\":null}")]
    [InlineData("{\"At\":5}")]
    public void Read_InvalidDate_IsAJsonError_AsWithoutTheConverter(string json)
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<Body>(json));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<Body>(json, Options));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Write_IsTheSameAsWithoutTheConverter(DateTimeKind kind)
    {
        var body = new Body(new DateTime(2026, 11, 23, 10, 0, 0, 125, kind), null);

        Assert.Equal(JsonSerializer.Serialize(body), JsonSerializer.Serialize(body, Options));
    }
}
