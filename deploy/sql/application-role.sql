-- What the application may do in its database, for whoever is a member: read and write rows, and only
-- add in the records. Nothing else is needed: the advisory
-- locks are open to every user. Run it as the owner of the database, the one who applies the migrations, and after them. It can be
-- repeated.
--   psql -v ON_ERROR_STOP=1 -d <database> -f application-role.sql

DO $$
BEGIN
    IF to_regclass('public."__EFMigrationsHistory"') IS NULL THEN
        RAISE EXCEPTION 'Apply the migrations first';
    END IF;

    -- The roles are of the whole server, and another database of it may have created this one.
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'lof_application') THEN
        CREATE ROLE lof_application NOLOGIN;
    END IF;
END
$$;

GRANT USAGE ON SCHEMA public TO lof_application;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO lof_application;
-- The tables of the next migrations.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO lof_application;

-- A row deleted here would make the next deployment apply a migration twice.
REVOKE INSERT, UPDATE, DELETE ON public."__EFMigrationsHistory" FROM lof_application;

-- What is recorded of the visits is only added. What expires is deleted by a function with the rights of its owner (SPEC 0085).
REVOKE UPDATE, DELETE ON public."AuditEvents", public."Visits", public."VisitEvents", public."RequestTraces" FROM lof_application;
GRANT EXECUTE ON FUNCTION public.delete_expired_records() TO lof_application;
