using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>Real driving hours, measured with the compliance engine's own calculation (SPEC 0047).</summary>
public class DriverHoursTests
{
    private static readonly DateTime Monday = new(2026, 11, 16);

    private static DrivingPeriod Period(DateTime start, double hours) => new(1, start, start.AddHours(hours));

    [Fact]
    public void MeasureHours_TwoDaysOfWork_SumsWorkingDayWeekAndTwoWeeks()
    {
        var history = new[] { Period(Monday.AddHours(8), 3), Period(Monday.AddDays(1).AddHours(8), 4) };

        var hours = LegalComplianceEvaluation.MeasureHours(history, Monday.AddDays(1).AddHours(20));

        Assert.Equal(4, hours.Today.TotalHours);
        Assert.Equal(7, hours.ThisWeek.TotalHours);
        Assert.Equal(7, hours.LastTwoWeeks.TotalHours);
        Assert.Equal(DrivingLimits.MaxDailyDriving, DriverHours.MaxDaily);
        Assert.Equal(DrivingLimits.MaxWeeklyDriving, DriverHours.MaxWeekly);
        Assert.Equal(DrivingLimits.MaxBiWeeklyDriving, DriverHours.MaxBiWeekly);
    }

    [Fact]
    public void MeasureHours_PreviousWeek_CountsInTwoWeeksNotInWeek()
    {
        var history = new[] { Period(Monday.AddDays(-3).AddHours(8), 5), Period(Monday.AddHours(8), 2) };

        var hours = LegalComplianceEvaluation.MeasureHours(history, Monday.AddHours(12));

        Assert.Equal(2, hours.ThisWeek.TotalHours);
        Assert.Equal(7, hours.LastTwoWeeks.TotalHours);
    }

    [Fact]
    public void MeasureHours_AfterWeeklyRest_CountsOnlySinceItEnded()
    {
        // 5h on Thursday, then ≥ 45h gap, 3h on Monday: the weekly rest ends Monday 08:00.
        var history = new[] { Period(Monday.AddDays(-4).AddHours(8), 5), Period(Monday.AddHours(8), 3) };

        var hours = LegalComplianceEvaluation.MeasureHours(history, Monday.AddHours(18));

        Assert.Equal(Monday.AddHours(8), hours.LastWeeklyRestEnd);
        Assert.Equal(3, hours.SinceLastWeeklyRest.TotalHours);
        Assert.False(DriverHoursDto.From(1, Monday.AddHours(18), hours).WeeklyRestInProgress);
    }

    [Fact]
    public void MeasureHours_RestStillRunning_IsInProgressNotEnded()
    {
        // 3h on Monday and nothing since: on Wednesday the rest is still running.
        var asOf = Monday.AddDays(2).AddHours(12);

        var dto = DriverHoursDto.From(1, asOf, LegalComplianceEvaluation.MeasureHours([Period(Monday.AddHours(8), 3)], asOf));

        Assert.True(dto.WeeklyRestInProgress);
    }

    [Fact]
    public void MeasureHours_NoWeeklyRestInHistory_CountsTheWholeWindow()
    {
        // Every day 08:00–12:00 for 7 days: gaps of 20h never reach 24h.
        var history = Enumerable.Range(0, 7).Select(d => Period(Monday.AddDays(-6 + d).AddHours(8), 4)).ToArray();

        var hours = LegalComplianceEvaluation.MeasureHours(history, Monday.AddHours(13));

        Assert.Null(hours.LastWeeklyRestEnd);
        Assert.Equal(28, hours.SinceLastWeeklyRest.TotalHours);
    }

    [Fact]
    public void MeasureHours_PeriodStillRunning_IsNotCounted()
    {
        var history = new[] { Period(Monday.AddHours(8), 4) };

        var hours = LegalComplianceEvaluation.MeasureHours(history, Monday.AddHours(10));

        Assert.Equal(TimeSpan.Zero, hours.Today);
    }

    // Mutation testing (SPEC 0099): a period that ends exactly at the instant asked for is finished, so it counts.
    [Fact]
    public void MeasureHours_PeriodEndingExactlyNow_CountsSinceTheWeeklyRest()
    {
        var history = new[] { Period(Monday.AddHours(8), 2) };

        var hours = LegalComplianceEvaluation.MeasureHours(history, Monday.AddHours(10));

        Assert.Equal(2, hours.SinceLastWeeklyRest.TotalHours);
    }

    [Fact]
    public void MeasureHours_NoHistory_NamesTheArgument() =>
        Assert.Throws<ArgumentNullException>("history", () => LegalComplianceEvaluation.MeasureHours(null!, Monday));
}
