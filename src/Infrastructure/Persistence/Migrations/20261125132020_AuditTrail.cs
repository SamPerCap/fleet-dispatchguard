using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, collation: "spanish_ci_as"),
                    TraceId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, collation: "spanish_ci_as"),
                    DeclaredName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, collation: "spanish_ci_as"),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, collation: "spanish_ci_as"),
                    Entity = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false, collation: "spanish_ci_as"),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    Changes = table.Column<string>(type: "text", nullable: false, collation: "spanish_ci_as")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestTraces",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "spanish_ci_as"),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "spanish_ci_as"),
                    Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, collation: "spanish_ci_as"),
                    Path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "spanish_ci_as"),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Queries = table.Column<int>(type: "integer", nullable: false),
                    QueriesMs = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false, collation: "spanish_ci_as")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestTraces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VisitEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "spanish_ci_as"),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, collation: "spanish_ci_as"),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "spanish_ci_as"),
                    Status = table.Column<int>(type: "integer", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Visits",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "spanish_ci_as"),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, collation: "spanish_ci_as")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visits", x => x.SessionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_SessionId",
                table: "AuditEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestTraces_SessionId",
                table: "RequestTraces",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitEvents_SessionId",
                table: "VisitEvents",
                column: "SessionId");

            migrationBuilder.Sql(DeleteExpired);
            migrationBuilder.Sql(OnlyAdd);
        }

        /// <summary>The time is inside and the rights are the owner's: the application can ask for it and decides nothing (SPEC 0085 R8).</summary>
        private const string DeleteExpired = """
            CREATE FUNCTION public.delete_expired_records() RETURNS void
            LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, pg_temp
            AS $$
                DELETE FROM public."AuditEvents" WHERE "At" < now() - interval '30 days';
                DELETE FROM public."VisitEvents" WHERE "At" < now() - interval '30 days';
                DELETE FROM public."RequestTraces" WHERE "At" < now() - interval '30 days';
                DELETE FROM public."Visits" WHERE "StartedAt" < now() - interval '30 days';
            $$;
            REVOKE ALL ON FUNCTION public.delete_expired_records() FROM PUBLIC;
            """;

        /// <summary>
        /// Every new table is born with all the permissions (application-role.sql), and nobody runs that script again after a
        /// migration. Where the role does not exist yet, the script says the same.
        /// </summary>
        private const string OnlyAdd = """
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'lof_application') THEN
                    REVOKE ALL ON public."AuditEvents", public."Visits", public."VisitEvents", public."RequestTraces" FROM lof_application;
                    GRANT SELECT, INSERT ON public."AuditEvents", public."Visits", public."VisitEvents", public."RequestTraces" TO lof_application;
                    GRANT EXECUTE ON FUNCTION public.delete_expired_records() TO lof_application;
                END IF;
            END
            $$;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION public.delete_expired_records();");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "RequestTraces");

            migrationBuilder.DropTable(
                name: "VisitEvents");

            migrationBuilder.DropTable(
                name: "Visits");
        }
    }
}
