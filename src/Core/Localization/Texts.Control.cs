namespace DispatchGuard.Core.Localization;

/// <summary>Control Centre.</summary>
public static partial class Texts
{
    public static LocalizedText ServiceLabel(int id) => Of("Service {0}", "Servicio {0}", [id]);

    public static LocalizedText ActionAssign() => Of("Assign driver and vehicle", "Asignar conductor y vehículo");

    public static LocalizedText ActionCompleteBooking() => Of("Complete the booking", "Completar la reserva");

    public static LocalizedText TripsCovered(int covered, int total) => Of("{0} of {1} trips covered", "{0} de {1} viajes cubiertos", [covered, total]);

    public static LocalizedText ActionReviewForced() => Of("Review the forced allocation", "Revisar la asignación forzada");

    public static LocalizedText ActionFindReplacement() => Of("Find a replacement", "Buscar sustituto");

    public static LocalizedText NotAvailableForService(int id) => Of("Not available for service {0}", "No disponible en el servicio {0}", [id]);

    public static LocalizedText ActionReviewNextServices() => Of("Review the next services", "Revisar sus próximos servicios");

    public static LocalizedText NearLimits(int percent, IReadOnlyList<LocalizedText> limits) =>
        Of("At {0} % or more of the {1} limit", "Al {0} % o más del límite {1}", [percent, limits]);

    public static LocalizedText DailyLimit() => Of("daily", "diario");

    public static LocalizedText WeeklyLimit() => Of("weekly", "semanal");

    public static LocalizedText TwoWeeksLimit() => Of("two-week", "de dos semanas");

    public static LocalizedText ActionChangeVehicle() => Of("Change the vehicle", "Cambiar de vehículo");

    public static LocalizedText PendingToday(int services) =>
        Of("{0} pending {0|service|services} today", "{0} {0|servicio pendiente|servicios pendientes} hoy", [services]);

    public static LocalizedText SeatsShortForOne(int seats, int service, int required) =>
        Of("It has {0} {0|seat|seats} and service {1} needs {2}", "Tiene {0} {0|plaza|plazas} y el servicio {1} necesita {2}", [seats, service, required]);

    public static LocalizedText SeatsShortForMany(int seats, int services, int required) =>
        Of("It has {0} {0|seat|seats} and {1} services need more, up to {2}", "Tiene {0} {0|plaza|plazas} y {1} servicios necesitan más, hasta {2}", [seats, services, required]);

    public static LocalizedText EverythingCovered() => Of("Everything covered", "Todo cubierto");

    public static LocalizedText NothingPending() => Of("Nothing pending", "Nada pendiente");

    public static LocalizedText ServicesCovered(int covered, int total) => Of("{0} of {1} services covered.", "{0} de {1} servicios cubiertos.", [covered, total]);

    public static LocalizedText TasksLeft(int tasks) =>
        Of("{0} {0|task|tasks} left to solve", "{0|Queda|Quedan} {0} {0|tarea|tareas} por resolver", [tasks]);

    public static LocalizedText UrgentAndWarnings(int urgent, int warnings, LocalizedText coverage) =>
        Of("{0} urgent and {1} {1|warning|warnings}. {2}", "{0} {0|urgente|urgentes} y {1} {1|aviso|avisos}. {2}", [urgent, warnings, coverage]);
}
