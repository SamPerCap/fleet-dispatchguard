namespace DispatchGuard.Core.Localization;

/// <summary>CE 561/2006, availability of drivers and vehicles, and state of the services.</summary>
public static partial class Texts
{
    public static LocalizedText ContinuousDriving() => Of("Driving period without a break of 45 min", "Conducción continua sin pausa de 45 min");

    public static LocalizedText DailyDriving() => Of("Daily driving time", "Conducción diaria");

    public static LocalizedText WeeklyDriving() => Of("Weekly driving time", "Conducción semanal");

    public static LocalizedText TwoWeeksDriving() => Of("Driving time in two consecutive weeks", "Conducción en dos semanas");

    public static LocalizedText DailyRest() => Of("Daily rest period within 24 h", "Descanso diario dentro de 24 h");

    public static LocalizedText ReducedDailyRests() =>
        Of("Reduced daily rest periods between weekly rest periods", "Descansos diarios reducidos entre descansos semanales");

    public static LocalizedText WeeklyRest() => Of("Weekly rest period within 6×24 h", "Descanso semanal dentro de 6×24 h");

    public static LocalizedText ContinuousDrivingExceeded(double hours, double maximum) =>
        Of("Driving period: {0:0.##} h (maximum {1:0.##} h)", "Conducción continua: {0:0.##} h (máximo {1:0.##} h)", [hours, maximum]);

    public static LocalizedText DailyDrivingExceeded(double hours, double maximum) =>
        Of("Daily driving time: {0:0.##} h (maximum {1:0.##} h)", "Conducción diaria: {0:0.##} h (máximo {1:0.##} h)", [hours, maximum]);

    public static LocalizedText WeeksExceeded(LocalizedText rule, double hours, double maximum) =>
        Of("{0}: {1:0.##} h (maximum {2:0.##} h)", "{0}: {1:0.##} h (máximo {2:0.##} h)", [rule, hours, maximum]);

    public static LocalizedText WeeksExceededInClock(LocalizedText rule, double hours, LocalizedText clock, double maximum) =>
        Of("{0}: {1:0.##} h in {2} (maximum {3:0.##} h)", "{0}: {1:0.##} h en {2} (máximo {3:0.##} h)", [rule, hours, clock, maximum]);

    public static LocalizedText UtcClock() => Of("UTC", "UTC");

    public static LocalizedText MadridClock() => Of("Madrid time", "hora de Madrid");

    public static LocalizedText WorkingDaySince(DateTime start) =>
        Of("Working day since {0:dd/MM HH:mm}", "Jornada desde el {0:dd/MM HH:mm}", [start]);

    public static LocalizedText ExtendedLimit(int used, int maximum) =>
        Of("Extended limit: {0} of {1} extended days used this week", "Límite ampliado: {0} de {1} días ampliados usados esta semana", [used, maximum]);

    public static LocalizedText ExtendedDaysUsedUp(int maximum) =>
        Of("The {0} extended days of the week were already used", "Ya se usaron los {0} días ampliados de la semana", [maximum]);

    public static LocalizedText StrictestExtendedDays(LocalizedText clock) =>
        Of("Two counts of the extended days (Madrid time and UTC): the strictest one applies, {0}",
            "Dos cuentas de los días ampliados (hora de Madrid y UTC): se aplica la más estricta, {0}", [clock]);

    public static LocalizedText WorkingDayAcrossMonday() =>
        Of("Working day across Monday: it counts in the week in which it starts (our own reading, not legal text)",
            "Jornada que cruza el lunes: cuenta en la semana en que empieza (criterio propio, no texto legal)");

    public static LocalizedText WeekOf(string monday) => Of("Week of {0}", "Semana del {0}", [monday]);

    public static LocalizedText WeeksOf(string first, string second) => Of("Weeks of {0} and {1}", "Semanas del {0} y del {1}", [first, second]);

    public static LocalizedText StrictestWeek(LocalizedText week, LocalizedText clock) =>
        Of("{0}. Two counts, in Madrid time and in UTC: the figure is the strictest one, {1}",
            "{0}. Dos cuentas, en hora de Madrid y en UTC: la cifra es la más estricta, {1}", [week, clock]);

    public static LocalizedText WorkingDayRest(DateTime start, DateTime end, LocalizedText rest) =>
        Of("Working day {0:dd/MM HH:mm}–{1:dd/MM HH:mm}: {2} rest period", "Jornada {0:dd/MM HH:mm}–{1:dd/MM HH:mm}: descanso {2}", [start, end, rest]);

    public static LocalizedText RegularRest() => Of("regular", "regular");

    public static LocalizedText ReducedRest(double minimum) => Of("reduced (minimum {0:0} h)", "reducido (mínimo {0:0} h)", [minimum]);

    public static LocalizedText InsufficientRest() => Of("insufficient", "insuficiente");

    public static LocalizedText NoDailyRestFits(DateTime start, DateTime end, double regular, double reduced, double window) =>
        Of("Working day {0:dd/MM HH:mm}–{1:dd/MM HH:mm}: no daily rest period ({2:0.##} h, or {3:0.##} h reduced) fits within the {4:0.##} h from its start",
            "Jornada {0:dd/MM HH:mm}–{1:dd/MM HH:mm}: no cabe un descanso diario ({2:0.##} h, o {3:0.##} h reducido) dentro de las {4:0.##} h desde su inicio",
            [start, end, regular, reduced, window]);

    public static LocalizedText TooManyReducedRests(int reduced, int maximum) =>
        Of("Reduced daily rest periods between weekly rest periods: {0} (maximum {1})",
            "Descansos diarios reducidos entre descansos semanales: {0} (máximo {1})", [reduced, maximum]);

    public static LocalizedText WeeklyRestUntilDeparture() =>
        Of("The weekly rest period lasts until the departure of this service, by schedule",
            "El descanso semanal llega hasta la salida de este servicio, según horario");

    public static LocalizedText LastWeeklyRestEnded(DateTime end) =>
        Of("Last weekly rest period ended on {0:dd/MM HH:mm}", "Último descanso semanal terminado el {0:dd/MM HH:mm}", [end]);

    public static LocalizedText NoWeeklyRestKnown() =>
        Of("There is no weekly rest period in the known history", "No hay descanso semanal en el historial conocido");

    public static LocalizedText NotEnoughHistory(LocalizedText note) =>
        Of("{0}: not enough history to verify it", "{0}: historial insuficiente para verificarlo", [note]);

    public static LocalizedText WeeklyRestUnverifiable() =>
        Of("Not enough history to verify the weekly rest period: check the driver's tachograph before departure",
            "Historial insuficiente para verificar el descanso semanal: confirma el tacógrafo del conductor antes de la salida");

    public static LocalizedText WeeklyRestDue(double regular, double reduced, double interval) =>
        Of("Weekly rest period due: none ({0:0.##} h, or {1:0.##} h reduced) in the last {2:0.##} h",
            "Descanso semanal pendiente: ninguno ({0:0.##} h, o {1:0.##} h reducido) en las últimas {2:0.##} h", [regular, reduced, interval]);

    public static LocalizedText LaterServiceSameLimit(
        string service, DateTime start, string article, LocalizedText rule, LocalizedText with, LocalizedText without, LocalizedText limit) =>
        Of("Service #{0} ({1:dd/MM HH:mm}), {2} {3:lower}: {4} with this service, {5} without it ({6})",
            "Servicio #{0} ({1:dd/MM HH:mm}), {2} {3:lower}: {4} con este servicio, {5} sin él ({6})",
            [service, start, article, rule, with, without, limit]);

    public static LocalizedText LaterServiceOtherLimit(
        string service, DateTime start, string article, LocalizedText rule, LocalizedText with, LocalizedText limitWith, LocalizedText without, LocalizedText limitWithout) =>
        Of("Service #{0} ({1:dd/MM HH:mm}), {2} {3:lower}: {4} with this service ({5}), {6} without it ({7})",
            "Servicio #{0} ({1:dd/MM HH:mm}), {2} {3:lower}: {4} con este servicio ({5}), {6} sin él ({7})",
            [service, start, article, rule, with, limitWith, without, limitWithout]);

    public static LocalizedText AtLeast(double hours) => Of("minimum {0:0.##} h", "mínimo {0:0.##} h", [hours]);

    public static LocalizedText AtMost(LocalizedText amount) => Of("maximum {0}", "máximo {0}", [amount]);

    public static LocalizedText NoLimit() => Of("no limit", "sin límite");

    public static LocalizedText NotEvaluatedFigure() => Of("not evaluated", "no evaluado");

    public static LocalizedText NoneInHistory() => Of("none in the history", "ninguno en el historial");

    public static LocalizedText AmountInClock(LocalizedText amount, LocalizedText clock) => Of("{0} in {1}", "{0} en {1}", [amount, clock]);

    public static LocalizedText AmountOfHours(double hours) => Of("{0:0.##} h", "{0:0.##} h", [hours]);

    public static LocalizedText AmountOfRests(double rests) => Of("{0:0.##} rest periods", "{0:0.##} descansos", [rests]);

    public static LocalizedText DriverNotCompliant() => Of("The driver does not comply with CE 561/2006.", "El conductor no cumple CE 561/2006.");

    public static LocalizedText DriverNotActive() => Of("Driver not active", "Conductor no activo");

    public static LocalizedText DriverNotAvailable(LocalizedText type, DateTime start, DateTime end) =>
        Of("Driver not available ({0}) from {1:dd/MM/yyyy HH:mm} to {2:dd/MM/yyyy HH:mm}",
            "Conductor no disponible ({0}) del {1:dd/MM/yyyy HH:mm} al {2:dd/MM/yyyy HH:mm}", [type, start, end]);

    public static LocalizedText Vacation() => Of("annual leave", "vacaciones");

    public static LocalizedText SickLeave() => Of("sick leave", "baja médica");

    public static LocalizedText WeeklyRestAbsence() => Of("weekly rest", "descanso semanal");

    public static LocalizedText Training() => Of("training", "formación");

    public static LocalizedText PersonalLeave() => Of("personal leave", "asuntos propios");

    public static LocalizedText OtherUnavailability() => Of("other unavailability", "otra indisponibilidad");

    public static LocalizedText VehicleNotActive() => Of("Vehicle not active", "Vehículo no activo");

    public static LocalizedText VehicleRetired(string date) => Of("Vehicle not active (retired on {0})", "Vehículo no activo (retirado el {0})", [date]);

    public static LocalizedText NotEnoughSeats(int seats, int required) =>
        Of("Not enough seats: it has {0}, the service needs {1}", "Plazas insuficientes: tiene {0}, el servicio necesita {1}", [seats, required]);

    public static LocalizedText VehicleNotAvailable(LocalizedText type, DateTime start, DateTime end) =>
        Of("Vehicle not available ({0}) from {1:dd/MM/yyyy HH:mm} to {2:dd/MM/yyyy HH:mm}",
            "Vehículo no disponible ({0}) del {1:dd/MM/yyyy HH:mm} al {2:dd/MM/yyyy HH:mm}", [type, start, end]);

    public static LocalizedText Maintenance() => Of("maintenance", "mantenimiento");

    public static LocalizedText TechnicalInspection() => Of("roadworthiness test", "ITV");

    public static LocalizedText Repair() => Of("repair at the workshop", "reparación en taller");

    public static LocalizedText Cleaning() => Of("cleaning", "limpieza");

    public static LocalizedText Administrative() => Of("paperwork", "gestión administrativa");

    public static LocalizedText VehicleNotFit() => Of("The vehicle is not fit for this service.", "El vehículo no es apto para este servicio.");

    public static LocalizedText OverlapsAnotherService() => Of("It overlaps another service.", "Se solapa con otro servicio.");

    public static LocalizedText OverlapsServices(string services) => Of("It overlaps: {0}", "Se solapa con: {0}", [services]);

    public static LocalizedText ServiceCancelled(int id) => Of("Service #{0} is cancelled", "El servicio #{0} está cancelado", [id]);

    public static LocalizedText ServiceAlreadyAssigned(int id) => Of("Service #{0} is already assigned", "El servicio #{0} ya está asignado", [id]);

    public static LocalizedText ServiceLocked(int id) => Of("Service #{0} is locked", "El servicio #{0} está bloqueado", [id]);

    public static LocalizedText ServiceNotPending(int id) => Of("Service #{0} is no longer pending", "El servicio #{0} ya no está pendiente", [id]);

    public static LocalizedText ServiceDeparted(int id) => Of("Service #{0} has already departed", "El servicio #{0} ya ha salido", [id]);

    public static LocalizedText ForcedBy(string by, string reason) => Of("Forced by {0}: {1}", "Forzado por {0}: {1}", [by, reason]);

    public static LocalizedText ForcedByService(int service, string by, string reason) =>
        Of("Forced by service #{0} (decided by {1}): {2}", "Forzado por el servicio #{0} (lo decidió {1}): {2}", [service, by, reason]);

    public static LocalizedText LateFromService(int service, double speed) =>
        Of("does not arrive from its service #{0} at {1:0} km/h (estimate)", "no llega desde su servicio #{0} a {1:0} km/h (estimación)", [service, speed]);

    public static LocalizedText LateToService(int service, double speed) =>
        Of("does not arrive at its next service #{0} at {1:0} km/h (estimate)", "no llega a su siguiente servicio #{0} a {1:0} km/h (estimación)", [service, speed]);

    /// <summary>Detail of a forced attempt (SPECs 0056 R5 and 0063 R9).</summary>
    public static LocalizedText ForcedDetail(string reason, LocalizedText broken) =>
        Of("Reason: {0} · Rules broken: {1}", "Motivo: {0} · Reglas incumplidas: {1}", [reason, broken]);

    public static LocalizedText ForcedDetailWithAffected(string reason, string affected, LocalizedText broken) =>
        Of("Reason: {0} · Later services affected: {1} · Rules broken: {2}",
            "Motivo: {0} · Servicios posteriores afectados: {1} · Reglas incumplidas: {2}", [reason, affected, broken]);
}
