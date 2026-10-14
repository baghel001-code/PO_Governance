"""
Storage for the VMM PO Governance server.

Two interchangeable back ends with the same methods:

  FileStore  - the original layout in the data folder (json / jsonl / json.gz files)
  PgStore    - PostgreSQL (pure-Python pg8000 driver, bundled in ./pylib)

config.json  "storage": "postgres"  or  "files"  picks one.  copy_store() moves
everything from one to the other (used for the one-time migration, for the
nightly backup - which is written in the file layout - and for a rollback).

What is stored
  documents  manifest, params, users, sessions, scr_mapping
  logs       uploads, batches, runs (run index), requests (one per day)
  audit      one line per change
  blobs      ds   = every uploaded file version (gzip JSON)
             run  = every saved Tool 2 run (gzip JSON)
"""
import gzip
import json
import os
import threading
import time
import zipfile
from datetime import datetime

DOCS = ("manifest", "params", "users", "sessions", "scr_mapping")
LOGS = ("uploads", "batches", "runs", "requests", "feedback")


def _dumps(obj):
    return json.dumps(obj, ensure_ascii=False)


# ════════════════════════════════════════════════════════════════════
#  Files (original layout)
# ════════════════════════════════════════════════════════════════════
class FileStore:
    kind = "files"

    def __init__(self, data_dir):
        self.dir = data_dir
        self.ds_dir = os.path.join(data_dir, "datasets")
        self.req_dir = os.path.join(data_dir, "requests")
        self.run_dir = os.path.join(data_dir, "runs")
        for d in (self.dir, self.ds_dir, self.req_dir, self.run_dir):
            os.makedirs(d, exist_ok=True)

    def describe(self):
        return "files in %s" % self.dir

    # ── documents
    def _doc_path(self, name):
        return os.path.join(self.dir, name + ".json")

    def get_doc(self, name, default=None):
        try:
            with open(self._doc_path(name), "r", encoding="utf-8") as f:
                return json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            return default

    def put_doc(self, name, obj):
        path = self._doc_path(name)
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(obj, f, ensure_ascii=False, indent=1)
        os.replace(tmp, path)

    def doc_stamp(self, name):
        try:
            return os.path.getmtime(self._doc_path(name))
        except OSError:
            return 0

    # ── logs
    def _log_path(self, stream, day=None):
        if stream == "uploads":
            return os.path.join(self.ds_dir, "uploads.jsonl")
        if stream == "batches":
            return os.path.join(self.ds_dir, "batches.jsonl")
        if stream == "runs":
            return os.path.join(self.run_dir, "index.jsonl")
        if stream == "requests":
            return os.path.join(self.req_dir, day + ".jsonl")
        if stream == "feedback":
            return os.path.join(self.dir, "feedback.jsonl")
        raise ValueError(stream)

    @staticmethod
    def _read_jsonl(path):
        out = []
        if os.path.exists(path):
            with open(path, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if line:
                        try:
                            out.append(json.loads(line))
                        except json.JSONDecodeError:
                            pass
        return out

    def read_log(self, stream, day=None):
        return self._read_jsonl(self._log_path(stream, day))

    def read_logs(self, stream, days):
        return {d: self.read_log(stream, d) for d in days}

    def append_log(self, stream, obj, day=None):
        with open(self._log_path(stream, day), "a", encoding="utf-8") as f:
            f.write(_dumps(obj) + "\n")

    def rewrite_log(self, stream, objs, day=None):
        path = self._log_path(stream, day)
        with open(path + ".tmp", "w", encoding="utf-8") as f:
            for o in objs:
                f.write(_dumps(o) + "\n")
        os.replace(path + ".tmp", path)

    def log_days(self, stream="requests"):
        return sorted(n[:-len(".jsonl")] for n in os.listdir(self.req_dir) if n.endswith(".jsonl"))

    def delete_log_day(self, stream, day):
        p = self._log_path(stream, day)
        if os.path.exists(p):
            os.remove(p)

    # ── audit
    def audit_append(self, at, who, action, detail):
        with open(os.path.join(self.dir, "audit.log"), "a", encoding="utf-8") as f:
            f.write("%s\t%s\t%s\t%s\n" % (at, who, action, detail))

    def audit_read(self):
        out = []
        path = os.path.join(self.dir, "audit.log")
        if os.path.exists(path):
            with open(path, "r", encoding="utf-8") as f:
                for line in f:
                    p = line.rstrip("\n").split("\t", 3)
                    if len(p) >= 3:
                        out.append((p[0], p[1], p[2], p[3] if len(p) > 3 else ""))
        return out

    # ── blobs
    def _blob_path(self, kind, key, ver, deleted=False):
        if kind == "ds":
            p = os.path.join(self.ds_dir, key, ver + ".json.gz")
        elif kind == "run":
            p = os.path.join(self.run_dir, ver + ".json.gz")
        else:
            raise ValueError(kind)
        return p + ".deleted" if deleted else p

    def put_blob(self, kind, key, ver, data, deleted=False):
        path = self._blob_path(kind, key, ver, deleted)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path + ".tmp", "wb") as f:
            f.write(data)
        os.replace(path + ".tmp", path)

    def get_blob(self, kind, key, ver):
        try:
            with open(self._blob_path(kind, key, ver), "rb") as f:
                return f.read()
        except FileNotFoundError:
            return None

    def blob_exists(self, kind, key, ver):
        return os.path.exists(self._blob_path(kind, key, ver))

    def blob_size(self, kind, key, ver):
        try:
            return os.path.getsize(self._blob_path(kind, key, ver))
        except OSError:
            return None

    def mark_blob_deleted(self, kind, key, ver):
        p = self._blob_path(kind, key, ver)
        if os.path.exists(p):
            os.replace(p, p + ".deleted")  # recoverable by an admin

    def delete_blob(self, kind, key, ver):
        p = self._blob_path(kind, key, ver)
        if os.path.exists(p):
            os.remove(p)

    def list_blobs(self, kind):
        """[(key, ver, deleted, mtime)]"""
        out = []
        if kind == "ds":
            for key in sorted(os.listdir(self.ds_dir)):
                kd = os.path.join(self.ds_dir, key)
                if not os.path.isdir(kd):
                    continue
                for f in sorted(os.listdir(kd)):
                    if f.endswith(".json.gz"):
                        out.append((key, f[:-len(".json.gz")], False, os.path.getmtime(os.path.join(kd, f))))
        else:
            for f in sorted(os.listdir(self.run_dir)):
                p = os.path.join(self.run_dir, f)
                if f.endswith(".json.gz"):
                    out.append(("", f[:-len(".json.gz")], False, os.path.getmtime(p)))
                elif f.endswith(".json.gz.deleted"):
                    out.append(("", f[:-len(".json.gz.deleted")], True, os.path.getmtime(p)))
        return out

    def get_blob_any(self, kind, key, ver, deleted):
        try:
            with open(self._blob_path(kind, key, ver, deleted), "rb") as f:
                return f.read()
        except FileNotFoundError:
            return None

    # ── misc
    def storage_bytes(self):
        total = 0
        for root, _, files in os.walk(self.dir):
            for f in files:
                try:
                    total += os.path.getsize(os.path.join(root, f))
                except OSError:
                    pass
        return total

    def is_empty(self):
        return not any(os.scandir(self.dir)) or (self.get_doc("manifest") is None and self.get_doc("users") is None)

    def close(self):
        pass


# ════════════════════════════════════════════════════════════════════
#  PostgreSQL
# ════════════════════════════════════════════════════════════════════
SCHEMA = [
    """CREATE TABLE IF NOT EXISTS app_settings (
         name text PRIMARY KEY, value json NOT NULL, updated_at timestamptz NOT NULL DEFAULT now())""",
    """CREATE TABLE IF NOT EXISTS app_users (
         email text PRIMARY KEY, name text, roles text, active boolean, data json NOT NULL,
         updated_at timestamptz NOT NULL DEFAULT now())""",
    """CREATE TABLE IF NOT EXISTS app_sessions (
         token text PRIMARY KEY, email text, data json NOT NULL, updated_at timestamptz NOT NULL DEFAULT now())""",
    """CREATE TABLE IF NOT EXISTS doc_stamps (name text PRIMARY KEY, stamp bigint NOT NULL)""",
    """CREATE TABLE IF NOT EXISTS upload_log (
         id bigserial PRIMARY KEY, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now())""",
    """CREATE TABLE IF NOT EXISTS upload_batches (
         id bigserial PRIMARY KEY, upload_id text, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now())""",
    """CREATE TABLE IF NOT EXISTS run_index (
         id bigserial PRIMARY KEY, run_id text, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now())""",
    """CREATE TABLE IF NOT EXISTS request_events (
         id bigserial PRIMARY KEY, req_date text NOT NULL, seq integer, event_type text, req_id text,
         rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now())""",
    """CREATE INDEX IF NOT EXISTS request_events_day ON request_events (req_date, id)""",
    """CREATE TABLE IF NOT EXISTS audit_log (
         id bigserial PRIMARY KEY, at text NOT NULL, who text, action text, detail text)""",
    """CREATE TABLE IF NOT EXISTS dataset_versions (
         dkey text NOT NULL, version text NOT NULL, data bytea NOT NULL, bytes integer,
         created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY (dkey, version))""",
    """CREATE TABLE IF NOT EXISTS run_files (
         run_id text PRIMARY KEY, data bytea NOT NULL, bytes integer, deleted boolean NOT NULL DEFAULT false,
         created_at timestamptz NOT NULL DEFAULT now())""",
    """CREATE TABLE IF NOT EXISTS feedback_log (
         id bigserial PRIMARY KEY, fb_id text, rec json NOT NULL, created_at timestamptz NOT NULL DEFAULT now())""",
    """CREATE TABLE IF NOT EXISTS schema_info (key text PRIMARY KEY, value text)""",
    """INSERT INTO schema_info VALUES ('version', '2') ON CONFLICT (key) DO NOTHING""",
    """UPDATE schema_info SET value = '2' WHERE key = 'version' AND value < '2'""",
]

_LOG_TABLE = {"uploads": "upload_log", "batches": "upload_batches", "runs": "run_index", "requests": "request_events",
              "feedback": "feedback_log"}
_MAP_TABLE = {"users": ("app_users", "email"), "sessions": ("app_sessions", "token")}


class PgStore:
    kind = "postgres"

    def __init__(self, db, wait_seconds=0, log=None):
        """db = {"host","port","name","user","password"}"""
        import pg8000.native  # bundled in ./pylib
        self._pg = pg8000.native
        self.db = db
        self.log = log
        self._pool = []            # idle connections
        self._pool_lock = threading.Condition()
        self._open = 0             # connections currently open
        self._max = int(db.get("pool_size") or 10)
        self._map_cache = {}  # name -> {key: json text} last written, to write only changed rows
        self._map_lock = threading.Lock()
        deadline = time.time() + wait_seconds
        while True:
            try:
                c = self._borrow()
                break
            except Exception as e:
                if time.time() >= deadline:
                    raise
                if self.log:
                    self.log.warning("Database not reachable yet (%s) - retrying in 5 s", e)
                time.sleep(5)
        try:
            for stmt in SCHEMA:
                c.run(stmt)
        finally:
            self._give_back(c)

    def describe(self):
        return "PostgreSQL %s@%s:%s/%s" % (self.db.get("user"), self.db.get("host"), self.db.get("port"), self.db.get("name"))

    # ── connections (small pool shared by all request threads)
    def _connect(self):
        return self._pg.Connection(
            self.db.get("user"), host=self.db.get("host", "127.0.0.1"), port=int(self.db.get("port", 5432)),
            database=self.db.get("name"), password=self.db.get("password"), timeout=60,
            application_name="vmm-po-governance")

    def _borrow(self):
        with self._pool_lock:
            while True:
                if self._pool:
                    return self._pool.pop()
                if self._open < self._max:
                    self._open += 1
                    break
                self._pool_lock.wait(30)
        try:
            return self._connect()
        except Exception:
            with self._pool_lock:
                self._open -= 1
                self._pool_lock.notify()
            raise

    def _give_back(self, c, broken=False):
        with self._pool_lock:
            if broken:
                self._open -= 1
                try:
                    c.close()
                except Exception:
                    pass
            else:
                self._pool.append(c)
            self._pool_lock.notify()

    def _conn(self):
        """Opens (and returns to the pool) one connection - used at start-up to test the login."""
        c = self._borrow()
        self._give_back(c)
        return c

    def _with_conn(self, work):
        """Runs work(conn); on a lost connection, retries once on a fresh one."""
        for attempt in (1, 2):
            c = self._borrow()
            try:
                result = work(c)
            except (self._pg.InterfaceError, OSError, ConnectionError) as e:
                self._give_back(c, broken=True)
                self._drain_idle()  # the others are most likely dead too (database restarted)
                if attempt == 2:
                    raise
                if self.log:
                    self.log.warning("Database connection lost (%s) - reconnecting", e)
                continue
            except Exception:
                # SQL error: make sure the connection is usable again
                try:
                    if getattr(c, "_in_tx", False):
                        c.run("ROLLBACK")
                except Exception:
                    self._give_back(c, broken=True)
                    raise
                self._give_back(c)
                raise
            self._give_back(c)
            return result

    def _drain_idle(self):
        with self._pool_lock:
            idle, self._pool = self._pool, []
            self._open -= len(idle)
            self._pool_lock.notify_all()
        for c in idle:
            try:
                c.close()
            except Exception:
                pass

    def run(self, sql, **params):
        return self._with_conn(lambda c: c.run(sql, **params))

    def tx(self, statements):
        """[(sql, params)] in one transaction."""
        def work(c):
            c.run("BEGIN")
            c._in_tx = True
            try:
                for sql, params in statements:
                    c.run(sql, **params)
                c.run("COMMIT")
            finally:
                c._in_tx = False
        def guarded(c):
            try:
                return work(c)
            except self._pg.DatabaseError:
                try:
                    c.run("ROLLBACK")
                except Exception:
                    pass
                raise
        return self._with_conn(guarded)

    def close(self):
        with self._pool_lock:
            for c in self._pool:
                try:
                    c.close()
                except Exception:
                    pass
            self._open -= len(self._pool)
            self._pool = []

    def _bump(self, name):
        return ("INSERT INTO doc_stamps (name, stamp) VALUES (:n, 1) "
                "ON CONFLICT (name) DO UPDATE SET stamp = doc_stamps.stamp + 1", {"n": name})

    # ── documents
    def get_doc(self, name, default=None):
        if name in _MAP_TABLE:
            table, kcol = _MAP_TABLE[name]
            rows = self.run("SELECT %s, data::text FROM %s ORDER BY %s" % (kcol, table, kcol))
            if not rows and not self.run("SELECT 1 FROM doc_stamps WHERE name = :n", n=name):
                return default
            with self._map_lock:
                self._map_cache[name] = {k: v for k, v in rows}
            return {k: json.loads(v) for k, v in rows}
        rows = self.run("SELECT value::text FROM app_settings WHERE name = :n", n=name)
        return json.loads(rows[0][0]) if rows else default

    def put_doc(self, name, obj):
        if name in _MAP_TABLE:
            return self._put_map(name, obj)
        self.tx([("INSERT INTO app_settings (name, value, updated_at) VALUES (:n, CAST(:v AS json), now()) "
                  "ON CONFLICT (name) DO UPDATE SET value = EXCLUDED.value, updated_at = now()",
                  {"n": name, "v": _dumps(obj)}), self._bump(name)])

    def _put_map(self, name, obj):
        table, kcol = _MAP_TABLE[name]
        new = {k: _dumps(v) for k, v in obj.items()}
        with self._map_lock:
            old = self._map_cache.get(name)
            if old is None:
                rows = self.run("SELECT %s, data::text FROM %s" % (kcol, table))
                old = {k: v for k, v in rows}
            stmts = []
            for k in old:
                if k not in new:
                    stmts.append(("DELETE FROM %s WHERE %s = :k" % (table, kcol), {"k": k}))
            for k, v in new.items():
                if old.get(k) == v:
                    continue
                rec = obj[k]
                if name == "users":
                    stmts.append(("INSERT INTO app_users (email, name, roles, active, data, updated_at) "
                                  "VALUES (:k, :nm, :r, :a, CAST(:v AS json), now()) ON CONFLICT (email) DO UPDATE SET "
                                  "name = EXCLUDED.name, roles = EXCLUDED.roles, active = EXCLUDED.active, "
                                  "data = EXCLUDED.data, updated_at = now()",
                                  {"k": k, "nm": rec.get("name"), "r": ",".join(rec.get("roles") or []),
                                   "a": bool(rec.get("active")), "v": v}))
                else:
                    stmts.append(("INSERT INTO app_sessions (token, email, data, updated_at) VALUES "
                                  "(:k, :e, CAST(:v AS json), now()) ON CONFLICT (token) DO UPDATE SET "
                                  "email = EXCLUDED.email, data = EXCLUDED.data, updated_at = now()",
                                  {"k": k, "e": rec.get("email"), "v": v}))
            stmts.append(self._bump(name))
            self.tx(stmts)
            self._map_cache[name] = new

    def doc_stamp(self, name):
        rows = self.run("SELECT stamp FROM doc_stamps WHERE name = :n", n=name)
        return rows[0][0] if rows else 0

    # ── logs
    def read_log(self, stream, day=None):
        table = _LOG_TABLE[stream]
        if stream == "requests":
            rows = self.run("SELECT rec::text FROM request_events WHERE req_date = :d ORDER BY id", d=day)
        else:
            rows = self.run("SELECT rec::text FROM %s ORDER BY id" % table)
        return [json.loads(r[0]) for r in rows]

    def read_logs(self, stream, days):
        out = {d: [] for d in days}
        if not days:
            return out
        rows = self.run("SELECT req_date, rec::text FROM request_events WHERE req_date = ANY(CAST(:ds AS text[])) "
                        "ORDER BY id", ds=list(days))
        for d, rec in rows:
            out.setdefault(d, []).append(json.loads(rec))
        return out

    def _insert_log(self, stream, obj, day):
        v = _dumps(obj)
        if stream == "requests":
            return ("INSERT INTO request_events (req_date, seq, event_type, req_id, rec) "
                    "VALUES (:d, :s, :t, :r, CAST(:v AS json))",
                    {"d": day, "s": obj.get("seq"), "t": obj.get("type"),
                     "r": obj.get("reqId") or (obj.get("request") or {}).get("reqId"), "v": v})
        if stream == "batches":
            return ("INSERT INTO upload_batches (upload_id, rec) VALUES (:u, CAST(:v AS json))",
                    {"u": obj.get("uploadId"), "v": v})
        if stream == "runs":
            return ("INSERT INTO run_index (run_id, rec) VALUES (:u, CAST(:v AS json))", {"u": obj.get("id"), "v": v})
        if stream == "feedback":
            return ("INSERT INTO feedback_log (fb_id, rec) VALUES (:u, CAST(:v AS json))", {"u": obj.get("id"), "v": v})
        return ("INSERT INTO upload_log (rec) VALUES (CAST(:v AS json))", {"v": v})

    def append_log(self, stream, obj, day=None):
        sql, p = self._insert_log(stream, obj, day)
        self.run(sql, **p)

    def rewrite_log(self, stream, objs, day=None):
        table = _LOG_TABLE[stream]
        if stream == "requests":
            stmts = [("DELETE FROM request_events WHERE req_date = :d", {"d": day})]
        else:
            stmts = [("DELETE FROM %s" % table, {})]
        stmts += [self._insert_log(stream, o, day) for o in objs]
        self.tx(stmts)

    def log_days(self, stream="requests"):
        return [r[0] for r in self.run("SELECT DISTINCT req_date FROM request_events ORDER BY req_date")]

    def delete_log_day(self, stream, day):
        self.run("DELETE FROM request_events WHERE req_date = :d", d=day)

    # ── audit
    def audit_append(self, at, who, action, detail):
        self.run("INSERT INTO audit_log (at, who, action, detail) VALUES (:a, :w, :ac, :d)",
                 a=at, w=who, ac=action, d=detail)

    def audit_read(self):
        return [tuple(r) for r in self.run("SELECT at, who, action, detail FROM audit_log ORDER BY id")]

    # ── blobs
    def put_blob(self, kind, key, ver, data, deleted=False):
        if kind == "ds":
            self.run("INSERT INTO dataset_versions (dkey, version, data, bytes) VALUES (:k, :v, :d, :b) "
                     "ON CONFLICT (dkey, version) DO UPDATE SET data = EXCLUDED.data, bytes = EXCLUDED.bytes",
                     k=key, v=ver, d=data, b=len(data))
        else:
            self.run("INSERT INTO run_files (run_id, data, bytes, deleted) VALUES (:v, :d, :b, :x) "
                     "ON CONFLICT (run_id) DO UPDATE SET data = EXCLUDED.data, bytes = EXCLUDED.bytes, deleted = EXCLUDED.deleted",
                     v=ver, d=data, b=len(data), x=bool(deleted))

    def get_blob(self, kind, key, ver):
        if kind == "ds":
            rows = self.run("SELECT data FROM dataset_versions WHERE dkey = :k AND version = :v", k=key, v=ver)
        else:
            rows = self.run("SELECT data FROM run_files WHERE run_id = :v AND NOT deleted", v=ver)
        return bytes(rows[0][0]) if rows else None

    def get_blob_any(self, kind, key, ver, deleted):
        if kind == "ds":
            return self.get_blob(kind, key, ver)
        rows = self.run("SELECT data FROM run_files WHERE run_id = :v AND deleted = :x", v=ver, x=bool(deleted))
        return bytes(rows[0][0]) if rows else None

    def blob_exists(self, kind, key, ver):
        return self.blob_size(kind, key, ver) is not None

    def blob_size(self, kind, key, ver):
        if kind == "ds":
            rows = self.run("SELECT bytes FROM dataset_versions WHERE dkey = :k AND version = :v", k=key, v=ver)
        else:
            rows = self.run("SELECT bytes FROM run_files WHERE run_id = :v AND NOT deleted", v=ver)
        return rows[0][0] if rows else None

    def mark_blob_deleted(self, kind, key, ver):
        if kind == "run":
            self.run("UPDATE run_files SET deleted = true WHERE run_id = :v", v=ver)

    def delete_blob(self, kind, key, ver):
        if kind == "ds":
            self.run("DELETE FROM dataset_versions WHERE dkey = :k AND version = :v", k=key, v=ver)
        else:
            self.run("DELETE FROM run_files WHERE run_id = :v", v=ver)

    def list_blobs(self, kind):
        if kind == "ds":
            rows = self.run("SELECT dkey, version, extract(epoch from created_at) FROM dataset_versions ORDER BY dkey, version")
            return [(k, v, False, float(t)) for k, v, t in rows]
        rows = self.run("SELECT run_id, deleted, extract(epoch from created_at) FROM run_files ORDER BY run_id")
        return [("", v, bool(d), float(t)) for v, d, t in rows]

    # ── misc
    def storage_bytes(self):
        return int(self.run("SELECT pg_database_size(current_database())")[0][0])

    def is_empty(self):
        n = self.run("SELECT (SELECT count(*) FROM app_settings) + (SELECT count(*) FROM app_users) + "
                     "(SELECT count(*) FROM dataset_versions) + (SELECT count(*) FROM request_events) + "
                     "(SELECT count(*) FROM feedback_log)")[0][0]
        return int(n) == 0

    def truncate_all(self):
        self.tx([("TRUNCATE app_settings, app_users, app_sessions, doc_stamps, upload_log, upload_batches, run_index, "
                  "request_events, audit_log, dataset_versions, run_files, feedback_log", {})])
        with self._map_lock:
            self._map_cache = {}


# ════════════════════════════════════════════════════════════════════
#  Copy / backup
# ════════════════════════════════════════════════════════════════════
def copy_store(src, dst, say=print):
    """Copies everything from src to dst. Returns a dict of counts."""
    counts = {}
    for name in DOCS:
        v = src.get_doc(name, None)
        if v is not None:
            dst.put_doc(name, v)
            counts["doc:" + name] = len(v) if isinstance(v, dict) else 1
    for stream in ("uploads", "batches", "runs", "feedback"):
        recs = src.read_log(stream)
        dst.rewrite_log(stream, recs)
        counts["log:" + stream] = len(recs)
    n = 0
    days = src.log_days("requests")
    for d in days:
        recs = src.read_log("requests", d)
        dst.rewrite_log("requests", recs, d)
        n += len(recs)
    counts["log:requests"] = n
    counts["request_days"] = len(days)
    lines = src.audit_read()
    if isinstance(dst, PgStore):
        stmts = [("INSERT INTO audit_log (at, who, action, detail) VALUES (:a, :w, :ac, :d)",
                  {"a": a, "w": w, "ac": ac, "d": d}) for a, w, ac, d in lines]
        for i in range(0, len(stmts), 2000):
            dst.tx(stmts[i:i + 2000])
    else:
        with open(os.path.join(dst.dir, "audit.log"), "w", encoding="utf-8") as f:
            for a, w, ac, d in lines:
                f.write("%s\t%s\t%s\t%s\n" % (a, w, ac, d))
    counts["audit"] = len(lines)
    for kind in ("ds", "run"):
        blobs = src.list_blobs(kind)
        for key, ver, deleted, _ in blobs:
            data = src.get_blob_any(kind, key, ver, deleted)
            if data is not None:
                dst.put_blob(kind, key, ver, data, deleted=deleted)
        counts["blob:" + kind] = len(blobs)
        say("  copied %d %s" % (len(blobs), "file versions" if kind == "ds" else "saved runs"))
    return counts


def verify_copy(src, dst):
    """Re-reads both sides and returns a list of differences (empty = identical)."""
    diffs = []
    for name in DOCS:
        a, b = src.get_doc(name, None), dst.get_doc(name, None)
        if a != b:
            diffs.append("document %s differs" % name)
    for stream in ("uploads", "batches", "runs", "feedback"):
        if src.read_log(stream) != dst.read_log(stream):
            diffs.append("log %s differs" % stream)
    if src.log_days() != dst.log_days():
        diffs.append("request days differ")
    for d in src.log_days():
        if src.read_log("requests", d) != dst.read_log("requests", d):
            diffs.append("requests of %s differ" % d)
    if [tuple(x) for x in src.audit_read()] != [tuple(x) for x in dst.audit_read()]:
        diffs.append("audit log differs")
    for kind in ("ds", "run"):
        a = {(k, v, x) for k, v, x, _ in src.list_blobs(kind)}
        b = {(k, v, x) for k, v, x, _ in dst.list_blobs(kind)}
        if a != b:
            diffs.append("%s list differs: missing %s" % (kind, sorted(a - b)[:5]))
            continue
        for k, v, x in a:
            if src.get_blob_any(kind, k, v, x) != dst.get_blob_any(kind, k, v, x):
                diffs.append("%s %s/%s content differs" % (kind, k, v))
    return diffs


def backup_to_zip(store, backup_dir, keep_days, tmp_root):
    """Writes the whole store as a zip in the file layout (vmm-data-*.zip).
    The zip can be unzipped into a data folder and used directly, or
    imported into PostgreSQL again with migrate_to_db.py."""
    import shutil
    os.makedirs(backup_dir, exist_ok=True)
    name = "vmm-data-%s.zip" % datetime.now().strftime("%Y%m%d-%H%M%S")
    if isinstance(store, FileStore):
        src_dir = store.dir
        cleanup = None
    else:
        src_dir = os.path.join(tmp_root, "backup-" + name[:-4])
        if os.path.exists(src_dir):
            shutil.rmtree(src_dir)
        copy_store(store, FileStore(src_dir), say=lambda *_: None)
        cleanup = src_dir
    path = os.path.join(backup_dir, name)
    try:
        with zipfile.ZipFile(path + ".tmp", "w", zipfile.ZIP_DEFLATED) as z:
            for root, _, files in os.walk(src_dir):
                for f in files:
                    if f.endswith(".tmp"):
                        continue
                    full = os.path.join(root, f)
                    z.write(full, os.path.relpath(full, src_dir))
        os.replace(path + ".tmp", path)
    finally:
        if cleanup:
            shutil.rmtree(cleanup, ignore_errors=True)
    cutoff = time.time() - keep_days * 86400
    for f in os.listdir(backup_dir):
        p = os.path.join(backup_dir, f)
        if f.startswith("vmm-data-") and f.endswith(".zip") and os.path.getmtime(p) < cutoff:
            os.remove(p)
    return name


def open_store(cfg, log=None, wait_seconds=120):
    if str(cfg.get("storage") or "files").lower() in ("postgres", "postgresql", "pg", "database", "db"):
        return PgStore(cfg.get("db") or {}, wait_seconds=wait_seconds, log=log)
    return FileStore(cfg["data_dir"])


def gz_rows(data):
    """rows of a gzip {"rows":[...]} payload"""
    return json.loads(gzip.decompress(data).decode("utf-8")).get("rows") or []
