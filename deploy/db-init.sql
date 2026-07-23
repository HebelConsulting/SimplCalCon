-- SimplCalCon PostgreSQL bootstrap (ADR 0024).
--
-- Runs once, as the cluster superuser, when the postgres container initialises an empty
-- data directory (mounted into /docker-entrypoint-initdb.d/ by docker-compose.yaml). It
-- is idempotent, so it is also safe to run by hand against an existing cluster:
--   psql -U postgres -f deploy/db-init.sql
--
-- Roles:
--   * simplcalcon_app  — NOLOGIN group role; owns the application database and holds its
--                        privileges. Grant future app roles into this group.
--   * simplcalcon      — LOGIN user the application connects as; a member of the group,
--                        so it inherits the group's privileges (INHERIT is the default).
--
-- The dev password below is for the local demo stack only. In production, provision the
-- login role's password out of band (managed DB / secret) and do not rely on this file.

-- NOLOGIN group role: privilege holder / database owner.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'simplcalcon_app') THEN
        CREATE ROLE simplcalcon_app NOLOGIN;
    END IF;
END
$$;

-- LOGIN user for the application, member of the group.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'simplcalcon') THEN
        CREATE ROLE simplcalcon LOGIN PASSWORD 'simplcalcon';
    END IF;
END
$$;

GRANT simplcalcon_app TO simplcalcon;

-- Application database, owned by the group role (CREATE DATABASE can't run in a DO block).
SELECT 'CREATE DATABASE simplcalcon OWNER simplcalcon_app'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'simplcalcon')\gexec

GRANT ALL PRIVILEGES ON DATABASE simplcalcon TO simplcalcon_app;

-- Schema-level rights inside the application database. On PostgreSQL 15+ the public schema
-- no longer grants CREATE to everyone, so the group needs it explicitly; default privileges
-- keep future EF-migrated tables/sequences usable by the whole group.
\connect simplcalcon
GRANT ALL ON SCHEMA public TO simplcalcon_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO simplcalcon_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO simplcalcon_app;
