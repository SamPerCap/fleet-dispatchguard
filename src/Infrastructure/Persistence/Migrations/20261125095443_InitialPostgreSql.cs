using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgreSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:CollationDefinition:spanish_ci_as", "es-u-ks-level2,es-u-ks-level2,icu,False");

            migrationBuilder.CreateTable(
                name: "AllocationAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "spanish_ci_as"),
                    TripId = table.Column<int>(type: "integer", nullable: false),
                    DriverId = table.Column<int>(type: "integer", nullable: false),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    BaseId = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, collation: "spanish_ci_as"),
                    RuleIds = table.Column<string[]>(type: "text[]", nullable: false),
                    ForcedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, collation: "spanish_ci_as"),
                    Explanation = table.Column<string>(type: "text", nullable: true, collation: "spanish_ci_as"),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Manual"),
                    SessionId = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: true, collation: "spanish_ci_as"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "spanish_ci_as"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bases", x => x.Id);
                });

            // The default base (OperatingBase.DefaultId); the next one gets the number after it.
            migrationBuilder.Sql("""
                INSERT INTO "Bases" ("Id", "Name", "IsActive", "CreatedAt") VALUES (1, 'Base principal', TRUE, now());
                SELECT setval(pg_get_serial_sequence('"Bases"', 'Id'), 1);
                """);

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "spanish_ci_as"),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, collation: "spanish_ci_as"),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, collation: "spanish_ci_as"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Circuits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "spanish_ci_as"),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, collation: "spanish_ci_as"),
                    DurationStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreferSameDriver = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    PreferSameVehicle = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Circuits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "spanish_ci_as"),
                    TaxId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, collation: "spanish_ci_as"),
                    ContactEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, collation: "spanish_ci_as"),
                    ContactPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, collation: "spanish_ci_as"),
                    IsExternal = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true, collation: "spanish_ci_as"),
                    Xml = table.Column<string>(type: "text", nullable: true, collation: "spanish_ci_as")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "spanish_ci_as"),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, collation: "spanish_ci_as"),
                    Latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "spanish_ci_as"),
                    Province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "spanish_ci_as"),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false, defaultValue: "ES", collation: "spanish_ci_as"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CachedRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OriginLocationId = table.Column<int>(type: "integer", nullable: false),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: false),
                    IntermediateLocationIdsJson = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, defaultValue: "[]", collation: "spanish_ci_as"),
                    DistanceKm = table.Column<double>(type: "double precision", precision: 10, scale: 2, nullable: false),
                    EstimatedDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    MetricsCalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawServiceResponse = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true, collation: "spanish_ci_as"),
                    ServiceProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "GoogleMaps", collation: "spanish_ci_as"),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CachedRoutes_Locations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CachedRoutes_Locations_OriginLocationId",
                        column: x => x.OriginLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Drivers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdentificationNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, collation: "spanish_ci_as"),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "spanish_ci_as"),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "spanish_ci_as"),
                    IsExternal = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AllowsAutoAssignment = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    HireDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DrivingToday = table.Column<TimeSpan>(type: "interval", nullable: true),
                    DrivingThisWeek = table.Column<TimeSpan>(type: "interval", nullable: true),
                    DrivingLastTwoWeeks = table.Column<TimeSpan>(type: "interval", nullable: true),
                    ExtendedDaysUsedThisWeek = table.Column<int>(type: "integer", nullable: true),
                    ReducedDailyRestsUsed = table.Column<int>(type: "integer", nullable: true),
                    LastDailyRestDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastDailyRestType = table.Column<string>(type: "text", nullable: true),
                    LastWeeklyRestDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastWeeklyRestType = table.Column<string>(type: "text", nullable: true),
                    LimitsCalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeclaredLastWeeklyRestEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BaseId = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    HomeLocationId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Drivers_Bases_BaseId",
                        column: x => x.BaseId,
                        principalTable: "Bases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Drivers_Locations_HomeLocationId",
                        column: x => x.HomeLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LicensePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, collation: "spanish_ci_as"),
                    Brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "spanish_ci_as"),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "spanish_ci_as"),
                    SeatingCapacity = table.Column<int>(type: "integer", nullable: false),
                    OdometerKm = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RegistrationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsExternal = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AllowsAutoAssignment = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    DecommissionedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BaseId = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    HomeLocationId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vehicles_Bases_BaseId",
                        column: x => x.BaseId,
                        principalTable: "Bases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vehicles_Locations_HomeLocationId",
                        column: x => x.HomeLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DriverUnavailabilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DriverId = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, collation: "spanish_ci_as"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverUnavailabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriverUnavailabilities_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OriginLocationId = table.Column<int>(type: "integer", nullable: false),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: false),
                    ScheduledWindowStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduledWindowEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequiredSeats = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, collation: "spanish_ci_as"),
                    AssignedDriverId = table.Column<int>(type: "integer", nullable: true),
                    AssignedVehicleId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    BookingId = table.Column<int>(type: "integer", nullable: false),
                    CircuitId = table.Column<int>(type: "integer", nullable: true),
                    ServiceType = table.Column<string>(type: "text", nullable: false),
                    CalculatedDistanceInKm = table.Column<double>(type: "double precision", precision: 10, scale: 2, nullable: true),
                    CalculatedEstimatedDurationInMinutes = table.Column<TimeSpan>(type: "interval", nullable: true),
                    CalculatedMetricsCalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LegalValidationIsCompliant = table.Column<bool>(type: "boolean", nullable: true),
                    LegalValidationViolations = table.Column<string[]>(type: "text[]", nullable: true),
                    LegalValidationWarnings = table.Column<string[]>(type: "text[]", nullable: true),
                    LegalValidationValidatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastOptimizationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ForcedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, collation: "spanish_ci_as"),
                    ForcedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "spanish_ci_as"),
                    BaseId = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    ClientId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ForcedByTrips = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trips_Bases_BaseId",
                        column: x => x.BaseId,
                        principalTable: "Bases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trips_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trips_Circuits_CircuitId",
                        column: x => x.CircuitId,
                        principalTable: "Circuits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Trips_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trips_Drivers_AssignedDriverId",
                        column: x => x.AssignedDriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Trips_Locations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trips_Locations_OriginLocationId",
                        column: x => x.OriginLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trips_Vehicles_AssignedVehicleId",
                        column: x => x.AssignedVehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "VehicleUnavailabilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, collation: "spanish_ci_as"),
                    ServiceProvider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: "spanish_ci_as"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleUnavailabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleUnavailabilities_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripStops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TripId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    StopWindowStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StopWindowEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SequenceOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripStops_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TripStops_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationAttempts_BaseId_AttemptedAt",
                table: "AllocationAttempts",
                columns: new[] { "BaseId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationAttempts_IsActive",
                table: "AllocationAttempts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Bases_IsActive",
                table: "Bases",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Bases_Name",
                table: "Bases",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CustomerName",
                table: "Bookings",
                column: "CustomerName");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_IsActive",
                table: "Bookings",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CachedRoutes_DestinationLocationId",
                table: "CachedRoutes",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CachedRoutes_IsActive",
                table: "CachedRoutes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CachedRoutes_OriginLocationId_DestinationLocationId",
                table: "CachedRoutes",
                columns: new[] { "OriginLocationId", "DestinationLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CachedRoutes_ServiceProvider",
                table: "CachedRoutes",
                column: "ServiceProvider");

            migrationBuilder.CreateIndex(
                name: "IX_Circuits_IsActive",
                table: "Circuits",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Circuits_Name",
                table: "Circuits",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Circuits_PreferSameDriver",
                table: "Circuits",
                column: "PreferSameDriver");

            migrationBuilder.CreateIndex(
                name: "IX_Circuits_PreferSameVehicle",
                table: "Circuits",
                column: "PreferSameVehicle");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_IsActive",
                table: "Clients",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_IsExternal",
                table: "Clients",
                column: "IsExternal");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Name",
                table: "Clients",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_TaxId",
                table: "Clients",
                column: "TaxId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_AllowsAutoAssignment",
                table: "Drivers",
                column: "AllowsAutoAssignment");

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_BaseId",
                table: "Drivers",
                column: "BaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_HomeLocationId",
                table: "Drivers",
                column: "HomeLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_IdentificationNumber",
                table: "Drivers",
                column: "IdentificationNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_IsActive",
                table: "Drivers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_IsExternal",
                table: "Drivers",
                column: "IsExternal");

            migrationBuilder.CreateIndex(
                name: "IX_DriverUnavailabilities_DriverId",
                table: "DriverUnavailabilities",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverUnavailabilities_Type",
                table: "DriverUnavailabilities",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_City",
                table: "Locations",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_CountryCode",
                table: "Locations",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_IsActive",
                table: "Locations",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Province",
                table: "Locations",
                column: "Province");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_AssignedDriverId",
                table: "Trips",
                column: "AssignedDriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_AssignedVehicleId",
                table: "Trips",
                column: "AssignedVehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_BaseId",
                table: "Trips",
                column: "BaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_BookingId",
                table: "Trips",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_CircuitId",
                table: "Trips",
                column: "CircuitId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_ClientId",
                table: "Trips",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DestinationLocationId",
                table: "Trips",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_IsActive",
                table: "Trips",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_IsLocked",
                table: "Trips",
                column: "IsLocked");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_OriginLocationId_DestinationLocationId",
                table: "Trips",
                columns: new[] { "OriginLocationId", "DestinationLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_Status",
                table: "Trips",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_LocationId",
                table: "TripStops",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_SequenceOrder",
                table: "TripStops",
                column: "SequenceOrder");

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_TripId",
                table: "TripStops",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_TripId_SequenceOrder",
                table: "TripStops",
                columns: new[] { "TripId", "SequenceOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_AllowsAutoAssignment",
                table: "Vehicles",
                column: "AllowsAutoAssignment");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_BaseId",
                table: "Vehicles",
                column: "BaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_Brand",
                table: "Vehicles",
                column: "Brand");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_DecommissionedDate",
                table: "Vehicles",
                column: "DecommissionedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_HomeLocationId",
                table: "Vehicles",
                column: "HomeLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_IsActive",
                table: "Vehicles",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_IsExternal",
                table: "Vehicles",
                column: "IsExternal");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_LicensePlate",
                table: "Vehicles",
                column: "LicensePlate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleUnavailabilities_Type",
                table: "VehicleUnavailabilities",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleUnavailabilities_VehicleId",
                table: "VehicleUnavailabilities",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllocationAttempts");

            migrationBuilder.DropTable(
                name: "CachedRoutes");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "DriverUnavailabilities");

            migrationBuilder.DropTable(
                name: "TripStops");

            migrationBuilder.DropTable(
                name: "VehicleUnavailabilities");

            migrationBuilder.DropTable(
                name: "Trips");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "Circuits");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "Drivers");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropTable(
                name: "Bases");

            migrationBuilder.DropTable(
                name: "Locations");
        }
    }
}
