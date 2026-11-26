namespace DispatchGuard.Core.Localization;

/// <summary>Errors of the API.</summary>
public static partial class Texts
{
    public static LocalizedText DriverUnavailableTitle() => Of("The driver is not available for this service", "El conductor no está disponible para este servicio");

    public static LocalizedText NotCompliantTitle() => Of("The driver does not comply with CE 561/2006", "El conductor no cumple CE 561/2006");

    public static LocalizedText VehicleNotFitTitle() => Of("The vehicle is not fit for this service", "El vehículo no es apto para este servicio");

    public static LocalizedText ScheduleOverlapTitle() => Of("Schedule overlap", "Solape de horario");

    public static LocalizedText NotAllocatableTitle() => Of("This service cannot be allocated", "No se puede asignar este servicio");

    public static LocalizedText AgendaEntryTitle() => Of("This agenda entry cannot be changed", "No se puede cambiar esta entrada de la agenda");

    public static LocalizedText AgendaEntryFinished(DateTime end) =>
        Of("This entry ended on {0:dd/MM/yyyy HH:mm}: it stays as history and can be neither changed nor deleted",
            "Esta entrada terminó el {0:dd/MM/yyyy HH:mm}: queda como historial y no se puede cambiar ni borrar", [end]);

    public static LocalizedText DetailsChangedTitle() => Of("Somebody else has changed these data", "Otra persona ha cambiado estos datos");

    public static LocalizedText DetailsChanged() =>
        Of("Nothing was saved: load the current data and try again", "No se ha guardado nada: carga los datos actuales y vuelve a intentarlo");

    public static LocalizedText RepeatedTitle() => Of("Repeated value", "Dato repetido");

    public static LocalizedText RepeatedIdentification(string number) =>
        Of("The ID number {0} belongs to another driver, who may be inactive", "El DNI {0} ya es de otro conductor, que puede estar dado de baja", [number]);

    public static LocalizedText RepeatedPlate(string plate) =>
        Of("The registration number {0} belongs to another vehicle, which may be inactive", "La matrícula {0} ya es de otro vehículo, que puede estar dado de baja", [plate]);

    public static LocalizedText RepeatedTaxId(string? taxId) =>
        Of("The tax ID {0} belongs to another customer, who may be inactive", "El NIF {0} ya es de otro cliente, que puede estar dado de baja", [taxId]);

    public static LocalizedText RepeatedBaseName(string name) =>
        Of("The name {0} belongs to another base, which may be inactive", "El nombre {0} ya es de otra base, que puede estar dada de baja", [name]);

    public static LocalizedText BusyTitle() => Of("Another operation in progress", "Otra operación en curso");

    public static LocalizedText Busy() =>
        Of("Somebody is changing this driver, vehicle or service right now. Nothing was saved: try again in a few seconds.",
            "Alguien está cambiando ahora mismo este conductor, vehículo o servicio. No se ha guardado nada: inténtalo de nuevo en unos segundos.");

    public static LocalizedText AttemptNotRecordedTitle() => Of("The attempt could not be recorded", "No se ha podido registrar el intento");

    public static LocalizedText AttemptNotRecorded() => Of("Nothing was allocated: try again", "No se ha asignado nada: vuelve a intentarlo");

    public static LocalizedText InvalidRequestTitle() => Of("Invalid request", "Petición no válida");

    public static LocalizedText InvalidRequest() => Of("Some value of the request is not valid", "Algún dato de la petición no es válido");

    public static LocalizedText MalformedRequestTitle() => Of("Malformed request", "Petición incorrecta");

    public static LocalizedText MalformedRequest() => Of("The request could not be read", "La petición no se ha podido leer");

    public static LocalizedText NotFoundTitle() => Of("Not found", "No encontrado");

    public static LocalizedText UnexpectedTitle() => Of("Unexpected error", "Error inesperado");

    public static LocalizedText MethodNotAllowedTitle() => Of("Method not allowed", "Método no permitido");

    public static LocalizedText UnsupportedMediaTitle() => Of("Unsupported content type", "Tipo de contenido no admitido");

    public static LocalizedText TooLargeTitle() => Of("Request too large", "Petición demasiado grande");

    public static LocalizedText UnauthorizedTitle() => Of("Not authorised", "No autorizado");

    public static LocalizedText ForbiddenTitle() => Of("Request not allowed", "Petición no permitida");

    public static LocalizedText UnavailableTitle() => Of("Service unavailable", "Servicio no disponible");

    public static LocalizedText HeaderMissing(string header) => Of("The header {0} is missing", "Falta la cabecera {0}", [header]);

    public static LocalizedText SessionMissingTitle() => Of("The session does not exist or has expired", "La sesión no existe o ha caducado");

    public static LocalizedText TooManyRequestsTitle() => Of("Too many requests", "Demasiadas peticiones");

    public static LocalizedText TooManyRequests() => Of("Wait a moment and try again", "Espera un momento y vuelve a intentarlo");

    public static LocalizedText DeletionLockedTitle() => Of("Action disabled in the demo", "Acción desactivada en la demo");

    public static LocalizedText DeletionLocked() =>
        Of("The data of the demo are shared: drivers, vehicles and locations cannot be deleted here.",
            "Los datos de la demo son compartidos: aquí no se pueden borrar conductores, vehículos ni ubicaciones.");

    public static LocalizedText ResetKeyTitle() => Of("Reset key not valid", "Clave de reinicio no válida");

    public static LocalizedText AlreadyRebuildingTitle() => Of("The demo is already being rebuilt", "La demo ya se está rehaciendo");

    public static LocalizedText MissingTrip(int id) => Of("Service {0} does not exist", "El servicio {0} no existe", [id]);

    public static LocalizedText MissingDriver(int id) => Of("Driver {0} does not exist", "El conductor {0} no existe", [id]);

    public static LocalizedText MissingVehicle(int id) => Of("Vehicle {0} does not exist", "El vehículo {0} no existe", [id]);

    public static LocalizedText MissingBooking(int id) => Of("Booking {0} does not exist", "La reserva {0} no existe", [id]);

    public static LocalizedText MissingBase(int id) => Of("Base {0} does not exist", "La base {0} no existe", [id]);

    public static LocalizedText MissingLocation(int id) => Of("Location {0} does not exist", "La ubicación {0} no existe", [id]);

    public static LocalizedText MissingClient(int id) => Of("Customer {0} does not exist", "El cliente {0} no existe", [id]);

    public static LocalizedText MissingAttempt(int id) => Of("Allocation attempt {0} does not exist", "El intento de asignación {0} no existe", [id]);

    public static LocalizedText MissingDriverUnavailability(int unavailability, int driver) =>
        Of("Unavailability {0} does not exist in driver {1}", "La indisponibilidad {0} no existe en el conductor {1}", [unavailability, driver]);

    public static LocalizedText MissingVehicleUnavailability(int unavailability, int vehicle) =>
        Of("Unavailability {0} does not exist in vehicle {1}", "La indisponibilidad {0} no existe en el vehículo {1}", [unavailability, vehicle]);

    /// <param name="subject">Begins the sentence, as in «El nombre no puede».</param>
    public static LocalizedText TooLong(LocalizedText subject, int maximum) =>
        Of("{0} exceed {1} characters", "{0} pasar de {1} caracteres", [subject, maximum]);

    public static LocalizedText NameCannot() => Of("The name cannot", "El nombre no puede");

    public static LocalizedText AddressCannot() => Of("The address cannot", "La dirección no puede");

    public static LocalizedText CityCannot() => Of("The city cannot", "La ciudad no puede");

    public static LocalizedText ProvinceCannot() => Of("The province cannot", "La provincia no puede");

    public static LocalizedText CountryCodeCannot() => Of("The country code cannot", "El código del país no puede");

    public static LocalizedText ServiceDescriptionCannot() => Of("The description of the service cannot", "La descripción del servicio no puede");

    public static LocalizedText NotesCannot() => Of("The notes cannot", "Las notas no pueden");

    public static LocalizedText WorkshopCannot() => Of("The workshop cannot", "El taller no puede");

    public static LocalizedText IdentificationCannot() => Of("The ID number cannot", "El DNI no puede");

    public static LocalizedText LastNameCannot() => Of("The surname cannot", "Los apellidos no pueden");

    public static LocalizedText PlateCannot() => Of("The registration number cannot", "La matrícula no puede");

    public static LocalizedText BrandCannot() => Of("The make cannot", "La marca no puede");

    public static LocalizedText ModelCannot() => Of("The model cannot", "El modelo no puede");

    public static LocalizedText CustomerCannot() => Of("The customer cannot", "El cliente no puede");

    public static LocalizedText DescriptionCannot() => Of("The description cannot", "La descripción no puede");

    public static LocalizedText IdentificationMissing() => Of("The ID number is missing", "Falta el DNI");

    public static LocalizedText NameMissing() => Of("The name is missing", "Falta el nombre");

    public static LocalizedText LastNameMissing() => Of("The surname is missing", "Faltan los apellidos");

    public static LocalizedText PlateMissing() => Of("The registration number is missing", "Falta la matrícula");

    public static LocalizedText BrandMissing() => Of("The make is missing", "Falta la marca");

    public static LocalizedText ModelMissing() => Of("The model is missing", "Falta el modelo");

    public static LocalizedText SeatsAtLeastOne() => Of("The seats must be 1 or more", "Las plazas deben ser 1 o más");

    public static LocalizedText StartMissing() => Of("The start is missing", "Falta el inicio");

    public static LocalizedText EndAfterStart() => Of("The end must be after the start", "El fin debe ser posterior al inicio");

    public static LocalizedText OverrideReasonLength(int minimum, int maximum) =>
        Of("Forcing needs a reason of {0} to {1} characters", "Para forzar hace falta un motivo de {0} a {1} caracteres", [minimum, maximum]);

    public static LocalizedText OverrideByMissing(int maximum) =>
        Of("Forcing needs the name of who decides it (at most {0} characters)", "Para forzar hace falta el nombre de quien lo decide (máx. {0} caracteres)", [maximum]);

    public static LocalizedText RecommendationNotForcible() => Of("A recommendation cannot be forced", "Una recomendación no se puede forzar");

    public static LocalizedText BookingOrPeriodNeeded() =>
        Of("A booking or a period (From and To) is needed", "Hace falta una reserva o un periodo (From y To)");

    public static LocalizedText TooManyLines(int maximum) => Of("At most {0} lines", "Como mucho {0} líneas", [maximum]);

    public static LocalizedText EmptyLine() => Of("There is an empty line", "Hay una línea vacía");

    public static LocalizedText RepeatedServices() => Of("There are repeated services", "Hay servicios repetidos");

    public static LocalizedText ProposalTooLong(int days) =>
        Of("The services of a proposal span {0} days at most", "Los servicios de una propuesta abarcan como mucho {0} días", [days]);

    public static LocalizedText UnknownValue(string field, string value) => Of("Unknown {0}: {1}", "{0} desconocido: {1}", [field, value]);

    public static LocalizedText DetailsVersionMalformed() =>
        Of("DetailsVersion is not the version of any data: send the one received when reading them",
            "DetailsVersion no es la versión de unos datos: envía la que se recibió al leerlos");

    public static LocalizedText FieldRequired(string field) => Of("{0} is required", "El campo {0} es obligatorio", [field]);

    public static LocalizedText FieldPositive(string field) => Of("{0} must be greater than zero", "{0} debe ser mayor que cero", [field]);

    public static LocalizedText FieldInFuture(string field) => Of("{0} cannot be in the future", "{0} no puede estar en el futuro", [field]);

    public static LocalizedText FieldBetween(string field, int minimum, int maximum) =>
        Of("{0} must be between {1} and {2}", "{0} debe estar entre {1} y {2}", [field, minimum, maximum]);

    public static LocalizedText FieldAtLeast(string field, int minimum) => Of("{0} must be {1} or more", "{0} debe ser {1} o más", [field, minimum]);

    public static LocalizedText FieldOneOf(string field, string values) => Of("{0} must be one of {1}", "{0} debe ser uno de {1}", [field, values]);

    public static LocalizedText FieldAfter(string field, string other) => Of("{0} must be after {1}", "{0} debe ser posterior a {1}", [field, other]);

    public static LocalizedText RangeTooLong(double days) => Of("The range cannot exceed {0:0} days", "El intervalo no puede pasar de {0:0} días", [days]);

    public static LocalizedText InvalidEmail() => Of("The e-mail address is not valid", "El correo electrónico no es válido");

    public static LocalizedText InvalidPhone() => Of("The phone number is not valid", "El teléfono no es válido");

    public static LocalizedText PhoneTooShort(int digits) =>
        Of("The phone number must have at least {0} digits", "El teléfono debe tener al menos {0} cifras", [digits]);

    public static LocalizedText TaxIdTooShort(int minimum) =>
        Of("The tax ID must have at least {0} characters", "El NIF debe tener al menos {0} caracteres", [minimum]);

    public static LocalizedText BookingNeedsTrips() => Of("A booking needs at least one trip", "Una reserva necesita al menos un viaje");

    public static LocalizedText BookingTooManyTrips(int maximum) =>
        Of("A booking cannot have more than {0} trips", "Una reserva no puede tener más de {0} viajes", [maximum]);

    public static LocalizedText NullTrip() => Of("A trip of the booking is empty", "Hay un viaje vacío en la reserva");

    public static LocalizedText NullCharacter() => Of("A text cannot have the null character", "Un texto no puede llevar el carácter nulo");

    public static LocalizedText InvalidVisitName() => Of("Name not valid", "Nombre no válido");

    // Messages of the annotations of the DTOs: {1} is written by the annotation itself.
    public static LocalizedText NameRequiredAnnotation() => Of("The name is required", "El nombre es obligatorio");

    public static LocalizedText NameLengthAnnotation() => Of("The name cannot exceed {1} characters", "El nombre no puede superar los {1} caracteres");

    public static LocalizedText TaxIdLengthAnnotation() => Of("The tax ID cannot exceed {1} characters", "El NIF no puede superar los {1} caracteres");

    public static LocalizedText EmailLengthAnnotation() =>
        Of("The e-mail address cannot exceed {1} characters", "El correo electrónico no puede superar los {1} caracteres");

    public static LocalizedText PhoneLengthAnnotation() => Of("The phone number cannot exceed {1} characters", "El teléfono no puede superar los {1} caracteres");

    public static LocalizedText ReasonLengthAnnotation() => Of("The reason cannot exceed {1} characters", "El motivo no puede superar los {1} caracteres");
}
