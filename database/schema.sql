-- PO Governance — PostgreSQL schema v2 (identical to v1.1.4 storage.py SCHEMA).
-- The API runs these statements itself at start-up (CREATE ... IF NOT EXISTS), so this file is only
-- needed to create the tables by hand or to review them. Safe to run on an existing v2 database.

CREATE TABLE IF NOT EXISTS app_settings (
    name text PRIMARY KEY, value json NOT NULL, updated_at timestamptz NOT NULL DEFAULT now());          -- documents: manifest, params, scr_mapping

CREATE TABLE IF NOT EXISTS app_users (
    email text PRIMARY KEY, name text, roles text, active boolean, data json NOT NULL,                     -- "users" document, one row per login
    updated_at timestamptz NOT NULL DEFAULT now());

CREATE TABLE IF NOT EXISTS app_sessions (
    token text PRIMARY KEY, email text, data json NOT NULL, updated_at timestamptz NOT NULL DEFAULT now()); -- "sessions" document

CREATE TABLE IF NOT EXISTS doc_stamps (name text PRIMARY KEY, stamp bigint NOT NULL);                      -- bumped on every document write

CREATE TABLE IF NOT EXISTS upload_log (
    id bigserial PRIMARY KEY, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now());           -- "uploads" log

CREATE TABLE IF NOT EXISTS upload_batches (
    id bigserial PRIMARY KEY, upload_id text, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now()); -- "batches" log (Upload IDs)

CREATE TABLE IF NOT EXISTS run_index (
    id bigserial PRIMARY KEY, run_id text, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now());   -- "runs" log (Tool 2)

CREATE TABLE IF NOT EXISTS request_events (
    id bigserial PRIMARY KEY, req_date text NOT NULL, seq integer, event_type text, req_id text,           -- "requests" log, one day per req_date
    rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now());

CREATE INDEX IF NOT EXISTS request_events_day ON request_events (req_date, id);

CREATE TABLE IF NOT EXISTS audit_log (
    id bigserial PRIMARY KEY, at text NOT NULL, who text, action text, detail text);

CREATE TABLE IF NOT EXISTS dataset_versions (
    dkey text NOT NULL, version text NOT NULL, data bytea NOT NULL, bytes integer,                         -- every uploaded data-file version (gzip JSON)
    created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY (dkey, version));

CREATE TABLE IF NOT EXISTS run_files (
    run_id text PRIMARY KEY, data bytea NOT NULL, bytes integer, deleted boolean NOT NULL DEFAULT false,  -- saved Tool 2 runs (gzip JSON)
    created_at timestamptz NOT NULL DEFAULT now());

CREATE TABLE IF NOT EXISTS feedback_log (
    id bigserial PRIMARY KEY, fb_id text, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now()); -- "feedback" log

CREATE TABLE IF NOT EXISTS schema_info (key text PRIMARY KEY, value text);
INSERT INTO schema_info VALUES ('version', '2') ON CONFLICT (key) DO NOTHING;
UPDATE schema_info SET value = '2' WHERE key = 'version' AND value < '2';
