-- A login with password for the application, on a server that has no identities to sign in with. Run it as someone who may create
-- roles, after application-role.sql. It can be repeated, and then it only changes the password.
--   psql -v ON_ERROR_STOP=1 -d <database> -v ApplicationLogin=lof_app -v ApplicationPassword=... -f application-login.sql

-- psql does not replace its variables inside a DO block.
SELECT set_config('lof.application_login', :'ApplicationLogin', false),
       set_config('lof.application_password', :'ApplicationPassword', false) IS NOT NULL;

DO $$
DECLARE
    login text := current_setting('lof.application_login');
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = login) THEN
        EXECUTE format('CREATE ROLE %I LOGIN', login);
    END IF;

    EXECUTE format('ALTER ROLE %I WITH LOGIN PASSWORD %L', login, current_setting('lof.application_password'));
    EXECUTE format('GRANT lof_application TO %I', login);
END
$$;

RESET lof.application_password;
