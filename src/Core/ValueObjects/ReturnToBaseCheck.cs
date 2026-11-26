using DispatchGuard.Core.Enums;

namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// Whether a driver returned to, or departed from, the home location within the 4-week window of Amendment 2020/1054, and the
/// last qualifying trip (SPEC 0016).
/// </summary>
public sealed class ReturnToBaseCheck
{
    public ReturnToBaseStatus Status { get; }

    public DateTime? LastReturnDate { get; }

    public int? DaysSinceLastReturn { get; }

    /// <summary>Audit only: decisions rely on <see cref="Status"/> alone.</summary>
    public DateTime CheckedAt { get; }

    public bool IsCompliant => Status == ReturnToBaseStatus.Compliant;

    private ReturnToBaseCheck(ReturnToBaseStatus status, DateTime? lastReturnDate, int? daysSinceLastReturn)
    {
        Status = status;
        LastReturnDate = lastReturnDate;
        DaysSinceLastReturn = daysSinceLastReturn;
        CheckedAt = DateTime.UtcNow;
    }

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="daysSinceLastReturn"/> is negative.</exception>
    public static ReturnToBaseCheck Compliant(DateTime lastReturnDate, int daysSinceLastReturn)
    {
        if (daysSinceLastReturn < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(daysSinceLastReturn), "Days since last return cannot be negative.");
        }

        return new ReturnToBaseCheck(ReturnToBaseStatus.Compliant, lastReturnDate, daysSinceLastReturn);
    }

    /// <exception cref="ArgumentException">
    /// Only one of <paramref name="lastReturnDate"/> and <paramref name="daysSinceLastReturn"/> has a value.
    /// </exception>
    public static ReturnToBaseCheck NonCompliant(DateTime? lastReturnDate, int? daysSinceLastReturn)
    {
        if (lastReturnDate.HasValue != daysSinceLastReturn.HasValue)
        {
            throw new ArgumentException("lastReturnDate and daysSinceLastReturn must both be null or both have a value.");
        }

        return new ReturnToBaseCheck(ReturnToBaseStatus.NonCompliant, lastReturnDate, daysSinceLastReturn);
    }

    /// <summary>No driver or no home location.</summary>
    public static ReturnToBaseCheck NotVerifiable() =>
        new(ReturnToBaseStatus.NotVerifiable, null, null);
}
