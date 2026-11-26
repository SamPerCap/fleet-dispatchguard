using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

/// <summary>
/// Reads include the trips with their driver and vehicle; <see cref="DeleteAsync"/> is <see cref="Booking.Cancel"/>, which
/// cancels the trips too.
/// </summary>
public class BookingRepository : EfRepository<Booking>, IBookingRepository
{
    public BookingRepository(DispatchGuardDbContext context) : base(context)
    {
    }

    protected override IQueryable<Booking> Query() => Set
        .Include(b => b.Trips).ThenInclude(t => t.AssignedDriver)
        .Include(b => b.Trips).ThenInclude(t => t.AssignedVehicle)
        .AsSplitQuery();

    public override Task DeleteAsync(Booking entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.Cancel();
        return UpdateAsync(entity, cancellationToken);
    }

    public async Task<IReadOnlyList<Booking>> GetByRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to < from) throw new ArgumentException("to must not be before from", nameof(to));

        var bookings = await Query()
            .Where(b => b.IsActive && b.Trips.Any(t => t.ScheduledWindow.Start < to && t.ScheduledWindow.End > from))
            .ToListAsync(cancellationToken);

        return bookings
            .OrderBy(b => b.Trips.Min(t => t.ScheduledWindow.Start))
            .ThenBy(b => b.Id)
            .ToList();
    }
}
