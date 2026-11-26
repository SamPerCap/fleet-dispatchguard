namespace DispatchGuard.DemoData;

public sealed record DemoLocation(string Key, string Name, string Address, decimal Latitude, decimal Longitude, string City, string Province);

public sealed record DemoDriver(string IdentificationNumber, string FirstName, string LastName, string HomeKey, DayOfWeek[] DaysOff);

public sealed record DemoVehicle(string LicensePlate, string Brand, string Model, int SeatingCapacity, string HomeKey);

/// <summary>A fictitious regular line; one service = <paramref name="Rounds"/> out-and-back trips from the origin.</summary>
public sealed record DemoLine(string Code, string OriginKey, string DestinationKey, int Minutes, int RequiredSeats, int Rounds,
    string Customer, bool Afternoon, bool Weekend);

/// <summary>One leg of a trip that is not a line service (excursion, circuit, transfer).</summary>
public sealed record DemoLeg(string OriginKey, string DestinationKey, int Minutes);

/// <param name="IsDefault">The API's base 1, renamed to <paramref name="Name"/>.</param>
public sealed record DemoBase(
    string Key, string Name, bool IsDefault,
    IReadOnlyList<DemoLocation> Locations, IReadOnlyList<DemoDriver> Drivers, IReadOnlyList<DemoVehicle> Vehicles,
    IReadOnlyList<DemoLine> Lines,
    string HubKey, string AirportKey, int AirportMinutes,
    string ExcursionName, DemoLeg Excursion, string ExcursionCustomer,
    string CircuitName, IReadOnlyList<DemoLeg> Circuit, string CircuitCustomer);

/// <summary>
/// Stops are real public locations (interurban bus interchanges, stations, airports) transcribed by hand from public
/// maps; coordinates are approximate. The company, lines, timetables, customers, people and plates are invented (SPEC 0053).
/// </summary>
public static class DemoDataset
{
    public const string Source = "Public locations (interchanges, stations and airports) transcribed by hand; approximate coordinates.";

    private static DayOfWeek[] Off(DayOfWeek first, DayOfWeek second) => [first, second];

    // Drivers rotate through these pairs of consecutive days off, so that every weekday keeps enough people.
    private static readonly DayOfWeek[][] OffPatterns =
    [
        Off(DayOfWeek.Saturday, DayOfWeek.Sunday), Off(DayOfWeek.Sunday, DayOfWeek.Monday), Off(DayOfWeek.Monday, DayOfWeek.Tuesday),
        Off(DayOfWeek.Tuesday, DayOfWeek.Wednesday), Off(DayOfWeek.Wednesday, DayOfWeek.Thursday), Off(DayOfWeek.Thursday, DayOfWeek.Friday),
        Off(DayOfWeek.Friday, DayOfWeek.Saturday),
    ];

    private static readonly string[] FirstNames =
    [
        "Lucía", "Javier", "Marta", "Andrés", "Elena", "Pablo", "Sara", "Diego", "Carmen", "Raúl", "Nuria", "Iván",
        "Laura", "Sergio", "Patricia", "Alberto", "Rocío", "Fernando", "Beatriz", "Óscar", "Cristina", "Manuel", "Silvia",
        "Rubén", "Alicia", "Hugo", "Irene", "Tomás", "Pilar", "Adrián", "Noelia", "Jorge", "Eva", "Vicente", "Amparo", "Josep",
        "Inmaculada", "Joan", "Rosa", "Salvador",
    ];

    private static readonly string[] Surnames =
    [
        "Martín", "Gómez", "Sanz", "Ortega", "Ruiz", "Delgado", "Navarro", "Gil", "Moreno", "Cano", "Herrero", "Vidal",
        "Iglesias", "León", "Castro", "Peña", "Ramos", "Prieto", "Domínguez", "Vega", "Serrano", "Lozano", "Molina", "Rubio",
        "Ferrer", "Soler", "Martí", "Llorens", "Puig", "Romero", "Torres", "Blasco", "Pastor", "Esteve", "Medina", "Garrido",
    ];

    private static readonly (string Brand, string Model, int Seats)[] Fleet =
    [
        ("Irizar", "i6S", 55), ("Scania", "Interlink", 55), ("Volvo", "9700", 59), ("MAN", "Lion's Coach", 57),
        ("Setra", "S 515 HD", 50), ("Volvo", "8900", 50), ("Irizar", "i4", 55), ("Mercedes-Benz", "Intouro", 35),
        ("Mercedes-Benz", "Tourismo K", 35), ("Iveco", "Daily Indcar Mago", 25), ("Mercedes-Benz", "Sprinter Travel", 19),
        ("Mercedes-Benz", "Sprinter Travel", 19),
    ];

    public const int DriversPerBase = 18;
    public const int VehiclesPerBase = 12;

    private static readonly DemoLocation[] MadridStops =
    [
        new("AVAM", "Intercambiador Avenida de América", "Av. de América 9A", 40.4380m, -3.6765m, "Madrid", "Madrid"),
        new("MONC", "Intercambiador Moncloa", "Pl. de la Moncloa", 40.4353m, -3.7192m, "Madrid", "Madrid"),
        new("PCAS", "Intercambiador Plaza de Castilla", "Pl. de Castilla", 40.4667m, -3.6890m, "Madrid", "Madrid"),
        new("PPIO", "Intercambiador Príncipe Pío", "Paseo de la Florida 2", 40.4213m, -3.7203m, "Madrid", "Madrid"),
        new("MAD4", "Aeropuerto Madrid-Barajas T4", "Terminal T4", 40.4919m, -3.5927m, "Madrid", "Madrid"),
        new("VILL", "Collado Villalba - Estación", "Pl. de la Estación", 40.6365m, -4.0060m, "Collado Villalba", "Madrid"),
        new("ESCO", "San Lorenzo de El Escorial - Estación de autobuses", "C. Juan de Toledo", 40.5915m, -4.1475m, "San Lorenzo de El Escorial", "Madrid"),
        new("ALCB", "Alcobendas - Marqués de la Valdavia", "Av. Marqués de la Valdavia", 40.5400m, -3.6400m, "Alcobendas", "Madrid"),
        new("FUEN", "Fuenlabrada - Estación Central", "C. Luis Sauquillo", 40.2840m, -3.7970m, "Fuenlabrada", "Madrid"),
        new("GETA", "Getafe Central", "Pl. de la Estación", 40.3030m, -3.7320m, "Getafe", "Madrid"),
        new("LEGA", "Leganés Central", "Av. de la Universidad", 40.3280m, -3.7710m, "Leganés", "Madrid"),
        new("MAJA", "Majadahonda - Gran Vía", "Gran Vía", 40.4730m, -3.8720m, "Majadahonda", "Madrid"),
    ];

    private static readonly DemoLocation[] HenaresStops =
    [
        new("ALCA", "Alcalá de Henares - Vía Complutense", "Vía Complutense", 40.4890m, -3.3660m, "Alcalá de Henares", "Madrid"),
        new("TORR", "Torrejón de Ardoz - Plaza de España", "Pl. de España", 40.4580m, -3.4790m, "Torrejón de Ardoz", "Madrid"),
        new("GUAD", "Guadalajara - Estación de autobuses", "C. Doctor Fleming", 40.6340m, -3.1650m, "Guadalajara", "Guadalajara"),
        new("COSL", "Coslada Central", "Av. de la Constitución", 40.4250m, -3.5610m, "Coslada", "Madrid"),
        new("SFDH", "San Fernando de Henares - Plaza de Fernando VI", "Pl. de Fernando VI", 40.4265m, -3.5340m, "San Fernando de Henares", "Madrid"),
        new("AZUQ", "Azuqueca de Henares - Estación", "Pl. de la Estación", 40.5650m, -3.2660m, "Azuqueca de Henares", "Guadalajara"),
    ];

    private static readonly DemoLocation[] ValenciaStops =
    [
        new("VLCE", "València - Estació d'Autobusos", "Av. de Menéndez Pidal 11", 39.4800m, -0.3855m, "València", "Valencia"),
        new("VLCN", "València - Estació del Nord", "C. de Xàtiva 24", 39.4666m, -0.3772m, "València", "Valencia"),
        new("VLCA", "Aeropuerto de València", "Ctra. del Aeropuerto, Manises", 39.4893m, -0.4816m, "Manises", "Valencia"),
        new("VLCC", "Ciutat de les Arts i les Ciències", "Av. del Professor López Piñero 7", 39.4548m, -0.3506m, "València", "Valencia"),
        new("SAGU", "Sagunt - Estació", "Av. del País Valencià", 39.6760m, -0.2790m, "Sagunt", "Valencia"),
        new("ALZI", "Alzira - Estació", "Pl. de l'Estació", 39.1530m, -0.4380m, "Alzira", "Valencia"),
        new("GAND", "Gandia - Estació", "Pl. de l'Estació", 38.9690m, -0.1860m, "Gandia", "Valencia"),
        new("PALM", "El Palmar - L'Albufera", "C. de Caudete", 39.3150m, -0.3240m, "València", "Valencia"),
    ];

    public static readonly IReadOnlyList<DemoBase> Bases =
    [
        Build("MAD", "Madrid", isDefault: true, number: 0, MadridStops,
            [
                new("M1", "PCAS", "ALCB", 35, 30, 2, "Concesión Zona Norte (demo)", true, true),
                new("M2", "MONC", "MAJA", 30, 40, 2, "Concesión Noroeste (demo)", true, true),
                new("M3", "PPIO", "FUEN", 45, 40, 2, "Concesión Zona Sur (demo)", true, false),
                new("M4", "MONC", "VILL", 50, 45, 2, "Concesión Sierra Noroeste (demo)", false, false),
                new("M5", "PPIO", "LEGA", 35, 40, 2, "Concesión Zona Sur (demo)", false, false),
                new("M6", "PPIO", "GETA", 40, 40, 2, "Concesión Zona Sur (demo)", false, false),
            ],
            hub: "AVAM", airport: "MAD4", airportMinutes: 30,
            "Excursión al Monasterio de El Escorial", new DemoLeg("MONC", "ESCO", 65), "Asociación Cultural Sierra Oeste (demo)",
            "Circuito Sierra de Madrid", [new("MONC", "VILL", 50), new("VILL", "ESCO", 25), new("ESCO", "MONC", 65)], "Viajes Castellana (demo)"),
        Build("HEN", "Corredor del Henares", isDefault: false, number: 1, HenaresStops,
            [
                new("H1", "ALCA", "TORR", 25, 40, 3, "Concesión Corredor del Henares (demo)", true, true),
                new("H2", "ALCA", "GUAD", 40, 45, 2, "Concesión Corredor del Henares (demo)", true, true),
                new("H3", "TORR", "COSL", 20, 30, 3, "Concesión Corredor del Henares (demo)", true, false),
                new("H4", "ALCA", "AZUQ", 30, 35, 2, "Concesión Henares Este (demo)", false, false),
                new("H5", "COSL", "SFDH", 15, 25, 3, "Concesión Henares Oeste (demo)", false, false),
            ],
            hub: "ALCA", airport: "MAD4", airportMinutes: 35,
            "Excursión a Guadalajara y su casco histórico", new DemoLeg("ALCA", "GUAD", 40), "Club de Jubilados Complutense (demo)",
            "Circuito Ruta del Henares", [new("ALCA", "GUAD", 40), new("GUAD", "AZUQ", 20), new("AZUQ", "ALCA", 30)], "Congresos Ribera del Henares (demo)"),
        Build("VLC", "València", isDefault: false, number: 2, ValenciaStops,
            [
                new("V1", "VLCE", "SAGU", 40, 45, 2, "Concessió Camp de Morvedre (demo)", true, true),
                new("V2", "VLCN", "ALZI", 50, 45, 2, "Concessió La Ribera (demo)", true, true),
                new("V3", "VLCN", "GAND", 70, 50, 1, "Concessió La Safor (demo)", true, false),
                new("V4", "VLCE", "VLCC", 20, 30, 3, "Concessió València Ciutat (demo)", false, false),
                new("V5", "VLCN", "PALM", 30, 30, 2, "Concessió L'Albufera (demo)", false, false),
            ],
            hub: "VLCN", airport: "VLCA", airportMinutes: 25,
            "Excursión a L'Albufera y El Palmar", new DemoLeg("VLCE", "PALM", 30), "Casal Faller del Carme (demo)",
            "Circuito Costa Valenciana", [new("VLCE", "SAGU", 40), new("VLCN", "GAND", 70), new("GAND", "VLCN", 70)], "Hotel Turia Centro (demo)"),
    ];

    public static IEnumerable<DemoLocation> Locations => Bases.SelectMany(b => b.Locations);

    public static DemoLocation Location(string key) => Locations.First(l => l.Key == key);

    private static DemoBase Build(string key, string name, bool isDefault, int number, DemoLocation[] stops, DemoLine[] lines,
        string hub, string airport, int airportMinutes, string excursionName, DemoLeg excursion, string excursionCustomer,
        string circuitName, DemoLeg[] circuit, string circuitCustomer)
    {
        var drivers = Enumerable.Range(0, DriversPerBase).Select(i =>
        {
            var driverNumber = number * DriversPerBase + i;
            var dni = 1000 + driverNumber; // Fictitious, but with a valid control letter.
            return new DemoDriver($"{dni:00000000}{"TRWAGMYFPDXBNJZSQVHLCKE"[dni % 23]}",
                FirstNames[driverNumber % FirstNames.Length], $"{Surnames[driverNumber % Surnames.Length]} {Surnames[(driverNumber * 7 + 3) % Surnames.Length]}",
                stops[i % stops.Length].Key, OffPatterns[i % OffPatterns.Length]);
        }).ToList();

        const string consonants = "BCDFGHJKLMNPRSTVWXYZ";
        var vehicles = Fleet.Select((f, i) =>
        {
            var plate = $"{(1000 + (number * VehiclesPerBase + i) * 613) % 9000 + 1000:0000} {"LMK"[number]}{consonants[(i * 3) % 20]}{consonants[(i * 7 + number) % 20]}";
            return new DemoVehicle(plate, f.Brand, f.Model, f.Seats, stops[i % stops.Length].Key);
        }).ToList();

        return new DemoBase(key, name, isDefault, stops, drivers, vehicles, lines, hub, airport, airportMinutes,
            excursionName, excursion, excursionCustomer, circuitName, circuit, circuitCustomer);
    }
}
