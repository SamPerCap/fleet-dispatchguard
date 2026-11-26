using DispatchGuard.Core.Enums;

namespace DispatchGuard.DemoData;

public enum BookingKind { RegularLine, Excursion, Transfer, Circuit, Shuttle, Reinforcement, Extra }

/// <summary><paramref name="Key"/> groups its trips in the plan.</summary>
public sealed record PlannedBooking(string Key, BookingKind Kind, string CustomerName, string Description, string? Notes = null);

/// <summary>
/// The seeder asks the API to allocate the driver and vehicle; both null = left uncovered on purpose.
/// Times in UTC. <see cref="Description"/> is unique and carries the date: it is the idempotency key.
/// </summary>
public sealed record PlannedTrip(
    string Description, string BaseKey, string OriginKey, string DestinationKey, DateTime Start, DateTime End,
    int RequiredSeats, ServiceType ServiceType, PlannedBooking Booking, string? DriverId, string? VehiclePlate)
{
    public TimeSpan Duration => End - Start;
    public bool Covered => DriverId is not null;
}

/// <summary>Why a planned wrong attempt must be rejected by the API.</summary>
public enum RejectionKind { VehicleTooSmall, DriverBusy, VehicleBusy, ContinuousDriving }

/// <summary>A realistic wrong allocation tried right before the good one of the same trip.</summary>
public sealed record PlannedRejection(string TripDescription, RejectionKind Kind, string DriverId, string VehiclePlate);

public sealed record PlannedDriverLeave(string DriverId, DateTime Start, DateTime End, UnavailabilityType Type, string Notes);

public sealed record PlannedVehicleLeave(string VehiclePlate, DateTime Start, DateTime End, VehicleUnavailabilityType Type, string Notes);

public sealed record DemoPlan(
    DateTime Now, DateOnly Today, IReadOnlyList<PlannedTrip> Trips, IReadOnlyList<PlannedRejection> Rejections,
    IReadOnlyList<PlannedDriverLeave> DriverLeaves, IReadOnlyList<PlannedVehicleLeave> VehicleLeaves)
{
    public const int HalfSpanDays = 7;

    /// <summary>So that the 28-day history of every driver and vehicle is full (SPEC 0061).</summary>
    public const int PastDays = 28;

    /// <summary>Of the near-limit driver: back to the week before last.</summary>
    public const int HeavyHistoryDays = 13;

    /// <summary>
    /// The planning margins are stricter than CE 561/2006 (see <see cref="DemoPlanner"/>), so every allocation should pass; the API
    /// decides anyway.
    /// </summary>
    public static DemoPlan Build(DateTime nowUtc)
    {
        if (nowUtc.Kind == DateTimeKind.Local) nowUtc = nowUtc.ToUniversalTime();
        nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var today = DemoClock.LocalDate(nowUtc);

        var trips = new List<PlannedTrip>();
        var rejections = new List<PlannedRejection>();
        var driverLeaves = new List<PlannedDriverLeave>();
        var vehicleLeaves = new List<PlannedVehicleLeave>();
        foreach (var demoBase in DemoDataset.Bases)
        {
            var planner = new DemoPlanner(demoBase, today);
            var requests = DemoRequests.For(demoBase, today, nowUtc, planner);
            var planned = planner.Assign(requests);
            trips.AddRange(planned);
            rejections.AddRange(DemoRejections.Pick(demoBase, planned, today));
            driverLeaves.Add(new PlannedDriverLeave(planner.SickDriver.IdentificationNumber,
                DemoClock.Utc(today, new TimeOnly(17, 0)), DemoClock.Utc(today.AddDays(3), TimeOnly.MinValue),
                UnavailabilityType.SickLeave, $"Baja médica comunicada hoy ({demoBase.Name}, demo)"));
            vehicleLeaves.Add(new PlannedVehicleLeave(planner.WorkshopVehicle.LicensePlate,
                DemoClock.Utc(today, TimeOnly.MinValue), DemoClock.Utc(today.AddDays(3), TimeOnly.MinValue),
                VehicleUnavailabilityType.Repair, $"Avería en el embrague: en taller ({demoBase.Name}, demo)"));
            // Past absences, so that the cards show them too; the planner leaves those people out on those days.
            driverLeaves.Add(new PlannedDriverLeave(planner.VacationDriver.IdentificationNumber,
                DemoClock.Utc(planner.VacationFrom, TimeOnly.MinValue), DemoClock.Utc(planner.VacationTo, TimeOnly.MinValue),
                UnavailabilityType.Vacation, $"Vacaciones del {planner.VacationFrom:dd/MM} al {planner.VacationTo.AddDays(-1):dd/MM} ({demoBase.Name}, demo)"));
            vehicleLeaves.Add(new PlannedVehicleLeave(planner.InspectionVehicle.LicensePlate,
                DemoClock.Utc(planner.InspectionDay, TimeOnly.MinValue), DemoClock.Utc(planner.InspectionDay.AddDays(1), TimeOnly.MinValue),
                VehicleUnavailabilityType.TechnicalInspection, $"ITV periódica del {planner.InspectionDay:dd/MM} ({demoBase.Name}, demo)"));
        }
        return new DemoPlan(nowUtc, today, trips.OrderBy(t => t.Start).ThenBy(t => t.Description, StringComparer.Ordinal).ToList(),
            rejections, driverLeaves, vehicleLeaves);
    }
}

/// <summary>Madrid local time (València shares the zone) ⇄ UTC.</summary>
public static class DemoClock
{
    public static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    public static DateTime Utc(DateOnly day, TimeOnly time) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(time), Madrid), DateTimeKind.Utc);

    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Madrid);

    public static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(Local(utc));

    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}

/// <summary>Still without people; <see cref="FixedDriver"/> and <see cref="FixedVehicle"/> pin them.</summary>
public sealed record TripRequest(
    string Description, string OriginKey, string DestinationKey, DateTime Start, DateTime End, int RequiredSeats,
    ServiceType ServiceType, PlannedBooking Booking, string? FixedDriver = null, string? FixedVehicle = null, bool LeaveUncovered = false);

/// <summary>The services of one base, still without people.</summary>
internal static class DemoRequests
{
    private static readonly string[] Hotels =
        ["Hotel Castellana Centro (demo)", "Hotel Puerta del Sol (demo)", "Agencia Viajes Iberia Sur (demo)", "Hotel Plaza Mayor (demo)"];

    public static List<TripRequest> For(DemoBase demoBase, DateOnly today, DateTime nowUtc, DemoPlanner planner)
    {
        var list = new List<TripRequest>();
        for (var d = today.AddDays(-DemoPlan.PastDays); d <= today.AddDays(DemoPlan.HalfSpanDays); d = d.AddDays(1))
        {
            var weekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            foreach (var line in demoBase.Lines.Where(l => !weekend || l.Weekend))
            {
                list.Add(LineService(demoBase, line, d, afternoon: false));
                if (line.Afternoon && !weekend) list.Add(LineService(demoBase, line, d, afternoon: true));
            }

            list.Add(Transfer(demoBase, d, demoBase.HubKey, demoBase.AirportKey, new TimeOnly(12, 30), "mañana"));
            list.Add(Transfer(demoBase, d, demoBase.AirportKey, demoBase.HubKey, new TimeOnly(19, 30), "tarde"));

            if (d.DayOfWeek is DayOfWeek.Wednesday or DayOfWeek.Saturday)
                list.AddRange(Excursion(demoBase, d));
        }

        list.AddRange(Circuit(demoBase, today.AddDays(-5), partial: false));
        list.AddRange(Circuit(demoBase, today.AddDays(-19), partial: false)); // One more, in the history.
        list.AddRange(Circuit(demoBase, today.AddDays(1), partial: true)); // A partially assigned booking.
        list.AddRange(CongressShuttles(demoBase, today.AddDays(2)));
        list.Add(AfterLongestLine(demoBase, today));
        list.AddRange(HeavyDriver(demoBase, today, planner.HeavyDriver.IdentificationNumber));
        list.AddRange(Incidents(demoBase, today, nowUtc, planner));
        return list;
    }

    private static TripRequest LineService(DemoBase demoBase, DemoLine line, DateOnly day, bool afternoon)
    {
        var start = DemoClock.Utc(day, new TimeOnly(afternoon ? 15 : 7, 0));
        var legs = line.Rounds * 2;
        var end = start.AddMinutes(legs * line.Minutes + (legs - 1) * DemoPlanner.TurnaroundMinutes);
        var shift = afternoon ? "tarde" : "mañana";
        var monday = DemoClock.MondayOf(day);
        var booking = new PlannedBooking($"{demoBase.Key}-{line.Code}-{shift}-{monday:yyyy-MM-dd}", BookingKind.RegularLine, line.Customer,
            $"Línea {line.Code} {Name(line.OriginKey)} ⇄ {Name(line.DestinationKey)} · turno de {shift}, semana del {monday:dd/MM/yyyy}");
        return new TripRequest(
            $"{line.Code} {Name(line.OriginKey)} ⇄ {Name(line.DestinationKey)} · {shift}, {line.Rounds} vuelta(s) · {day:dd/MM/yyyy} (demo)",
            line.OriginKey, line.OriginKey, start, end, line.RequiredSeats, ServiceType.Regular, booking);
    }

    private static TripRequest Transfer(DemoBase demoBase, DateOnly day, string from, string to, TimeOnly at, string part)
    {
        var customer = Hotels[(day.DayNumber + demoBase.Key.Length + part.Length) % Hotels.Length];
        var booking = new PlannedBooking($"{demoBase.Key}-TRF-{day:yyyy-MM-dd}-{part}", BookingKind.Transfer, customer,
            $"Traslado {Name(from)} → {Name(to)} · {day:dd/MM/yyyy} ({part})");
        var start = DemoClock.Utc(day, at);
        return new TripRequest($"Traslado {Name(from)} → {Name(to)} · {part} · {day:dd/MM/yyyy} (demo)", from, to,
            start, start.AddMinutes(demoBase.AirportMinutes), 8 + day.DayNumber % 3 * 4, ServiceType.AirportTransfer, booking);
    }

    private static IEnumerable<TripRequest> Excursion(DemoBase demoBase, DateOnly day)
    {
        var booking = new PlannedBooking($"{demoBase.Key}-EXC-{day:yyyy-MM-dd}", BookingKind.Excursion, demoBase.ExcursionCustomer,
            $"{demoBase.ExcursionName} · {day:dd/MM/yyyy}", "Grupo organizado; parada técnica a la vuelta si la piden.");
        var leg = demoBase.Excursion;
        var outbound = DemoClock.Utc(day, new TimeOnly(8, 30));
        var back = DemoClock.Utc(day, new TimeOnly(17, 30));
        yield return new TripRequest($"{demoBase.ExcursionName} · ida · {day:dd/MM/yyyy} (demo)", leg.OriginKey, leg.DestinationKey,
            outbound, outbound.AddMinutes(leg.Minutes), 45, ServiceType.Discretionary, booking);
        yield return new TripRequest($"{demoBase.ExcursionName} · vuelta · {day:dd/MM/yyyy} (demo)", leg.DestinationKey, leg.OriginKey,
            back, back.AddMinutes(leg.Minutes), 45, ServiceType.Discretionary, booking);
    }

    private static IEnumerable<TripRequest> Circuit(DemoBase demoBase, DateOnly first, bool partial)
    {
        var booking = new PlannedBooking($"{demoBase.Key}-CIR-{first:yyyy-MM-dd}", BookingKind.Circuit, demoBase.CircuitCustomer,
            $"{demoBase.CircuitName} · 3 días desde el {first:dd/MM/yyyy}", "Circuito con guía; el mismo conductor todo el viaje si es posible.");
        for (var i = 0; i < demoBase.Circuit.Count; i++)
        {
            var leg = demoBase.Circuit[i];
            var day = first.AddDays(i);
            var start = DemoClock.Utc(day, new TimeOnly(9, 30));
            yield return new TripRequest($"{demoBase.CircuitName} · día {i + 1}: {Name(leg.OriginKey)} → {Name(leg.DestinationKey)} · {day:dd/MM/yyyy} (demo)",
                leg.OriginKey, leg.DestinationKey, start, start.AddMinutes(leg.Minutes), 35, ServiceType.Circuit, booking,
                LeaveUncovered: partial && i > 0);
        }
    }

    /// <summary>
    /// A 90-min service on the next weekday that starts 15 min after the longest morning line ends, so offering it
    /// to that line's driver would exceed 4h30 of continuous driving (Art. 7): a certain, realistic rejection.
    /// </summary>
    private static TripRequest AfterLongestLine(DemoBase demoBase, DateOnly today)
    {
        var nextWeekday = today.AddDays(1);
        while (nextWeekday.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) nextWeekday = nextWeekday.AddDays(1);
        var longest = demoBase.Lines.Max(l => l.Rounds * 2 * l.Minutes + (l.Rounds * 2 - 1) * DemoPlanner.TurnaroundMinutes);
        var start = DemoClock.Utc(nextWeekday, new TimeOnly(7, 0)).AddMinutes(longest + DemoPlanner.TurnaroundMinutes);
        var booking = new PlannedBooking($"{demoBase.Key}-VIS-{nextWeekday:yyyy-MM-dd}", BookingKind.Extra, "Bodegas y Visitas Guiadas (demo)",
            $"Visita guiada de empresa · {nextWeekday:dd/MM/yyyy}");
        return new TripRequest($"Visita guiada {demoBase.Name} · {nextWeekday:dd/MM/yyyy} (demo)", demoBase.HubKey, demoBase.Excursion.DestinationKey,
            start, start.AddMinutes(90), 30, ServiceType.Discretionary, booking);
    }

    private static IEnumerable<TripRequest> CongressShuttles(DemoBase demoBase, DateOnly day)
    {
        var booking = new PlannedBooking($"{demoBase.Key}-CONG-{day:yyyy-MM-dd}", BookingKind.Shuttle, "Congreso Nacional de Movilidad (demo)",
            $"Lanzaderas aeropuerto → {Name(demoBase.HubKey)} · congreso del {day:dd/MM/yyyy}", "Cuatro llegadas de ponentes.");
        foreach (var at in new[] { new TimeOnly(9, 0), new TimeOnly(11, 0), new TimeOnly(13, 0), new TimeOnly(16, 0) })
        {
            var start = DemoClock.Utc(day, at);
            yield return new TripRequest($"Lanzadera congreso {Name(demoBase.AirportKey)} → {Name(demoBase.HubKey)} · {at:HH\\:mm} · {day:dd/MM/yyyy} (demo)",
                demoBase.AirportKey, demoBase.HubKey, start, start.AddMinutes(demoBase.AirportMinutes), 19, ServiceType.AirportTransfer, booking);
        }
    }

    /// <summary>
    /// 8h30 or 10 h on the weekdays of the previous 13 days (two blocks of 4h15 with 45 min between them, plus 1h30
    /// on extended days), keeping a week ≤ <see cref="HeavyWeekCap"/> h and two weeks ≤ <see cref="HeavyTwoWeeksCap"/> h
    /// counting today's 2 h, until ≥ 81 h in all. Then a 2 h service today. Two natural weeks (Art. 6.3, SPEC 0064) leave the week
    /// before last out of the count, so with this weekday pattern the alert only shows from Friday to Sunday.
    /// </summary>
    internal static IEnumerable<TripRequest> HeavyDriver(DemoBase demoBase, DateOnly today, string driverId)
    {
        var line = demoBase.Lines[0];
        var perWeek = new Dictionary<DateOnly, double>();
        var extended = new Dictionary<DateOnly, int>();
        double Week(DateOnly monday) => perWeek.GetValueOrDefault(monday);
        double total = 0;
        var todayMonday = DemoClock.MondayOf(today);
        perWeek[todayMonday] = TodayHours;

        for (var d = today.AddDays(-DemoPlan.HeavyHistoryDays); d < today; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var monday = DemoClock.MondayOf(d);
            double FitsWith(double hours) =>
                Week(monday) + hours <= HeavyWeekCap && Week(monday.AddDays(-7)) + Week(monday) + hours <= HeavyTwoWeeksCap
                && Week(monday) + hours + Week(monday.AddDays(7)) <= HeavyTwoWeeksCap ? hours : 0;
            var hours = extended.GetValueOrDefault(monday) < 2 ? FitsWith(10) : 0;
            if (hours == 0) hours = FitsWith(8.5);
            if (hours == 0) continue;
            if (hours == 10) extended[monday] = extended.GetValueOrDefault(monday) + 1;
            perWeek[monday] = Week(monday) + hours;
            total += hours;

            var booking = new PlannedBooking($"{demoBase.Key}-REF-{monday:yyyy-MM-dd}", BookingKind.Reinforcement, line.Customer,
                $"Refuerzos de la línea {line.Code} · semana del {monday:dd/MM/yyyy}");
            var blocks = new List<(TimeOnly At, int Minutes)> { (new(7, 0), 255), (new(12, 0), 255) };
            if (hours == 10) blocks.Add((new(17, 0), 90));
            for (var i = 0; i < blocks.Count; i++)
            {
                var start = DemoClock.Utc(d, blocks[i].At);
                yield return new TripRequest($"Refuerzo {line.Code} {Name(line.OriginKey)} · tramo {i + 1} · {d:dd/MM/yyyy} (demo)",
                    line.OriginKey, line.OriginKey, start, start.AddMinutes(blocks[i].Minutes), line.RequiredSeats, ServiceType.Regular,
                    booking, FixedDriver: driverId);
            }
        }
        if (total < HeavyTargetHours)
            throw new InvalidOperationException($"The near-limit driver of {demoBase.Name} only adds up to {total} h (< {HeavyTargetHours}).");

        var todayBooking = new PlannedBooking($"{demoBase.Key}-REF-{todayMonday:yyyy-MM-dd}", BookingKind.Reinforcement, line.Customer,
            $"Refuerzos de la línea {line.Code} · semana del {todayMonday:dd/MM/yyyy}");
        var serviceStart = DemoClock.Utc(today, new TimeOnly(15, 0));
        yield return new TripRequest($"Refuerzo {line.Code} {Name(line.OriginKey)} · tarde · {today:dd/MM/yyyy} (demo)",
            line.OriginKey, line.OriginKey, serviceStart, serviceStart.AddHours(TodayHours), line.RequiredSeats, ServiceType.Regular, todayBooking, FixedDriver: driverId);
    }

    internal const double TodayHours = 2;
    internal const double HeavyWeekCap = 56;       // DrivingLimits.MaxWeeklyDriving
    internal const double HeavyTwoWeeksCap = 90;   // DrivingLimits.MaxBiWeeklyDriving
    internal const double HeavyTargetHours = 81; // 90 % of MaxBiWeeklyDriving (ControlRules.NearLimitPercent).

    /// <summary>Today's incidents that need people pinned or no people at all.</summary>
    private static IEnumerable<TripRequest> Incidents(DemoBase demoBase, DateOnly today, DateTime nowUtc, DemoPlanner planner)
    {
        var urgent = RoundUp(nowUtc.AddMinutes(40));
        var soon = RoundUp(nowUtc.AddHours(2));
        yield return new TripRequest($"Traslado urgente {Name(demoBase.HubKey)} → {Name(demoBase.AirportKey)} · {today:dd/MM/yyyy} (demo)",
            demoBase.HubKey, demoBase.AirportKey, urgent, urgent.AddMinutes(demoBase.AirportMinutes), 15, ServiceType.AirportTransfer,
            new PlannedBooking($"{demoBase.Key}-URG-{today:yyyy-MM-dd}", BookingKind.Extra, "Hotel Plaza Mayor (demo)",
                $"Traslado de última hora al aeropuerto · {today:dd/MM/yyyy}", "Entró por teléfono hace 10 minutos."),
            LeaveUncovered: true);
        yield return new TripRequest($"Servicio discrecional boda {Name(demoBase.Lines[1].OriginKey)} → {Name(demoBase.Lines[1].DestinationKey)} · {today:dd/MM/yyyy} (demo)",
            demoBase.Lines[1].OriginKey, demoBase.Lines[1].DestinationKey, soon, soon.AddMinutes(60), 40, ServiceType.Discretionary,
            new PlannedBooking($"{demoBase.Key}-BODA-{today:yyyy-MM-dd}", BookingKind.Extra, "Familia invitada a la boda (demo)",
                $"Invitados de una boda · {today:dd/MM/yyyy}"),
            LeaveUncovered: true);

        // Assigned now, then the driver calls in sick (leave from 17:00): gap alert.
        var sick = DemoClock.Utc(today, new TimeOnly(18, 0));
        var line = demoBase.Lines[0];
        yield return new TripRequest($"Refuerzo {line.Code} {Name(line.OriginKey)} · noche · {today:dd/MM/yyyy} (demo)",
            line.OriginKey, line.OriginKey, sick, sick.AddHours(2), 30, ServiceType.Regular,
            new PlannedBooking($"{demoBase.Key}-NOC-{today:yyyy-MM-dd}", BookingKind.Reinforcement, line.Customer,
                $"Refuerzo nocturno de la línea {line.Code} · {today:dd/MM/yyyy}"),
            FixedDriver: planner.SickDriver.IdentificationNumber);

        // Assigned now, then the coach breaks down (in the workshop all day): vehicle-unavailable alert.
        var school = new PlannedBooking($"{demoBase.Key}-ESC-{today:yyyy-MM-dd}", BookingKind.Extra, "Colegio Público Los Almendros (demo)",
            $"Salida escolar · {today:dd/MM/yyyy}");
        foreach (var (at, leg) in new[] { (new TimeOnly(8, 0), "ida"), (new TimeOnly(14, 0), "vuelta") })
        {
            var start = DemoClock.Utc(today, at);
            yield return new TripRequest($"Salida escolar {demoBase.Name} · {leg} · {today:dd/MM/yyyy} (demo)", demoBase.HubKey, demoBase.Excursion.DestinationKey,
                start, start.AddMinutes(90), 50, ServiceType.Discretionary, school, FixedVehicle: planner.WorkshopVehicle.LicensePlate);
        }
    }

    private static DateTime RoundUp(DateTime utc)
    {
        var ticks = TimeSpan.FromMinutes(5).Ticks;
        return new DateTime((utc.Ticks + ticks - 1) / ticks * ticks, DateTimeKind.Utc);
    }

    private static string Name(string key) => DemoDataset.Location(key).Name;
}

/// <summary>
/// Greedy, per base. Margins stricter than CE 561/2006 so that the API accepts: trips ≤ 4h15, 45 min between a driver's trips,
/// ≤ 8 h a day, a working day of ≤ 13 h (≥ 11 h rest), ≤ 40 h a week, two fixed consecutive days off. Vehicles: 15 min between
/// trips and enough seats (SPEC 0053 R5).
/// </summary>
public sealed class DemoPlanner
{
    public const int TurnaroundMinutes = 15;
    public static readonly TimeSpan MaxTrip = TimeSpan.FromMinutes(255);
    public static readonly TimeSpan DriverGap = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan MaxDay = TimeSpan.FromHours(8);
    public static readonly TimeSpan MaxSpan = TimeSpan.FromHours(13);
    public static readonly TimeSpan MinRest = TimeSpan.FromHours(11);
    public static readonly TimeSpan MaxWeek = TimeSpan.FromHours(40);
    public static readonly TimeSpan VehicleGap = TimeSpan.FromMinutes(15);

    private readonly DemoBase _base;
    private readonly DateOnly _today;
    private readonly Dictionary<string, List<PlannedTrip>> _byDriver = new();
    private readonly Dictionary<string, List<PlannedTrip>> _byVehicle = new();

    public DemoPlanner(DemoBase demoBase, DateOnly today)
    {
        _base = demoBase;
        _today = today;
        HeavyDriver = demoBase.Drivers[0];
        SickDriver = demoBase.Drivers.Skip(1).First(d => !d.DaysOff.Contains(today.DayOfWeek));
        WorkshopVehicle = demoBase.Vehicles[1];
        VacationDriver = demoBase.Drivers[^1];
        InspectionVehicle = demoBase.Vehicles[^1];
    }

    public DemoDriver HeavyDriver { get; }
    public DemoDriver SickDriver { get; }
    public DemoVehicle WorkshopVehicle { get; }

    /// <summary>On holiday [<see cref="VacationFrom"/>, <see cref="VacationTo"/>) three weeks ago; never planned then.</summary>
    public DemoDriver VacationDriver { get; }
    public DateOnly VacationFrom => _today.AddDays(-20);
    public DateOnly VacationTo => _today.AddDays(-15);

    /// <summary>At the periodic inspection (ITV) all day <see cref="InspectionDay"/>; never planned that day.</summary>
    public DemoVehicle InspectionVehicle { get; }
    public DateOnly InspectionDay => _today.AddDays(-12);

    public List<PlannedTrip> Assign(IEnumerable<TripRequest> requests)
    {
        var result = new List<PlannedTrip>();
        var bookingDriver = new Dictionary<string, string>();
        var bookingVehicle = new Dictionary<string, string>();
        foreach (var request in requests.OrderBy(r => r.Start).ThenBy(r => r.Description, StringComparer.Ordinal))
        {
            if (request.End - request.Start > MaxTrip) throw new InvalidOperationException($"Service of more than 4h15: {request.Description}");
            if (request.LeaveUncovered)
            {
                result.Add(Planned(request, null, null));
                continue;
            }

            var driver = request.FixedDriver ?? PickDriver(request, bookingDriver.GetValueOrDefault(request.Booking.Key))
                ?? throw new InvalidOperationException($"No driver for {request.Description} ({_base.Name}).");
            var vehicle = request.FixedVehicle ?? PickVehicle(request, bookingVehicle.GetValueOrDefault(request.Booking.Key))
                ?? throw new InvalidOperationException($"No vehicle for {request.Description} ({_base.Name}).");
            if (request.FixedDriver is not null && request.FixedDriver != HeavyDriver.IdentificationNumber && !DriverFits(request.FixedDriver, request))
                throw new InvalidOperationException($"The pinned driver does not fit in {request.Description}.");

            var trip = Planned(request, driver, vehicle);
            Of(_byDriver, driver).Add(trip);
            Of(_byVehicle, vehicle).Add(trip);
            bookingDriver.TryAdd(request.Booking.Key, driver);
            bookingVehicle.TryAdd(request.Booking.Key, vehicle);
            result.Add(trip);
        }
        return result;
    }

    private PlannedTrip Planned(TripRequest request, string? driver, string? vehicle) =>
        new(request.Description, _base.Key, request.OriginKey, request.DestinationKey, request.Start, request.End, request.RequiredSeats, request.ServiceType, request.Booking, driver, vehicle);

    private string? PickDriver(TripRequest request, string? preferred)
    {
        var day = DemoClock.LocalDate(request.Start);
        var pool = _base.Drivers.Where(d => d != HeavyDriver && !d.DaysOff.Contains(day.DayOfWeek)
            && !(d == SickDriver && day >= _today) && !(d == VacationDriver && day >= VacationFrom && day < VacationTo)).ToList();
        if (preferred is not null && pool.Any(d => d.IdentificationNumber == preferred) && DriverFits(preferred, request)) return preferred;
        var monday = DemoClock.MondayOf(day);
        return pool
            .Where(d => DriverFits(d.IdentificationNumber, request))
            .OrderBy(d => Of(_byDriver, d.IdentificationNumber).Where(t => DemoClock.MondayOf(DemoClock.LocalDate(t.Start)) == monday).Sum(t => t.Duration.TotalMinutes))
            .ThenBy(d => d.HomeKey == request.OriginKey ? 0 : 1)
            .ThenBy(d => d.IdentificationNumber, StringComparer.Ordinal)
            .Select(d => d.IdentificationNumber)
            .FirstOrDefault();
    }

    /// <summary>Within the planning margins, not the legal limits.</summary>
    public bool DriverFits(string driverId, TripRequest request)
    {
        var mine = Of(_byDriver, driverId);
        if (mine.Any(t => request.Start < t.End + DriverGap && t.Start < request.End + DriverGap)) return false;

        var day = DemoClock.LocalDate(request.Start);
        var sameDay = mine.Where(t => DemoClock.LocalDate(t.Start) == day).Select(t => (t.Start, t.End)).Append((request.Start, request.End)).ToList();
        if (TimeSpan.FromTicks(sameDay.Sum(t => (t.End - t.Start).Ticks)) > MaxDay) return false;
        var first = sameDay.Min(t => t.Start);
        var last = sameDay.Max(t => t.End);
        if (last - first > MaxSpan) return false;
        var before = mine.Where(t => DemoClock.LocalDate(t.Start) < day).Select(t => (DateTime?)t.End).Max();
        if (before is not null && first - before < MinRest) return false;
        var after = mine.Where(t => DemoClock.LocalDate(t.Start) > day).Select(t => (DateTime?)t.Start).Min();
        if (after is not null && after - last < MinRest) return false;

        var monday = DemoClock.MondayOf(day);
        var week = mine.Where(t => DemoClock.MondayOf(DemoClock.LocalDate(t.Start)) == monday).Sum(t => t.Duration.Ticks) + (request.End - request.Start).Ticks;
        return TimeSpan.FromTicks(week) <= MaxWeek;
    }

    private string? PickVehicle(TripRequest request, string? preferred)
    {
        var day = DemoClock.LocalDate(request.Start);
        bool Fits(DemoVehicle vehicle) => vehicle.SeatingCapacity >= request.RequiredSeats && !(vehicle == WorkshopVehicle && day >= _today)
            && !(vehicle == InspectionVehicle && day == InspectionDay)
            && !Of(_byVehicle, vehicle.LicensePlate).Any(t => request.Start < t.End + VehicleGap && t.Start < request.End + VehicleGap);
        var preferredVehicle = _base.Vehicles.FirstOrDefault(v => v.LicensePlate == preferred);
        if (preferredVehicle is not null && Fits(preferredVehicle)) return preferred;
        return _base.Vehicles.Where(Fits)
            .OrderBy(v => v.SeatingCapacity)
            .ThenBy(v => Of(_byVehicle, v.LicensePlate).Count)
            .ThenBy(v => v.LicensePlate, StringComparer.Ordinal)
            .Select(v => v.LicensePlate)
            .FirstOrDefault();
    }

    private static List<PlannedTrip> Of(Dictionary<string, List<PlannedTrip>> map, string key) =>
        map.TryGetValue(key, out var list) ? list : map[key] = [];
}

/// <summary>Realistic wrong attempts, all on future trips, each one certain to be rejected.</summary>
internal static class DemoRejections
{
    private const int PerKind = 2;

    public static IEnumerable<PlannedRejection> Pick(DemoBase demoBase, IReadOnlyList<PlannedTrip> planned, DateOnly today)
    {
        var covered = planned.Where(t => t.Covered && DemoClock.LocalDate(t.Start) > today).OrderBy(t => t.Start).ThenBy(t => t.Description, StringComparer.Ordinal).ToList();
        var used = new HashSet<string>();
        var picks = new List<PlannedRejection>();
        void Add(PlannedTrip trip, RejectionKind kind, string driver, string plate)
        {
            if (picks.Count(p => p.Kind == kind) >= PerKind || !used.Add(trip.Description)) return;
            picks.Add(new PlannedRejection(trip.Description, kind, driver, plate));
        }

        var minibus = demoBase.Vehicles.Where(v => v != demoBase.Vehicles[1]).OrderBy(v => v.SeatingCapacity).First();
        foreach (var trip in covered.Where(t => t.RequiredSeats > minibus.SeatingCapacity))
            Add(trip, RejectionKind.VehicleTooSmall, trip.DriverId!, minibus.LicensePlate);

        foreach (var trip in covered)
        {
            // Allocated earlier by the seeder (starts before) and overlapping in time.
            var busy = covered.Where(u => u.Start < trip.Start && u.End > trip.Start).ToList();
            var otherDriver = busy.FirstOrDefault(u => u.DriverId != trip.DriverId);
            if (otherDriver is not null) Add(trip, RejectionKind.DriverBusy, otherDriver.DriverId!, trip.VehiclePlate!);
            var otherVehicle = busy.FirstOrDefault(u => u.VehiclePlate != trip.VehiclePlate && SeatsOf(demoBase, u.VehiclePlate!) >= trip.RequiredSeats);
            if (otherVehicle is not null) Add(trip, RejectionKind.VehicleBusy, trip.DriverId!, otherVehicle.VehiclePlate!);

            // Continuous driving: someone who has just finished a long trip (< 45 min ago) and would exceed 4h30 (Art. 7).
            var tired = covered.FirstOrDefault(u => u.DriverId != trip.DriverId && u.End <= trip.Start && trip.Start - u.End < DemoPlanner.DriverGap
                && u.Duration + trip.Duration > TimeSpan.FromMinutes(270)
                && !covered.Any(o => o.DriverId == u.DriverId && o.Start < trip.End && trip.Start < o.End));
            if (tired is not null) Add(trip, RejectionKind.ContinuousDriving, tired.DriverId!, trip.VehiclePlate!);
        }
        return picks;
    }

    private static int SeatsOf(DemoBase demoBase, string plate) => demoBase.Vehicles.First(v => v.LicensePlate == plate).SeatingCapacity;
}
