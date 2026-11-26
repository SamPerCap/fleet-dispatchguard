namespace DispatchGuard.Core.Localization;

/// <summary>Candidates, recommendation and vehicle plan.</summary>
public static partial class Texts
{
    public static LocalizedText FilterActive() => Of("active, allocatable automatically and not external", "activo, auto-asignable y no externo");

    public static LocalizedText FilterNotAssigned() => Of("not the driver already assigned", "no es el conductor ya asignado");

    public static LocalizedText FilterAvailable() =>
        Of("no unavailability (sick leave, annual leave) at that time", "sin indisponibilidad (baja, vacaciones) en el horario");

    public static LocalizedText FilterNoOverlap() => Of("no overlap with another non-cancelled service", "sin solape con otro servicio no cancelado");

    public static LocalizedText FilterFitsDay() => Of("fits in the daily availability (SPEC 0030)", "le cabe en su disponibilidad diaria (SPEC 0030)");

    public static LocalizedText FilterCompliant() =>
        Of("complies with CE 561/2006, also in the later services already assigned (warnings do not exclude)",
            "cumple CE 561/2006, también sus servicios posteriores ya asignados (los avisos no excluyen)");

    public static LocalizedText DiscardedNotEligible() =>
        Of("Not an active driver, allocatable automatically and not external", "No es un conductor activo, auto-asignable y no externo");

    public static LocalizedText DiscardedOverlap() => Of("Overlaps another non-cancelled service", "Tiene un solape con otro servicio no cancelado");

    public static LocalizedText DiscardedNoAvailability() =>
        Of("Does not fit in the daily availability (SPEC 0030)", "No le cabe en su disponibilidad diaria (SPEC 0030)");

    public static LocalizedText DiscardedNotCompliant(LocalizedText broken) =>
        Of("Does not comply with CE 561/2006: {0}", "No cumple CE 561/2006: {0}", [broken]);

    public static LocalizedText ListedAndMore(IReadOnlyList<LocalizedText> texts, int more) =>
        Of("{0:; }; and {1} more", "{0:; }; y {1} más", [texts, more]);

    public static LocalizedText SameBookingReason(int trip, LocalizedText reason) =>
        Of("Same booking: already covers trip #{0}; {1}", "Misma reserva: ya cubre el viaje #{0}; {1}", [trip, reason]);

    public static LocalizedText WonTieBreakReason(LocalizedText reason, double hours) =>
        Of("{0}; tie-break: {1:0.0} h since the last weekly rest period", "{0}; desempate: {1:0.0} h desde su último descanso semanal", [reason, hours]);

    public static LocalizedText NoBaseReason() => Of("No base assigned: distance unknown", "Sin base asignada: distancia desconocida");

    public static LocalizedText BaseIsBothReason(string location) => Of("Base = departure and arrival ({0})", "Base = salida y llegada ({0})", [location]);

    public static LocalizedText BaseIsOriginReason(string location) => Of("Base = departure ({0})", "Base = salida ({0})", [location]);

    public static LocalizedText BaseWithinReason(double km, double radius) =>
        Of("Base {0:0.0} km from the departure (radius {1:0} km)", "Base a {0:0.0} km de la salida (radio {1:0} km)", [km, radius]);

    public static LocalizedText BaseBeyondReason(double km, double radius) =>
        Of("Base {0:0.0} km from the departure (outside the {1:0} km radius)", "Base a {0:0.0} km de la salida (fuera del radio de {1:0} km)", [km, radius]);

    public static LocalizedText VehicleUnknownReason() =>
        Of("No recent trips and no base assigned: position unknown", "Sin viajes recientes ni base asignada: posición desconocida");

    public static LocalizedText VehicleFromLastTrip(int trip, string location) =>
        Of("From: destination of its last trip (#{0}, {1})", "Origen: destino de su último viaje (#{0}, {1})", [trip, location]);

    public static LocalizedText VehicleFromBase(string location) => Of("From: its base ({0})", "Origen: su base ({0})", [location]);

    public static LocalizedText VehicleIsBothReason(LocalizedText where) => Of("{0} = departure; base = arrival", "{0} = salida; base = llegada", [where]);

    public static LocalizedText VehicleIsOriginReason(LocalizedText where) => Of("{0} = departure", "{0} = salida", [where]);

    public static LocalizedText VehicleWithinReason(LocalizedText where, double km, double radius) =>
        Of("{0}; {1:0.0} km from the departure (radius {2:0} km)", "{0}; a {1:0.0} km de la salida (radio {2:0} km)", [where, km, radius]);

    public static LocalizedText VehicleBeyondReason(LocalizedText where, double km, double radius) =>
        Of("{0}; {1:0.0} km from the departure (outside the {2:0} km radius)", "{0}; a {1:0.0} km de la salida (fuera del radio de {2:0} km)", [where, km, radius]);

    public static LocalizedText RecommendationNote() => Of("Heuristic, not a global optimum", "Heurística, no óptimo global");

    public static LocalizedText RecommendationArrivalNote(double speed) =>
        Of("Straight-line distances. Whoever would not arrive from the previous service or at the next one at {0:0} km/h is not proposed: it is an estimate.",
            "Distancias en línea recta. Quien no llegaría desde su servicio anterior o al siguiente a {0:0} km/h no se propone: es una estimación.", [speed]);

    public static LocalizedText NoPairChosen() => Of("No driver and vehicle chosen", "Sin conductor y vehículo elegidos");

    public static LocalizedText TheDriver(LocalizedText reason) => Of("The driver {0}", "El conductor {0}", [reason]);

    public static LocalizedText TheVehicle(LocalizedText reason) => Of("The vehicle {0}", "El vehículo {0}", [reason]);

    public static LocalizedText PlateAndSeats(string plate, int seats) => Of("{0} · {1} {1|seat|seats}", "{0} · {1} {1|plaza|plazas}", [plate, seats]);

    public static LocalizedText FromPreviousService(int service) => Of("from the previous service #{0}", "desde su servicio anterior #{0}", [service]);

    public static LocalizedText FromLocation(string location) => Of("from {0}", "desde {0}", [location]);

    public static LocalizedText UnknownPosition() => Of("unknown position", "posición desconocida");

    public static LocalizedText NoDriverNorVehicle() =>
        Of("No solution: no legal driver and no fit vehicle", "Sin solución: ningún conductor legal ni vehículo apto");

    public static LocalizedText NoLegalDriver() => Of("No solution: no legal driver", "Sin solución: ningún conductor legal");

    public static LocalizedText NoFitVehicle() => Of("No solution: no fit vehicle", "Sin solución: ningún vehículo apto");

    public static LocalizedText FartherAway(double km, int drivers, int vehicles) =>
        Of("No solution within {0:0} km. Farther away: {1} legal {1|driver|drivers} and {2} fit {2|vehicle|vehicles}",
            "Sin solución a {0:0} km. Más lejos: {1} {1|conductor legal|conductores legales} y {2} {2|vehículo apto|vehículos aptos}", [km, drivers, vehicles]);

    public static LocalizedText DoNotArrive(int drivers, int vehicles) =>
        Of("Do not arrive (estimate): {0} {0|driver|drivers} and {1} {1|vehicle|vehicles}",
            "No llegan (estimación): {0} {0|conductor|conductores} y {1} {1|vehículo|vehículos}", [drivers, vehicles]);

    public static LocalizedText WithoutPosition(int drivers, int vehicles) =>
        Of("Without a known position: {0} {0|driver|drivers} and {1} {1|vehicle|vehicles}",
            "Sin posición conocida: {0} {0|conductor|conductores} y {1} {1|vehículo|vehículos}", [drivers, vehicles]);

    public static LocalizedText OnlyServiceDeparted() =>
        Of("The only assigned service of this period has already departed: it keeps its bus and there is nothing to change.",
            "El único servicio asignado de este periodo ya ha salido: conserva su autobús y no hay nada que cambiar.");

    public static LocalizedText EveryServiceDeparted(int departed) =>
        Of("The {0} assigned services of this period have already departed: they keep their bus and there is nothing to change.",
            "Los {0} servicios asignados de este periodo ya han salido: conservan su autobús y no hay nada que cambiar.", [departed]);

    public static LocalizedText NoVehicleArrives(int trip) =>
        Of("Trip #{0} has no fit vehicle that arrives in time: the current plan stays.",
            "El viaje #{0} no tiene ningún vehículo apto que llegue a tiempo: se mantiene la planificación actual.", [trip]);

    public static LocalizedText NoBetterPlan(double km) =>
        Of("The calculation found no plan with fewer empty km than the current one ({0:#,0.0} km): no change is proposed.",
            "El cálculo no ha encontrado un reparto con menos km en vacío que el actual ({0:#,0.0} km): no se propone ningún cambio.", [km]);

    public static LocalizedText NoChanges() => Of("No changes", "Sin cambios");

    public static LocalizedText GreedyByDepartureRole() => Of("Greedy heuristic by departure time", "Heurística voraz por orden de salida");

    public static LocalizedText ReturnsCurrentPlan() => Of("Returns the current plan", "Devuelve la planificación actual");

    public static LocalizedText GreedyPattern() => Of("Greedy algorithm", "Algoritmo voraz (greedy)");

    public static LocalizedText PurePattern() => Of("Testable pure function (no I/O)", "Función pura testeable (sin I/O)");

    public static LocalizedText StraightLineTo(double km, string location) =>
        Of("{0:#,0.0} km in a straight line to {1}", "{0:#,0.0} km en línea recta hasta {1}", [km, location]);

    public static LocalizedText UnknownCountsZero() => Of("no known position, it counts as 0 km", "sin posición conocida, cuenta como 0 km");

    public static LocalizedText UnknownNoBase() => Of("Position unknown (no base): it counts as 0 km", "Posición desconocida (sin base): cuenta como 0 km");

    public static LocalizedText FromTripDestination(int trip, string location, DateTime end) =>
        Of("Destination of trip #{0}: {1}, ends {2:HH:mm}", "Destino del viaje #{0}: {1}, fin {2:HH:mm}", [trip, location, end]);

    public static LocalizedText FromDepartedTripDestination(int trip, string location, DateTime end) =>
        Of("Destination of trip #{0} (already departed): {1}, ends {2:HH:mm}", "Destino del viaje #{0} (ya ha salido): {1}, fin {2:HH:mm}", [trip, location, end]);

    public static LocalizedText FromItsBase(string location) => Of("Its base: {0}", "Su base: {0}", [location]);

    public static LocalizedText NoTravelEstimate() =>
        Of("Without a known position the travel time cannot be estimated", "Sin posición conocida no se puede estimar el trayecto");

    public static LocalizedText Arrival(LocalizedText departure, double minutes, double speed, LocalizedText margin) =>
        Of("{0}, ≈{1:0} min at {2:0} km/h (estimated); margin: {3}", "{0}, ≈{1:0} min a {2:0} km/h (estimado); margen: {3}", [departure, minutes, speed, margin]);

    public static LocalizedText LeavesNow(DateTime now, LocalizedText place) =>
        Of("It leaves at {0:HH:mm} at the earliest (now){1}", "Como pronto sale a las {0:HH:mm} (ahora){1}", [now, place]);

    public static LocalizedText LeavesWhenFree(DateTime departs, LocalizedText place) =>
        Of("It leaves on {0:dd/MM} at {0:HH:mm} at the earliest (free since then){1}",
            "Como pronto sale el {0:dd/MM} a las {0:HH:mm} (libre desde entonces){1}", [departs, place]);

    public static LocalizedText NoPlace() => Of("", "");

    public static LocalizedText FromLastServicePlace(string location, int service) =>
        Of(" from {0} (destination of its last service #{1})", " desde {0} (destino de su último servicio #{1})", [location, service]);

    public static LocalizedText Minutes(double minutes) => Of("{0:0} min", "{0:0} min", [minutes]);

    public static LocalizedText MoreThanADay() => Of("more than a day", "más de un día");

    public static LocalizedText VehicleChange(int trip, string plate, LocalizedText distance) =>
        Of("Trip #{0}: {1}. Empty leg: {2}.", "Viaje #{0}: {1}. Tramo en vacío: {2}.", [trip, plate, distance]);

    public static LocalizedText VehicleOrigin() => Of("Where the vehicle comes from", "Origen del vehículo");

    public static LocalizedText Criterion() => Of("Criterion", "Criterio");

    public static LocalizedText ShortestEmptyLeg(LocalizedText distance) =>
        Of("Shortest empty leg among the fit vehicles that arrive in time: {0}", "Menor tramo en vacío entre los vehículos aptos que llegan a tiempo: {0}", [distance]);

    public static LocalizedText ArrivesInTime() => Of("Arrives in time", "Llega a tiempo");

    public static LocalizedText PlannedVehicleFit(int seats, int required) =>
        Of("Active, {0} seats ≥ {1}, no unavailability", "Activo, {0} plazas ≥ {1}, sin indisponibilidad", [seats, required]);

    public static LocalizedText DriverRules() => Of("Driver (CE 561/2006)", "Conductor (CE 561/2006)");

    public static LocalizedText PlanKeepsDriver() =>
        Of("The plan changes neither driver nor schedule and does not evaluate them; they are validated when allocating",
            "El plan no cambia conductor ni horario y no los evalúa; se validan al asignar");

    public static LocalizedText GreedyNearestRole() =>
        Of("Greedy heuristic: trips by departure time, nearest fit vehicle", "Heurística voraz: viajes por hora de salida, vehículo apto más cercano");

    public static LocalizedText PlateAndEmptyKm(string plate, double km) => Of("{0}, {1:#,0.0} empty km", "{0}, {1:#,0.0} km en vacío", [plate, km]);

    public static LocalizedText HardEligibilityRole() =>
        Of("Hard eligibility filter (the same as allocating)", "Filtro duro de elegibilidad (el mismo de la asignación)");

    public static LocalizedText EstimatedArrivalRole() => Of("Estimated arrival; it also prevents overlaps", "Llegada estimada; también impide solapes");

    public static LocalizedText PlanSummary(LocalizedText changes, double before, double after, LocalizedText saving) =>
        Of("{0} of vehicle: {1:#,0.0} → {2:#,0.0} empty km (saving: {3}).", "{0} de vehículo: {1:#,0.0} → {2:#,0.0} km en vacío (ahorro: {3}).",
            [changes, before, after, saving]);

    public static LocalizedText EmptyKm() => Of("Empty km", "Km en vacío");

    public static LocalizedText EmptyKmBeforeAndAfter(double before, double after, int trips) =>
        Of("Before {0:#,0.0} km, after {1:#,0.0} km in {2} {2|trip|trips}", "Antes {0:#,0.0} km, después {1:#,0.0} km en {2} {2|viaje|viajes}", [before, after, trips]);

    public static LocalizedText ArrivalTitle() => Of("Arrival", "Llegada");

    public static LocalizedText NoBusLeavesBefore(DateTime now, double speed) =>
        Of("No bus leaves before now ({0:dd/MM HH:mm}): from where its last service leaves it, or from its base, and at {1:0} km/h",
            "Ningún autobús sale antes de ahora ({0:dd/MM HH:mm}): desde donde lo deja su último servicio, o su base, y a {1:0} km/h", [now, speed]);

    public static LocalizedText KeepBusThoughLate() => Of("They keep their bus though it does not arrive", "Conservan su autobús aunque no llegue");

    public static LocalizedText KeepBusThoughLateDetail(LocalizedText trips) =>
        Of("By the estimate, the bus they already have does not arrive in time, but no better one was found: {0}",
            "Según la estimación, el autobús que ya tienen no llega a tiempo, pero no se ha encontrado otro mejor: {0}", [trips]);

    public static LocalizedText KeepBusThoughUnfit() => Of("They keep a bus that is not fit", "Conservan un autobús que no es apto");

    public static LocalizedText KeepBusThoughUnfitDetail(LocalizedText trips) =>
        Of("The bus they already have is not fit and no other arrives in time by the estimate: {0}",
            "El autobús que ya tienen no es apto y ningún otro llega a tiempo según la estimación: {0}", [trips]);

    public static LocalizedText TripsAndMore(string trips, int more) => Of("{0} and {1} more", "{0} y {1} más", [trips, more]);

    public static LocalizedText BackToBase() => Of("Back to base", "Vuelta a base");

    public static LocalizedText EveryDayAtBase() =>
        Of("Each vehicle starts and ends the day (Madrid time) at its base", "Cada vehículo empieza y acaba el día (de Madrid) en su base");

    public static LocalizedText PositionCarriedOver() =>
        Of("The position of the vehicle carries over from day to day", "La posición del vehículo se arrastra entre días");

    public static LocalizedText GreedyNotOptimalRole() => Of("Greedy heuristic, not a global optimum", "Heurística voraz, no óptimo global");

    public static LocalizedText Changes(int changes) => Of("{0} {0|change|changes}", "{0} {0|cambio|cambios}", [changes]);

    public static LocalizedText LessThanPercent(double percent) => Of("less than {0:0.0} %", "menos del {0:0.0} %", [percent]);

    public static LocalizedText Percent(double percent) => Of("{0:0.#} %", "{0:0.#} %", [percent]);

    public static LocalizedText ServicesDeparted() => Of("Services already departed", "Servicios que ya han salido");

    public static LocalizedText ServicesDepartedDetail(int departed, int count) =>
        Of("{0} of {1}: they keep their bus and count for its position and for the km, before and after",
            "{0} de {1}: conservan su autobús y cuentan para su posición y para los km, antes y después", [departed, count]);
}
