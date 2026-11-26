namespace DispatchGuard.Core.Localization;

/// <summary>Rule catalogue (SPEC 0054).</summary>
public static partial class Texts
{
    public static LocalizedText GroupDriving() => Of("Driving limits (CE 561/2006)", "Límites de conducción (CE 561/2006)");

    public static LocalizedText RuleContinuous() => Of("Driving period", "Conducción continua");

    public static LocalizedText RuleContinuousWhy(LocalizedText maximum) =>
        Of("After a driving period of {0}, a break is compulsory before going on.", "Tras {0} al volante, pausa obligatoria antes de seguir.", [maximum]);

    public static LocalizedText RuleContinuousFigure(LocalizedText maximum, LocalizedText pause) =>
        Of("{0} + {1} break", "{0} + pausa de {1}", [maximum, pause]);

    public static LocalizedText RuleDailyWhy() =>
        Of("Driving time between the end of one daily rest period and the beginning of the next (Art. 4.k).",
            "Horas de conducción entre el fin de un descanso diario y el principio del siguiente (Art. 4.k).");

    public static LocalizedText RuleExtended() => Of("Extended day", "Día ampliado");

    public static LocalizedText RuleExtendedWhy(LocalizedText clocks) =>
        Of("The daily driving time, between rest periods, may be extended a few times a week. {0} Our own reading, not legal text: a working day across Monday counts in the week in which it starts.",
            "La conducción diaria, entre descansos, puede ampliarse unas pocas veces por semana. {0} Criterio propio, no texto legal: la jornada que cruza el lunes cuenta en la semana en que empieza.", [clocks]);

    public static LocalizedText RuleExtendedFigure(LocalizedText maximum, int times) =>
        Of("{0}, at most {1} times/week", "{0}, máx. {1} veces/semana", [maximum, times]);

    public static LocalizedText RuleWeeklyWhy(LocalizedText clocks) =>
        Of("Total driving time from 00:00 on Monday to 24:00 on Sunday (Art. 4.i). {0}", "Total de conducción de 00:00 del lunes a 24:00 del domingo (Art. 4.i). {0}", [clocks]);

    public static LocalizedText RuleTwoWeeksWhy(LocalizedText clocks) =>
        Of("Total driving time in two consecutive calendar weeks. {0}", "Total de conducción en dos semanas naturales consecutivas. {0}", [clocks]);

    public static LocalizedText BothClocks() =>
        Of("It is counted in Madrid time and in UTC, and both must be met: the Regulation does not set the clock.",
            "Se cuenta en hora de Madrid y en UTC, y deben cumplirse las dos: el Reglamento no fija la hora.");

    public static LocalizedText GroupRest() => Of("Rest periods (CE 561/2006)", "Descansos (CE 561/2006)");

    public static LocalizedText RuleDailyRest() => Of("Daily rest period", "Descanso diario");

    public static LocalizedText RuleDailyRestWhy(LocalizedText window) =>
        Of("It must be taken within the {0} after the end of the previous rest period.", "Debe completarse dentro de las {0} siguientes al fin del descanso anterior.", [window]);

    public static LocalizedText RuleDailyRestFigure(LocalizedText rest, LocalizedText window) => Of("{0} in {1}", "{0} en {1}", [rest, window]);

    public static LocalizedText RuleReducedRest() => Of("Reduced daily rest period", "Descanso diario reducido");

    public static LocalizedText RuleReducedRestWhy() =>
        Of("Shorter daily rest period, limited between any two weekly rest periods.", "Descanso diario más corto, limitado entre dos descansos semanales.");

    public static LocalizedText RuleReducedRestFigure(LocalizedText rest, int times) => Of("{0}, at most {1} times", "{0}, máx. {1} veces", [rest, times]);

    public static LocalizedText RuleSplitRest() => Of("Daily rest period in two parts", "Descanso diario fraccionado");

    public static LocalizedText RuleSplitRestWhy() =>
        Of("The regular daily rest period may be taken in two periods.", "El descanso normal puede tomarse en dos partes.");

    public static LocalizedText RuleSplitRestFigure(LocalizedText first, LocalizedText second) => Of("{0} + {1}", "{0} + {1}", [first, second]);

    public static LocalizedText RuleWeeklyRest() => Of("Weekly rest period", "Descanso semanal");

    public static LocalizedText RuleWeeklyRestWhy() =>
        Of("Regular or reduced weekly rest period, which starts no later than at the end of six 24-hour periods.",
            "Descanso semanal normal o reducido, que empieza como tarde tras seis periodos de 24 h.");

    public static LocalizedText RuleWeeklyRestFigure(LocalizedText regular, LocalizedText reduced, double days) =>
        Of("{0} ({1} reduced) every {2:0}×24 h", "{0} (reducido {1}) cada {2:0}×24 h", [regular, reduced, days]);

    public static LocalizedText RuleUnverifiable() => Of("Weekly rest period not verifiable", "Descanso semanal no verificable");

    public static LocalizedText RuleUnverifiableWhy() =>
        Of("With less history than the weekly interval it cannot be checked: it warns, it does not block.",
            "Con menos historial que el intervalo semanal no se puede comprobar: se avisa, no se bloquea.");

    public static LocalizedText RuleUnverifiableFigure(double days) => Of("history < {0:0}×24 h", "historial < {0:0}×24 h", [days]);

    public static LocalizedText GroupLater() => Of("Later services", "Servicios posteriores");

    public static LocalizedText RuleLaterReference() => Of("CE 561/2006 Arts. 6 to 8 · SPEC 0063", "CE 561/2006 Arts. 6 a 8 · SPEC 0063");

    public static LocalizedText RuleLaterWhy() =>
        Of("A new service cannot make a later one of the same driver stop complying, or break the rules further. Each one is validated again with the same rules.",
            "Un servicio nuevo no puede hacer que otro posterior del mismo conductor deje de cumplir, ni que incumpla más. Se revalida cada uno con las mismas reglas.");

    public static LocalizedText RuleLaterFigure() => Of("Two calendar weeks", "Dos semanas naturales");

    public static LocalizedText GroupReturn() => Of("Return to base (UE 2020/1054)", "Retorno a base (UE 2020/1054)");

    public static LocalizedText RuleReturn() => Of("Return to the operational centre", "Regreso a la base");

    public static LocalizedText RuleReturnReference() => Of("CE 561/2006 Art. 8(8a) (UE 2020/1054)", "CE 561/2006 Art. 8.8 bis (UE 2020/1054)");

    public static LocalizedText RuleReturnWhy() =>
        Of("The driver must return to the operational centre periodically. It is checked, but today it takes no part in the allocation.",
            "El conductor debe volver a su base periódicamente. Se comprueba, pero hoy no interviene en la asignación.");

    public static LocalizedText EveryWeeks(double weeks, double days) => Of("every {0:0} weeks ({1:0} days)", "cada {0:0} semanas ({1:0} días)", [weeks, days]);

    public static LocalizedText EveryDays(double days) => Of("every {0:0} days", "cada {0:0} días", [days]);

    public static LocalizedText GroupOverlap() => Of("Overlap and availability", "Solape y disponibilidad");

    public static LocalizedText EngineRule() => Of("Rule of the engine", "Regla del motor");

    public static LocalizedText EngineRuleOf(string spec) => Of("Rule of the engine · {0}", "Regla del motor · {0}", [spec]);

    public static LocalizedText RuleDriverOverlap() => Of("Driver without overlap", "Conductor sin solape");

    public static LocalizedText RuleDriverOverlapWhy() =>
        Of("A driver cannot have two non-cancelled services that overlap.", "Un conductor no puede tener dos servicios no cancelados que se pisen.");

    public static LocalizedText NoMargin() => Of("0 min of margin", "0 min de margen");

    public static LocalizedText RuleVehicleOverlap() => Of("Vehicle without overlap", "Vehículo sin solape");

    public static LocalizedText RuleVehicleOverlapWhy() =>
        Of("A vehicle cannot have two non-cancelled services that overlap.", "Un vehículo no puede tener dos servicios no cancelados que se pisen.");

    public static LocalizedText RuleDriverActive() => Of("Driver active", "Conductor activo");

    public static LocalizedText RuleDriverActiveWhy() => Of("Only active drivers are allocated. It cannot be forced.", "Solo se asignan conductores activos. No se puede forzar.");

    public static LocalizedText Active() => Of("active", "activo");

    public static LocalizedText RuleDriverAvailableWhy() =>
        Of("No sick leave, annual leave or other unavailability at the time of the service. It cannot be forced.",
            "Sin baja, vacaciones u otra indisponibilidad en el horario del servicio. No se puede forzar.");

    public static LocalizedText NoUnavailability() => Of("no unavailability", "ninguna indisponibilidad");

    public static LocalizedText GroupVehicle() => Of("Vehicle", "Vehículo");

    public static LocalizedText RuleVehicleActive() => Of("Vehicle active", "Vehículo activo");

    public static LocalizedText RuleVehicleActiveWhy() => Of("Only vehicles in the fleet are allocated.", "Solo se asignan vehículos dados de alta.");

    public static LocalizedText RuleSeats() => Of("Enough seats", "Plazas suficientes");

    public static LocalizedText RuleSeatsWhy() =>
        Of("The vehicle must have at least the seats the service asks for.", "El vehículo debe tener al menos las plazas que pide el servicio.");

    public static LocalizedText RuleSeatsFigure() => Of("seats ≥ requested", "plazas ≥ pedidas");

    public static LocalizedText RuleVehicleAvailable() => Of("Vehicle available", "Vehículo disponible");

    public static LocalizedText RuleVehicleAvailableWhy() =>
        Of("No workshop or other unavailability at the time of the service.", "Sin taller ni otra indisponibilidad en el horario del servicio.");

    public static LocalizedText GroupRanking() => Of("Ranking criteria (they do not block)", "Criterios de ranking (no bloquean)");

    public static LocalizedText RuleSameBookingWhy() =>
        Of("First, whoever already drives another trip of the same booking.", "Primero, quien ya conduce otro viaje de la misma reserva.");

    public static LocalizedText Criterion1() => Of("criterion 1", "1.º criterio");

    public static LocalizedText Criterion2() => Of("criterion 2", "2.º criterio");

    public static LocalizedText RuleBaseMatch() => Of("Base at departure and arrival", "Base en salida y llegada");

    public static LocalizedText RuleBaseMatchWhy() =>
        Of("Then, drivers whose base is the departure and the arrival; after them, only the departure.",
            "Después, conductores cuya base es la salida y la llegada; luego solo la salida.");

    public static LocalizedText RuleProximity() => Of("Proximity of the base", "Proximidad de la base");

    public static LocalizedText RuleProximityWhy(double radius) =>
        Of("Straight-line distance from the base to the departure, by radius. Outside {0:0.#} km or without a base, last (not excluded).",
            "Distancia en línea recta de la base a la salida, por radios. Fuera de {0:0.#} km o sin base, al final (no se excluyen).", [radius]);

    public static LocalizedText Radii(IReadOnlyList<double> km) => Of("{0: / } km", "{0: / } km", [km]);

    public static LocalizedText RuleTieBreak() => Of("Tie-break by weekly rest period", "Desempate por descanso semanal");

    public static LocalizedText RuleTieBreakWhy() =>
        Of("All else being equal, whoever has fewer hours since the last weekly rest period.", "A igualdad, gana quien lleva menos horas desde su último descanso semanal.");

    public static LocalizedText FewerHours() => Of("fewer hours", "menos horas");

    public static LocalizedText RuleSuggestions() => Of("Suggestions per service", "Sugerencias por servicio");

    public static LocalizedText Configuration() => Of("Configuration", "Configuración");

    public static LocalizedText RuleSuggestionsWhy() =>
        Of("Number of drivers suggested by default.", "Número de conductores que se sugieren por defecto.");

    public static LocalizedText Number(int number) => Of("{0}", "{0}", [number]);

    public static LocalizedText HoursAndMinutes(int hours, int minutes) => Of("{0} h {1} min", "{0} h {1} min", [hours, minutes]);

    public static LocalizedText WholeHours(int hours) => Of("{0} h", "{0} h", [hours]);
}
