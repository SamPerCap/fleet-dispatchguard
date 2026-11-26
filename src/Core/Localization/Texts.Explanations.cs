using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Localization;

/// <summary>Explanations of the allocation and of the candidates.</summary>
public static partial class Texts
{
    public static LocalizedText ForcedFlowResult() =>
        Of("Rules broken; allocation forced by an explicit human decision", "Normativa incumplida; asignación forzada por decisión humana explícita");

    public static LocalizedText ForceAssignRole() =>
        Of("Allocates despite the CE 561/2006 rejection and keeps the reason and the author; vehicle and overlaps did pass",
            "Asigna pese al rechazo de CE 561/2006 y guarda motivo y autor; vehículo y solapes sí pasaron");

    public static LocalizedText ForceAssignResult() =>
        Of("Saved as “Forced” and audited in the register of attempts", "Guardado como «Forzado» y auditado en el registro de intentos");

    public static LocalizedText ForcedSummary(string by, IReadOnlyList<LocalizedText> failed, string reason) =>
        Of("Allocation FORCED by {0}: it breaks “{1:”, “}”. Reason: {2}", "Asignación FORZADA por {0}: incumple «{1:», «}». Motivo: {2}", [by, failed, reason]);

    public static LocalizedText ProposalRole() =>
        Of("The same steps as allocating, on an in-memory copy", "Los mismos pasos que al asignar, sobre una copia en memoria");

    public static LocalizedText AllValidatorsPassed() => Of("Every validator passed", "Todos los validadores pasaron");

    public static LocalizedText StopsAtFirstFailure() => Of("Stops at the first validator that fails", "Corta en el primer validador que falla");

    public static LocalizedText ProposalComplies(int rules) =>
        Of("Proposal not saved: it meets the driver's availability, {0} rules of CE 561/2006, the vehicle and the schedules. It is checked again when applied.",
            "Propuesta sin guardar: cumple la disponibilidad del conductor, {0} reglas de CE 561/2006, el vehículo y los horarios. Se vuelve a comprobar al aplicar.",
            [rules]);

    public static LocalizedText ProposalCompliesWithWarnings(int rules, int warnings) =>
        Of("Proposal not saved: it meets the driver's availability, {0} rules of CE 561/2006, the vehicle and the schedules; {1} {1|warning that does|warnings that do} not block. It is checked again when applied.",
            "Propuesta sin guardar: cumple la disponibilidad del conductor, {0} reglas de CE 561/2006, el vehículo y los horarios; {1} {1|aviso que no bloquea|avisos que no bloquean}. Se vuelve a comprobar al aplicar.",
            [rules, warnings]);

    public static LocalizedText ProposalFails(IReadOnlyList<LocalizedText> failed) =>
        Of("It does not comply, it would not be applied: “{0:”, “}” fails.", "No cumple, no se aplicaría: falla «{0:», «}».", [failed]);

    public static LocalizedText DriverAvailable() => Of("Driver available", "Conductor disponible");

    public static LocalizedText DriverIsAvailable() =>
        Of("Active, with no sick leave or other unavailability at that time.", "Está activo y sin baja ni otra indisponibilidad en ese horario.");

    public static LocalizedText VehicleFit() => Of("Vehicle fit", "Vehículo apto");

    public static LocalizedText VehicleIsFit(int required, int seats) =>
        Of("The service needs {0} seats and the vehicle has {1}; it is active and has no unavailability at that time.",
            "El servicio necesita {0} plazas y el vehículo tiene {1}; está activo y sin indisponibilidad en ese horario.", [required, seats]);

    public static LocalizedText NoDriverOverlap() =>
        Of("No overlap with other services of the driver", "Sin solape con otros servicios del conductor");

    public static LocalizedText NoVehicleOverlap() =>
        Of("No overlap with other services of the vehicle", "Sin solape con otros servicios del vehículo");

    public static LocalizedText NothingOverlaps() =>
        Of("No assigned service overlaps this schedule.", "Ningún servicio asignado se solapa con este horario.");

    public static LocalizedText Overlaps(string services) =>
        Of("It overlaps service {0}.", "Se solapa con el servicio {0}.", [services]);

    public static LocalizedText NotCheckedAfterFailure() =>
        Of("Not checked: an earlier step failed (it stops at the first failure).", "No se comprobó: falló un paso anterior (se corta en el primer fallo).");

    public static LocalizedText Allocated(int rules) =>
        Of("Allocated: the driver's availability, {0} rules of CE 561/2006, the vehicle and the schedules were checked before saving.",
            "Asignación hecha: se comprobaron la disponibilidad del conductor, {0} reglas de CE 561/2006, el vehículo y los horarios antes de guardar.",
            [rules]);

    public static LocalizedText AllocatedWithWarnings(int rules, int warnings) =>
        Of("Allocated: the driver's availability, {0} rules of CE 561/2006, the vehicle and the schedules were checked before saving; {1} {1|warning that does|warnings that do} not block.",
            "Asignación hecha: se comprobaron la disponibilidad del conductor, {0} reglas de CE 561/2006, el vehículo y los horarios antes de guardar; {1} {1|aviso que no bloquea|avisos que no bloquean}.",
            [rules, warnings]);

    public static LocalizedText Rejected(IReadOnlyList<LocalizedText> failed) =>
        Of("Allocation rejected before saving anything: “{0:”, “}” fails.", "Asignación rechazada antes de guardar nada: falla «{0:», «}».", [failed]);

    public static LocalizedText OrchestratorRole() =>
        Of("Orchestrator: calls the validators in a fixed order and allocates only if all of them pass",
            "Orquestador: llama a los validadores en orden fijo y solo asigna si todos pasan");

    public static LocalizedText DriverEligibilityRole() =>
        Of("Active and with no unavailability at that time; the same criterion in the searches and in the Control Centre",
            "Activo y sin indisponibilidad en el horario; el mismo criterio en las búsquedas y en el Centro de control");

    public static LocalizedText VehicleEligibilityRole(string result) =>
        Of("Active, seats and unavailability → {0}", "Activo, plazas e indisponibilidad → {0}", [result]);

    public static LocalizedText AssignRole() =>
        Of($"The aggregate allocates driver and vehicle; {nameof(IUnitOfWork)}.{nameof(IUnitOfWork.SaveChangesAsync)} persists",
            $"El agregado asigna conductor y vehículo; {nameof(IUnitOfWork)}.{nameof(IUnitOfWork.SaveChangesAsync)} persiste");

    public static LocalizedText Saved() => Of("Saved", "Guardado");

    public static LocalizedText NothingPersisted() => Of("Not run: nothing is persisted", "No se ejecuta: no se persiste nada");

    public static LocalizedText PatternFailFast(string orchestrator) =>
        Of("Validation before persisting, fail-fast: {0} orders driver available → compliance → vehicle → driver schedule → vehicle schedule",
            "Validación antes de persistir, fail-fast: {0} ordena conductor disponible → compliance → vehículo → horario conductor → horario vehículo", [orchestrator]);

    public static LocalizedText PatternPureValidators() =>
        Of($"Validators without side effects that return value objects ({nameof(DriverEligibilityCheck)}, {nameof(ComplianceCheckResult)}, {nameof(VehicleEligibilityCheck)}, {nameof(ScheduleConflictCheck)})",
            $"Validadores sin efectos que devuelven value objects ({nameof(DriverEligibilityCheck)}, {nameof(ComplianceCheckResult)}, {nameof(VehicleEligibilityCheck)}, {nameof(ScheduleConflictCheck)})");

    public static LocalizedText PatternOnlyOrchestratorThrows() =>
        Of("Only the orchestrator throws domain exceptions; the global handler turns them into ProblemDetails (422/409)",
            "Solo el orquestador lanza excepciones de dominio; el manejador global las traduce a ProblemDetails (422/409)");

    public static LocalizedText PatternSingleSource() =>
        Of($"Legal figures in a single source: {nameof(DrivingLimits)}", $"Cifras legales en una sola fuente: {nameof(DrivingLimits)}");

    public static LocalizedText HardFilters() => Of("Hard filters", "Filtros duros");

    public static LocalizedText BaseProximity() => Of("How close the base is to the departure", "Cercanía de la base a la salida");

    public static LocalizedText Position(int rank) => Of("Position {0}", "Posición {0}", [rank]);

    public static LocalizedText PositionSameBooking(int rank, string source) =>
        Of("Position {0} (preferred: same booking, through {1})", "Posición {0} (preferido: misma reserva, vía {1})", [rank, source]);

    public static LocalizedText SameBooking() => Of("Same booking", "Misma reserva");

    public static LocalizedText CoversSameBooking(int trip) =>
        Of("Already covers trip #{0} of this booking. Ranked before proximity; it skips no filter.",
            "Ya cubre el viaje #{0} de esta reserva. Se ordena antes que la cercanía; no salta ningún filtro.", [trip]);

    public static LocalizedText TieBreakByHours() => Of("Tie-break by hours", "Desempate por horas");

    public static LocalizedText WonTieBreak(double hours) =>
        Of("Same booking, band and distance as another candidate; {0:0.0} h since the last weekly rest period, fewer than the other. It only ranks, it does not exclude.",
            "Reserva, banda y distancia iguales a las de otro candidato; {0:0.0} h desde su último descanso semanal, menos que él. Solo ordena, no excluye.", [hours]);

    public static LocalizedText WarningTitle() => Of("Warning", "Aviso");

    public static LocalizedText CheapFiltersRole() =>
        Of("Cheap in-memory filters (active, allocatable automatically, unavailability)", "Filtros baratos en memoria (activo, auto-asignable, indisponibilidad)");

    public static LocalizedText Passes() => Of("Passes", "Pasa");

    public static LocalizedText RankingRole(string policy) =>
        Of("Ranking strategy ({0}): proximity band + straight-line distance", "Estrategia de ranking ({0}): banda de cercanía + distancia en línea recta", [policy]);

    public static LocalizedText BandAndDistance(string band, LocalizedText distance) => Of("{0}, {1}", "{0}, {1}", [band, distance]);

    public static LocalizedText OrderRole() =>
        Of("Orders by same booking → band → distance → hours since the weekly rest period (Art. 8.6) → Id and evaluates the expensive checks only in that order until it has N",
            "Ordena por misma reserva → banda → distancia → horas desde el descanso semanal (Art. 8.6) → Id y evalúa lo caro solo en ese orden hasta tener N");

    public static LocalizedText AvailabilityRole(double regular, double reduced) =>
        Of("Overlap with other services and daily availability ({0:0}/{1:0} h)", "Solape con otros servicios y disponibilidad diaria ({0:0}/{1:0} h)", [regular, reduced]);

    public static LocalizedText FitsWorkingDay() => Of("Fits in the working day", "Cabe en su jornada");

    public static LocalizedText ComplianceRole(string engine) =>
        Of("CE 561/2006 through {0}; warnings do not exclude", "CE 561/2006 vía {0}; los avisos no excluyen", [engine]);

    public static LocalizedText Complies() => Of("Complies", "Cumple");

    public static LocalizedText RulesEvaluated(int rules) => Of("{0} rules evaluated", "{0} reglas evaluadas", [rules]);

    public static LocalizedText CandidateRole() =>
        Of("Value object with position, band, distance and reason", "Value object con posición, banda, distancia y motivo");

    public static LocalizedText DriverCandidateSummary(int rank, string driver, LocalizedText position) =>
        Of("#{0} {1}: passes the filters; {2}", "#{0} {1}: pasa los filtros; {2}", [rank, driver, position]);

    public static LocalizedText DriverCandidateSummaryWithWarnings(int rank, string driver, int warnings, LocalizedText position) =>
        Of("#{0} {1}: passes the filters with {2} {2|warning|warnings}; {3}", "#{0} {1}: pasa los filtros con {2} {2|aviso|avisos}; {3}", [rank, driver, warnings, position]);

    public static LocalizedText VehicleCandidateFit(int required, int seats) =>
        Of("The service needs {0} seats and the vehicle has {1}; active, allocatable automatically and with no unavailability or other service at that time.",
            "El servicio necesita {0} plazas y el vehículo tiene {1}; activo, auto-asignable y sin indisponibilidad ni otro servicio en ese horario.", [required, seats]);

    public static LocalizedText VehicleCandidateFitNoSeats(int seats) =>
        Of("The vehicle has {0} seats; active, allocatable automatically and with no unavailability or other service at that time.",
            "El vehículo tiene {0} plazas; activo, auto-asignable y sin indisponibilidad ni otro servicio en ese horario.", [seats]);

    public static LocalizedText CurrentVehiclePosition() => Of("Current position of the vehicle", "Posición actual del vehículo");

    public static LocalizedText ProximityToDeparture() => Of("How close it is to the departure", "Cercanía a la salida");

    public static LocalizedText FitResult() => Of("Fit", "Apto");

    public static LocalizedText LocateVehicleRole() =>
        Of("Discards overlaps and locates the vehicle: last destination → base → unknown", "Descarta solapes y sitúa el vehículo: último destino → base → desconocida");

    public static LocalizedText SameBandsRole() => Of("The same proximity bands as the drivers", "Mismas bandas de cercanía que los conductores");

    public static LocalizedText VehicleCandidateSummary(int rank, string plate, LocalizedText position) =>
        Of("#{0} {1}: fit for the service; {2}", "#{0} {1}: apto para el servicio; {2}", [rank, plate, position]);

    public static LocalizedText FirstByBand(LocalizedText band, LocalizedText distance) =>
        Of("first by band ({0}) and distance ({1}).", "primero por banda ({0}) y distancia ({1}).", [band, distance]);

    public static LocalizedText WorseBand(LocalizedText band, int previous, LocalizedText previousBand) =>
        Of("band: {0}; that of #{1}: {2} (better band).", "banda: {0}; la del #{1}: {2} (banda mejor).", [band, previous, previousBand]);

    public static LocalizedText LongerDistance(int previous, LocalizedText distance, LocalizedText previousDistance) =>
        Of("same band as #{0}; distance: {1} against {2}.", "misma banda que el #{0}; distancia: {1} frente a {2}.", [previous, distance, previousDistance]);

    public static LocalizedText TiedByIdentifier(int previous) =>
        Of("same band and distance as #{0}; tie-break: identifier.", "misma banda y distancia que el #{0}; desempate: identificador.", [previous]);

    public static LocalizedText FirstBySameBooking(int trip) =>
        Of("first by same booking (covers trip #{0}).", "primero por misma reserva (cubre el viaje #{0}).", [trip]);

    public static LocalizedText PreviousCoversBooking(int previous) =>
        Of("#{0} covers another trip of the same booking (criterion before proximity).", "el #{0} cubre otro viaje de la misma reserva (criterio anterior a la cercanía).", [previous]);

    public static LocalizedText TiedByHours(int previous, double hours, double previousHours) =>
        Of("same band and distance as #{0}; hours since the last weekly rest period: {1:0.0} h against {2:0.0} h.",
            "misma banda y distancia que el #{0}; horas desde el último descanso semanal: {1:0.0} h frente a {2:0.0} h.", [previous, hours, previousHours]);

    public static LocalizedText SameBookingAs(int previous, LocalizedText reason) =>
        Of("same booking as #{0}; {1}", "misma reserva que el #{0}; {1}", [previous, reason]);

    public static LocalizedText PatternFiltersFirst() =>
        Of("Hard filters first, ranking afterwards: the ranking orders but never excludes", "Filtros duros primero, ranking después: el ranking ordena pero nunca excluye");

    public static LocalizedText PatternRankingPolicy(string contract, string implementation) =>
        Of("Interchangeable ranking strategy/policy ({0} → {1})", "Strategy/policy de ranking intercambiable ({0} → {1})", [contract, implementation]);

    public static LocalizedText PatternLazyEvaluation() =>
        Of("Lazy evaluation: what is expensive (compliance) is only computed in ranking order until there are N candidates",
            "Evaluación perezosa: lo caro (compliance) solo se calcula en orden de ranking hasta tener N candidatos");

    public static LocalizedText PatternImmutableResults() =>
        Of("Results as immutable value objects with their reason", "Resultados como value objects inmutables con su motivo");

    public static LocalizedText LaterServices() => Of("Later services already assigned", "Servicios posteriores ya asignados");

    public static LocalizedText NoLaterServices(DateTime until) =>
        Of("0 later services assigned until {0:dd/MM HH:mm}.", "0 servicios posteriores asignados hasta el {0:dd/MM HH:mm}.", [until]);

    public static LocalizedText LaterServicesUnharmed(int evaluated, DateTime until) =>
        Of("{0} later {0|service|services} evaluated until {1:dd/MM HH:mm}: none stops complying or gets worse.",
            "{0} {0|servicio posterior evaluado|servicios posteriores evaluados} hasta el {1:dd/MM HH:mm}: ninguno pasa a incumplir ni empeora.", [evaluated, until]);

    public static LocalizedText RestsOf(double actual, double limit) =>
        Of("{0:0} of {1:0} rest periods allowed", "{0:0} de {1:0} descansos permitidos", [actual, limit]);

    public static LocalizedText RestThatFits(double actual, double regular, double reduced) =>
        Of("{0:0.0} h of rest fit; {1:0.0} h are required (or {2:0.0} h reduced)", "Caben {0:0.0} h de descanso; se exigen {1:0.0} h (o {2:0.0} h reducido)", [actual, regular, reduced]);

    public static LocalizedText SinceWeeklyRest(double actual, double limit) =>
        Of("{0:0.0} h since the last weekly rest period; maximum {1:0.0} h", "{0:0.0} h desde el último descanso semanal; máximo {1:0.0} h", [actual, limit]);

    public static LocalizedText HoursOf(double actual, double limit) =>
        Of("{0:0.0} h of {1:0.0} h counting this service", "{0:0.0} h de {1:0.0} h contando este servicio", [actual, limit]);

    public static LocalizedText LoadHistoryRole() =>
        Of("Loads the driver's history (assigned services) and delegates to the engine", "Carga el historial del conductor (servicios asignados) y delega en el motor");

    public static LocalizedText EngineRole() =>
        Of($"Pure CE 561/2006 engine (no I/O), figures of {nameof(DrivingLimits)}", $"Motor puro CE 561/2006 (sin E/S), cifras de {nameof(DrivingLimits)}");

    public static LocalizedText EngineResult(int rules, int broken, int warnings) =>
        Of("{0} rules evaluated, {1} broken, {2} {2|warning|warnings}", "{0} reglas evaluadas, {1} incumplidas, {2} {2|aviso|avisos}", [rules, broken, warnings]);

    public static LocalizedText LaterServicesRole(string engine) =>
        Of("Repeats {0} on every later service, without and with the new one, and compares rule by rule",
            "Repite {0} en cada servicio posterior, sin y con el nuevo, y compara regla a regla", [engine]);

    public static LocalizedText LaterServicesResult(int evaluated, int affected) =>
        Of("{0} services evaluated, {1} stop complying or get worse", "{0} servicios evaluados, {1} pasan a incumplir o empeoran", [evaluated, affected]);

    public static LocalizedText NotRunFailFast() => Of("Not run (fail-fast)", "No se ejecuta (fail-fast)");

    public static LocalizedText Ok() => Of("OK", "OK");

    public static LocalizedText NegativeResult() => Of("Negative result", "Resultado negativo");

    public static LocalizedText DomainExceptionRole() =>
        Of("Domain exception thrown by the orchestrator; the global handler turns it into ProblemDetails",
            "Excepción de dominio lanzada por el orquestador; el manejador global la traduce a ProblemDetails");

    public static LocalizedText HttpNothingPersisted(int status) => Of("HTTP {0}, nothing persisted", "HTTP {0}, sin persistir nada", [status]);

    public static LocalizedText PositionIsOriginAndBaseIsDestination() =>
        Of("It is at the departure and its base is the arrival (best band).", "Su posición es la salida y su base es la llegada (mejor banda).");

    public static LocalizedText BaseIsOriginAndDestination() =>
        Of("The base is the departure and the arrival (best band).", "Su base es la salida y la llegada (mejor banda).");

    public static LocalizedText SubjectAtOrigin(LocalizedText subject) =>
        Of("{0} is the origin of the service (second best band).", "{0} coincide con el origen del servicio (segunda mejor banda).", [subject]);

    public static LocalizedText SubjectBeyondRadius(LocalizedText subject, LocalizedText distance, double radius) =>
        Of("{0} is {1} from the departure, outside the {2:0} km radius: it is not excluded, it goes behind.",
            "{0} está a {1} de la salida, fuera del radio de {2:0} km: no se excluye, va detrás.", [subject, distance, radius]);

    public static LocalizedText VehiclePositionUnknown() =>
        Of("Position of the vehicle unknown: it is not excluded, it goes last.", "Posición del vehículo desconocida: no se excluye, va al final.");

    public static LocalizedText DriverWithoutBase() =>
        Of("The driver has no base: distance unknown; not excluded, goes last.", "El conductor no tiene base asignada: distancia desconocida; no se excluye, va al final.");

    public static LocalizedText SubjectWithinBand(LocalizedText subject, LocalizedText distance, LocalizedText band) =>
        Of("{0} is {1} from the departure in a straight line: within the {2}.", "{0} está a {1} de la salida en línea recta: dentro del {2}.", [subject, distance, band]);

    public static LocalizedText VehiclePositionSubject() => Of("The position of the vehicle", "La posición del vehículo");

    public static LocalizedText DriverBaseSubject() => Of("The driver's base", "La base del conductor");

    public static LocalizedText BandOriginAndDestination() => Of("base at origin and destination", "base en origen y destino");

    public static LocalizedText BandOrigin() => Of("base at the origin", "base en el origen");

    public static LocalizedText BandBeyond() => Of("outside the radius", "fuera de radio");

    public static LocalizedText BandNoPosition() => Of("no position", "sin posición");

    public static LocalizedText BandRadius(double km) => Of("{0:0} km radius", "radio de {0:0} km", [km]);

    public static LocalizedText LastServiceDestination(LocalizedText reason) => Of("Destination of its last service: {0}.", "Destino de su último servicio: {0}.", [reason]);

    public static LocalizedText NoRecentServices(LocalizedText reason) => Of("No recent services, its base: {0}.", "Sin servicios recientes: su base: {0}.", [reason]);

    public static LocalizedText PositionUnknown(LocalizedText reason) => Of("Position unknown: {0}.", "Posición desconocida: {0}.", [reason]);

    public static LocalizedText Kilometres(double km) => Of("{0:0.0} km", "{0:0.0} km", [km]);

    public static LocalizedText UnknownDistance() => Of("unknown distance", "distancia desconocida");
}
