#!/usr/bin/env python3
"""
VMM PO Governance — shared server for a Windows VM.

Python 3.9+, nothing to pip-install (the PostgreSQL driver is bundled in
./pylib), works with no internet access. Serves the web app from ./app and
keeps the shared state either in PostgreSQL ("storage": "postgres") or in the
original ./data files ("storage": "files") - see storage.py:

    uploaded file versions, who uploaded what, planner parameters,
    Tool 1 requests + approvals, saved Tool 2 runs, users, sessions, audit log

Start:  python server.py            (settings come from config.json)
"""
import gzip
import io
import json
import logging
import os
import re
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from logging.handlers import RotatingFileHandler
from http.cookies import SimpleCookie

BASE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, BASE)  # embeddable Python doesn't add the script folder itself
sys.path.insert(0, os.path.join(BASE, "pylib"))  # bundled pure-Python PostgreSQL driver
import accounts as acc  # noqa: E402
import storage  # noqa: E402

APP_DIR = os.path.join(BASE, "app")
VERSION = "3.1.4"

DEFAULT_CONFIG = {
    "host": "0.0.0.0",
    "port": 8080,
    "data_dir": "data",
    "storage": "files",
    "db": {"host": "127.0.0.1", "port": 5433, "name": "pogov", "user": "vmm_app", "password": ""},
    "admin_email": "kosaraju.srihari@vishalwholesale.co.in",
    "default_password": "1234",
    "password_min_length": 8,
    "session_idle_hours": 12,
    "max_failed_logins": 5,
    "lockout_minutes": 15,
    "restrict_to_own_mcs": True,
    "backup_dir": "backups",
    "backup_hour": 2,
    "backup_keep_days": 30,
    "cookie_secure": False,
    "max_upload_mb": 400,
    "history_retention_days": 0,
    "anthropic_api_key": "",
    "anthropic_model": "claude-sonnet-5",
    "groq_api_key": "",
    "groq_model": "openai/gpt-oss-120b",
    "outbound_proxy": "",
}


def load_config():
    cfg = dict(DEFAULT_CONFIG)
    path = os.path.join(BASE, "config.json")
    if os.path.exists(path):
        with open(path, "r", encoding="utf-8-sig") as f:
            user = json.load(f)
        cfg.update({k: v for k, v in user.items() if not k.startswith("_")})
    # environment overrides (handy for testing)
    if os.environ.get("VMM_PORT"):
        cfg["port"] = int(os.environ["VMM_PORT"])
    if os.environ.get("VMM_DATA_DIR"):
        cfg["data_dir"] = os.environ["VMM_DATA_DIR"]
    if not os.path.isabs(cfg["data_dir"]):
        cfg["data_dir"] = os.path.join(BASE, cfg["data_dir"])
    if os.environ.get("VMM_BACKUP_DIR"):
        cfg["backup_dir"] = os.environ["VMM_BACKUP_DIR"]
    if not os.path.isabs(cfg["backup_dir"]):
        cfg["backup_dir"] = os.path.join(BASE, cfg["backup_dir"])
    return cfg


CFG = load_config()
DATA = CFG["data_dir"]
TMP_DIR = os.path.join(BASE, "tmp")
for d in (os.path.join(BASE, "logs"), TMP_DIR):
    os.makedirs(d, exist_ok=True)

log = logging.getLogger("vmm")
log.setLevel(logging.INFO)
_h = RotatingFileHandler(os.path.join(BASE, "logs", "server.log"), maxBytes=5_000_000, backupCount=3, encoding="utf-8")
_h.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s"))
log.addHandler(_h)
if sys.stdout is not None:
    log.addHandler(logging.StreamHandler(sys.stdout))

BACKUP_LOCK = threading.Lock()   # one backup at a time; does not block normal requests
LOCK = threading.RLock()
KEY_RE = re.compile(r"^[A-Za-z0-9_]{1,40}$")
DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")
VER_RE = re.compile(r"^[A-Za-z0-9_-]{1,60}$")


# ── small persistence helpers ───────────────────────────────────────────
def _now():
    return datetime.now(timezone.utc).astimezone().isoformat(timespec="seconds")


def _audit(user, action, detail=""):
    STORE.audit_append(_now(), user, action, detail)
    log.info("%s %s %s", user, action, detail)


STORE = None  # set by init_store()
ACC = None


def init_store(wait_seconds=120):
    global STORE, ACC
    STORE = storage.open_store(CFG, log=log, wait_seconds=wait_seconds)
    ACC = acc.Accounts(STORE, CFG, _audit)


def manifest():
    m = STORE.get_doc("manifest", None)
    if not m:
        m = {"dataVersion": 0, "paramsVersion": 0, "datasets": {}}
    return m


def save_manifest(m):
    STORE.put_doc("manifest", m)


def read_requests(date, include_cleared=False):
    """Returns (requests, seq). A "clear" line empties the day's live ledger;
    with include_cleared=True the cleared requests are still returned (marked),
    so history never loses anything."""
    return _replay(STORE.read_log("requests", date), include_cleared)


def _replay(recs, include_cleared=False):
    reqs, seq, cleared = [], 0, []
    for rec in recs:
        seq = max(seq, rec.get("seq", 0))
        if rec.get("type") == "clear":
            for r in reqs:
                r["cleared"] = {"by": rec.get("by"), "at": rec.get("at")}
                a = r.get("approval")
                if a and a.get("status") == "pending":
                    a["status"] = "cleared"
            cleared.extend(reqs)
            reqs = []
        elif rec.get("type") == "request":
            reqs.append(rec["request"])
        elif rec.get("type") == "approval":
            for r in reqs:
                if r.get("reqId") == rec.get("reqId"):
                    _apply_approval(r, rec)
    return (cleared + reqs if include_cleared else reqs), seq


def _apply_approval(r, rec):
    a = r.setdefault("approval", {})
    a.update({"status": rec["status"], "by": rec.get("by"), "byName": rec.get("byName"),
              "at": rec.get("at"), "comment": rec.get("comment", "")})
    if rec["status"] == "approved":
        a["qty"] = rec.get("qty")
        r["finalApprovedQty"] = rec.get("qty")
    elif rec["status"] == "rejected":
        a["qty"] = 0
        r["finalApprovedQty"] = 0


UPLOAD_ID_RE = re.compile(r"^UPL-\d{8}-\d{4}$")


def _next_number(stream, prefix):
    """Next running number for IDs like PREFIX-YYYYMMDD-0001 (per day)."""
    day = datetime.now().strftime("%Y%m%d")
    tag = "%s-%s-" % (prefix, day)
    n = 0
    for e in STORE.read_log(stream):
        for v in e.values():
            if isinstance(v, str) and v.startswith(tag):
                try:
                    k = int(v[len(tag):len(tag) + 4])
                    if k < 9000:  # 9001+ are IDs given to pre-ID history
                        n = max(n, k)
                except ValueError:
                    pass
    return "%s%04d" % (tag, n + 1)


def new_batch(user, source, source_files=None, note=""):
    """One upload action (a zip, a set of files, a restore) = one Upload ID."""
    with LOCK:
        uid = _next_number("batches", "UPL")
        STORE.append_log("batches", {"uploadId": uid, "at": _now(), "by": user, "source": source,
                                    "sourceFiles": (source_files or [])[:50], "note": note[:200]})
    return uid


def _new_id():
    return datetime.now().strftime("%Y%m%d-%H%M%S") + "-%04x" % (int(time.time() * 1000) & 0xFFFF)


def migrate_flat_datasets():
    """v1.0 kept one file per key (datasets/<key>.json.gz). Move each into
    the versioned layout (datasets/<key>/<version>.json.gz) once."""
    if not isinstance(STORE, storage.FileStore):
        return
    DS_DIR = STORE.ds_dir
    m = manifest()
    changed = False
    for name in os.listdir(DS_DIR):
        p = os.path.join(DS_DIR, name)
        if not (name.endswith(".json.gz") and os.path.isfile(p)):
            continue
        key = name[:-len(".json.gz")]
        ver = "v1-" + datetime.fromtimestamp(os.path.getmtime(p)).strftime("%Y%m%d-%H%M%S")
        os.makedirs(os.path.join(DS_DIR, key), exist_ok=True)
        os.replace(p, os.path.join(DS_DIR, key, ver + ".json.gz"))
        e = m["datasets"].get(key)
        if e:
            e["version"] = ver
            STORE.append_log("uploads", dict(e, key=key, action="upload"))
        changed = True
    if changed:
        save_manifest(m)


def migrate_ids():
    """Give Upload IDs / Run IDs to history saved before IDs existed.
    Consecutive uploads by the same person within 2 minutes = one upload."""
    with LOCK:
        entries = STORE.read_log("uploads")
        if any(not e.get("uploadId") for e in entries):
            counters, batches, cur = {}, [], None
            for e in entries:
                if e.get("uploadId"):
                    cur = None
                    continue
                t = e.get("updatedAt") or _now()
                ts = datetime.fromisoformat(t).timestamp() if t else 0
                if not (cur and cur["by"] == e.get("updatedBy") and cur["action"] == e.get("action") and ts - cur["ts"] <= 120):
                    day = t[:10].replace("-", "")
                    counters[day] = counters.get(day, 0) + 1
                    uid = "UPL-%s-%04d" % (day, 9000 + counters[day])  # 9001+ marks IDs given afterwards
                    cur = {"by": e.get("updatedBy"), "action": e.get("action"), "ts": ts, "uid": uid}
                    batches.append({"uploadId": uid, "at": t, "by": e.get("updatedBy"),
                                    "source": "restore" if e.get("action") == "restore" else "clear" if e.get("action") == "clear" else "files",
                                    "sourceFiles": [], "note": "Uploaded before Upload IDs existed"})
                cur["ts"] = ts
                e["uploadId"] = cur["uid"]
            STORE.rewrite_log("uploads", entries)
            for b in batches:
                STORE.append_log("batches", b)
            m = manifest()
            by_ver = {(e["key"], e.get("version")): e["uploadId"] for e in entries if e.get("action") == "upload"}
            for k, d in m["datasets"].items():
                d.setdefault("uploadId", by_ver.get((k, d.get("version"))))
            save_manifest(m)
        idx = STORE.read_log("runs")
        if any(not e.get("runNo") and not e.get("deleted") for e in idx):
            counters = {}
            for e in idx:
                if e.get("deleted"):
                    continue
                day = e["id"][:8]
                if e.get("runNo"):
                    counters[day] = max(counters.get(day, 0), int(e["runNo"][-4:]))
                    continue
                counters[day] = counters.get(day, 0) + 1
                e["runNo"] = "RUN-%s-%04d" % (day, counters[day])
            STORE.rewrite_log("runs", idx)


def _in_range(iso, frm, to):
    d = (iso or "")[:10]
    return (not frm or d >= frm) and (not to or d <= to)


def prune_history():
    """history_retention_days = 0 keeps everything forever (default)."""
    keep = int(CFG.get("history_retention_days") or 0)
    if keep <= 0:
        return
    cutoff = time.time() - keep * 86400
    cut_iso = datetime.fromtimestamp(cutoff).strftime("%Y-%m-%d")
    for day in STORE.log_days("requests"):
        if day < cut_iso:
            STORE.delete_log_day("requests", day)
    current = {(k, e.get("version")) for k, e in manifest()["datasets"].items()}
    for key, ver, _, mtime in STORE.list_blobs("ds"):
        if (key, ver) not in current and mtime < cutoff:
            STORE.delete_blob("ds", key, ver)
    for _, rid, deleted, _ in STORE.list_blobs("run"):
        if not deleted and rid[:8] < cut_iso.replace("-", ""):
            STORE.delete_blob("run", "", rid)


def scr_rows():
    """(version, rows) of the current SCR products / Cat Head Mapping file."""
    e = manifest()["datasets"].get("catheadmap")
    if not e:
        return None, []
    if _SCR_CACHE.get("ver") != e.get("version"):
        data = STORE.get_blob("ds", "catheadmap", e.get("version", ""))
        if data is None:
            return None, []
        _SCR_CACHE.update(ver=e["version"], rows=storage.gz_rows(data))
    return e["version"], _SCR_CACHE["rows"]


_SCR_CACHE = {}


def req_key(r):
    i = r.get("info") or {}
    return "|".join([acc.nkey(i.get("brand")), acc.nkey(i.get("mcCd")), acc.nkey(i.get("mrp"))])


def is_staff(u):
    return bool(u) and bool({"admin", "planner"} & set(u["roles"]))


def visible_to(u, r, people):
    """Buyers / category heads see their own requests plus those on their MCs."""
    if is_staff(u) or not CFG.get("restrict_to_own_mcs", True):
        return True
    if (r.get("requestedByEmail") or "") == u["email"]:
        return True
    a = r.get("approval") or {}
    if u["email"] in (a.get("approvers") or []):
        return True
    return req_key(r) in (people.get(u["email"], {}).get("keys") or set())


# ── AI proxy ───────────────────────────────────────────────────────────
def _post_json(url, headers, body, timeout=90):
    handlers = []
    if CFG.get("outbound_proxy"):
        p = CFG["outbound_proxy"]
        handlers.append(urllib.request.ProxyHandler({"http": p, "https": p}))
    opener = urllib.request.build_opener(*handlers)
    req = urllib.request.Request(url, data=json.dumps(body).encode("utf-8"), method="POST")
    for k, v in headers.items():
        req.add_header(k, v)
    req.add_header("Content-Type", "application/json")
    try:
        with opener.open(req, timeout=timeout) as r:
            return r.status, json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode("utf-8"))
        except Exception:
            return e.code, {"error": {"message": str(e)}}
    except Exception as e:  # network blocked, DNS, timeout …
        return 502, {"error": {"message": "The server could not reach the AI service: %s" % e}}


def anthropic_call(system, messages, max_tokens=1000):
    return _post_json(
        "https://api.anthropic.com/v1/messages",
        {"x-api-key": CFG["anthropic_api_key"], "anthropic-version": "2023-06-01"},
        {
            "model": CFG.get("anthropic_model") or DEFAULT_CONFIG["anthropic_model"],
            "max_tokens": max_tokens,
            "system": system,
            "messages": messages,
        },
    )


def insights_call(prompt):
    """AI Insights. Uses Groq when a Groq key is set, otherwise Anthropic, so
    one Anthropic key is enough for both AI features. Always answers in the
    OpenAI-style shape the page expects: {"choices":[{"message":{"content":…}}]}."""
    if CFG.get("groq_api_key"):
        url = "https://api.groq.com/openai/v1/chat/completions"
        hdr = {"Authorization": "Bearer " + CFG["groq_api_key"]}
        req = {
            "model": CFG.get("groq_model") or DEFAULT_CONFIG["groq_model"],
            "messages": [{"role": "user", "content": prompt}],
            "temperature": 0.5,
            "response_format": {"type": "json_object"},
        }
        status, data = _post_json(url, hdr, req)
        if status == 400 and "response_format" in json.dumps(data):
            req.pop("response_format")  # model without JSON mode — the page parses plain text too
            status, data = _post_json(url, hdr, req)
        data["_provider"] = "groq"
        return status, data
    status, data = anthropic_call(
        "You are a retail PO-governance analyst. Reply with a single JSON object only — no prose, no code fences.",
        [{"role": "user", "content": prompt}],
        max_tokens=2000,
    )
    if status != 200:
        data["_provider"] = "anthropic"
        return status, data
    text = "".join(b.get("text", "") for b in data.get("content", []) if b.get("type") == "text")
    return 200, {"choices": [{"message": {"role": "assistant", "content": text}}], "_provider": "anthropic"}


def test_ai():
    """python server.py --test-ai : checks the VM can reach the AI services."""
    ok = True
    print("Outbound proxy:", CFG.get("outbound_proxy") or "(none)")
    if CFG.get("anthropic_api_key"):
        st, data = anthropic_call("Reply with the single word OK.", [{"role": "user", "content": "ping"}], max_tokens=10)
        msg = "".join(b.get("text", "") for b in data.get("content", [])) if st == 200 else json.dumps(data)[:300]
        print("AI chat     (Anthropic, %s): %s  %s" % (CFG["anthropic_model"], "OK" if st == 200 else "FAILED %s" % st, msg))
        ok &= st == 200
    else:
        print("AI chat     : off (no anthropic_api_key)")
    if CFG.get("groq_api_key") or CFG.get("anthropic_api_key"):
        st, data = insights_call('Return {"findings":[],"action":"ok"} exactly.')
        prov = data.get("_provider")
        print("AI Insights (%s): %s  %s" % (prov, "OK" if st == 200 else "FAILED %s" % st,
                                           "" if st == 200 else json.dumps(data)[:300]))
        ok &= st == 200
    else:
        print("AI Insights : off (no key)")
    return 0 if ok else 1


# ── HTTP handler ───────────────────────────────────────────────────────
class Handler(SimpleHTTPRequestHandler):
    server_version = "VMM-PO/" + VERSION
    protocol_version = "HTTP/1.1"

    def __init__(self, *a, **kw):
        super().__init__(*a, directory=APP_DIR, **kw)

    # quieter default logging → our logger
    def log_message(self, fmt, *args):
        if self.path.startswith("/api/status"):
            return
        log.info("%s %s", self.address_string(), fmt % args)

    # ---- helpers
    me = None
    token = None

    def user(self):
        return (self.me or {}).get("name") or "unknown"

    def has(self, *roles):
        return bool(self.me) and bool(set(roles) & set(self.me["roles"]))

    def client_ip(self):
        return self.headers.get("X-Forwarded-For", "").split(",")[0].strip() or self.client_address[0]

    def _session_token(self):
        c = SimpleCookie()
        try:
            c.load(self.headers.get("Cookie", ""))
        except Exception:
            return None
        return c["vmm_sess"].value if "vmm_sess" in c else None

    def set_cookie(self, token, max_age):
        parts = ["vmm_sess=%s" % token, "Path=/", "HttpOnly", "SameSite=Strict", "Max-Age=%d" % max_age]
        if CFG.get("cookie_secure"):
            parts.append("Secure")
        self._pending_cookie = "; ".join(parts)

    def send_json(self, obj, status=200):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def error_json(self, status, msg):
        self.send_json({"error": msg}, status)

    def read_body(self):
        n = int(self.headers.get("Content-Length") or 0)
        limit = int(CFG["max_upload_mb"]) * 1024 * 1024
        if n > limit:
            raise ValueError("Upload is larger than max_upload_mb (%s MB) in config.json" % CFG["max_upload_mb"])
        buf = bytearray()
        while len(buf) < n:
            chunk = self.rfile.read(min(1 << 20, n - len(buf)))
            if not chunk:
                break
            buf.extend(chunk)
        return bytes(buf)

    def read_json_body(self):
        raw = self.read_body()
        return json.loads(raw.decode("utf-8")) if raw else {}

    def planner_ok(self):
        return self.has("planner", "admin")

    def end_headers(self):
        # Static files: always revalidate so a new index.html reaches users.
        if not self.path.startswith("/api/"):
            self.send_header("Cache-Control", "no-cache")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("X-Frame-Options", "DENY")
        self.send_header("Referrer-Policy", "same-origin")
        ck = getattr(self, "_pending_cookie", None)
        if ck:
            self.send_header("Set-Cookie", ck)
            self._pending_cookie = None
        super().end_headers()

    # ---- routing
    def route(self, method):
        parsed = urllib.parse.urlparse(self.path)
        path = parsed.path
        query = urllib.parse.parse_qs(parsed.query)
        if not path.startswith("/api/"):
            return False
        # Decode each part: the page sends emails as %40 (e.g. users/a%40b.in/update).
        parts = [urllib.parse.unquote(p) for p in path[len("/api/"):].strip("/").split("/")]
        self.token = self._session_token()
        try:
            self.me = ACC.session_user(self.token)
        except Exception as e:
            log.error("Storage not reachable: %s", e)
            self.error_json(503, "The database is not reachable right now - please try again in a minute.")
            return True
        public = (method, parts[0]) in (("POST", "auth"), ("GET", "health")) and (parts[0] == "health" or parts[1:2] == ["login"])
        if method != "GET" and self.headers.get("X-VMM") != "1":
            self.error_json(403, "Missing X-VMM header")  # blocks cross-site form posts
            return True
        if not public:
            if not self.me:
                self.error_json(401, "Please log in")
                return True
            allowed_when_must_change = ("auth",)
            if self.me.get("mustChange") and parts[0] not in allowed_when_must_change:
                self.send_json({"error": "Please change your password first", "mustChange": True}, 403)
                return True
        try:
            name = "api_%s_%s" % (method, parts[0].replace("-", "_"))
            fn = getattr(self, name, None)
            if fn is None:
                self.error_json(404, "Unknown API endpoint")
            else:
                fn(parts[1:], query)
        except ValueError as e:
            self.error_json(400, str(e))
        except Exception as e:  # never crash the server on one bad request
            log.exception("API error")
            self.error_json(500, "Server error: %s" % e)
        return True

    def send_video(self, path):
        """Static video with HTTP Range support, so the player can seek."""
        fs = os.path.join(APP_DIR, *[p for p in path.split("/") if p and p not in (".", "..")])
        if not (os.path.isfile(fs) and os.path.realpath(fs).startswith(os.path.realpath(APP_DIR))):
            self.send_error(404)
            return
        size = os.path.getsize(fs)
        ctype = "video/webm" if fs.endswith(".webm") else "video/mp4"
        start, end = 0, size - 1
        m = re.match(r"bytes=(\d*)-(\d*)", self.headers.get("Range") or "")
        if m and (m.group(1) or m.group(2)):
            if m.group(1):
                start = int(m.group(1))
                end = int(m.group(2)) if m.group(2) else size - 1
            else:
                start = max(0, size - int(m.group(2)))
            end = min(end, size - 1)
            if start > end:
                self.send_response(416)
                self.send_header("Content-Range", "bytes */%d" % size)
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
            self.send_response(206)
            self.send_header("Content-Range", "bytes %d-%d/%d" % (start, end, size))
        else:
            self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(end - start + 1))
        self.end_headers()
        with open(fs, "rb") as f:
            f.seek(start)
            left = end - start + 1
            while left > 0:
                chunk = f.read(min(1 << 20, left))
                if not chunk:
                    break
                try:
                    self.wfile.write(chunk)
                except (BrokenPipeError, ConnectionResetError):
                    return
                left -= len(chunk)

    def do_GET(self):
        if self.route("GET"):
            return
        path = urllib.parse.urlparse(self.path).path
        if path.startswith("/video/"):
            return self.send_video(path)
        if path in ("/", "/index.html"):
            try:
                u = ACC.session_user(self._session_token())
            except Exception as e:
                log.error("Storage not reachable: %s", e)
                body = b"<h3>VMM PO Governance</h3><p>The database is not reachable right now - please try again in a minute.</p>"
                self.send_response(503)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
                return
            if not u or u.get("mustChange"):
                self.send_response(302)
                self.send_header("Location", "/login.html" + ("?change=1" if u else ""))
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
        super().do_GET()

    def do_HEAD(self):
        super().do_HEAD()

    def do_POST(self):
        if not self.route("POST"):
            self.error_json(405, "Method not allowed")

    def do_PUT(self):
        if not self.route("PUT"):
            self.error_json(405, "Method not allowed")

    def do_DELETE(self):
        if not self.route("DELETE"):
            self.error_json(405, "Method not allowed")

    # ---- /api/status
    def api_GET_status(self, parts, q):
        if self.token and self.me:
            self.set_cookie(self.token, int(float(CFG.get("session_idle_hours") or 12) * 3600))
        with LOCK:
            m = manifest()
            params = STORE.get_doc("params", None)
            today = datetime.now().strftime("%Y-%m-%d")
            _, seq = read_requests(today)
        self.send_json({
            "version": VERSION,
            "serverTime": _now(),
            "today": datetime.now().strftime("%Y-%m-%d"),
            "dataVersion": m["dataVersion"],
            "paramsVersion": m["paramsVersion"],
            "datasets": m["datasets"],
            "params": params,
            "reqSeq": seq,
            "me": self.me_public(),
            "pendingApprovals": self.pending_count(),
            "ai": {"chat": bool(CFG.get("anthropic_api_key")),
                   "insights": bool(CFG.get("groq_api_key") or CFG.get("anthropic_api_key"))},
        })

    # ---- /api/auth/…  and /api/health
    def api_GET_health(self, parts, q):
        self.send_json({"ok": True, "version": VERSION, "storage": STORE.kind})

    def me_public(self):
        if not self.me:
            return None
        d = ACC.public(self.me)
        d["roleLabels"] = [acc.ROLE_LABEL[r] for r in d["roles"]]
        people, _ = ACC.scope_index(scr_rows)
        d["mcCount"] = len(people.get(self.me["email"], {}).get("keys") or [])
        d["restricted"] = not is_staff(self.me) and bool(CFG.get("restrict_to_own_mcs", True))
        if d["restricted"]:
            d["scopeKeys"] = sorted(people.get(self.me["email"], {}).get("keys") or [])
        return d

    def pending_count(self):
        me = self.me["email"]
        staff = is_staff(self.me)
        if not (staff or "cathead" in self.me["roles"]):
            return 0
        n = 0
        for r in self._approval_items(30):
            a = r["approval"]
            if a.get("status") == "pending" and (r.get("requestedByEmail") or "") != me and (staff or me in (a.get("approvers") or [])):
                n += 1
        return n

    def api_GET_auth(self, parts, q):
        if parts[:1] == ["me"]:
            return self.send_json({"user": self.me_public(), "passwordMinLength": int(CFG.get("password_min_length") or 8)})
        self.error_json(404, "Unknown endpoint")

    def api_POST_auth(self, parts, q):
        what = parts[0] if parts else ""
        if what == "login":
            body = self.read_json_body()
            time.sleep(0.3)  # slows password guessing
            token, err = ACC.login(body.get("email", ""), str(body.get("password", "")), self.client_ip())
            if err:
                return self.error_json(401, err)
            self.set_cookie(token, int(float(CFG.get("session_idle_hours") or 12) * 3600))
            u = ACC.users[acc.norm_email(body.get("email"))]
            return self.send_json({"ok": True, "mustChange": bool(u.get("mustChange")), "name": u["name"]})
        if what == "logout":
            if self.token:
                ACC.logout(self.token)
            self.set_cookie("x", 0)
            return self.send_json({"ok": True})
        if what == "change-password":
            body = self.read_json_body()
            err = ACC.change_password(self.me["email"], str(body.get("old", "")), str(body.get("new", "")))
            if err:
                return self.error_json(400, err)
            ACC.end_sessions(self.me["email"], keep=self.token)  # sign out other devices
            return self.send_json({"ok": True})
        self.error_json(404, "Unknown endpoint")

    # ---- /api/users  (admin)
    def api_GET_users(self, parts, q):
        if not self.has("admin"):
            return self.error_json(403, "Admin only")
        if parts[:1] == ["scr"]:
            ver, rows = scr_rows()
            info = ACC.analyse_scr(rows)
            people = ACC.scr_people(rows) if rows else {}
            return self.send_json({"hasScr": bool(rows), "scrRows": len(rows), "analysis": info, "mapping": ACC.mapping(),
                                   "people": [{"email": e, "name": d["name"], "roles": sorted(d["roles"]), "mcs": len(d["keys"]),
                                               "exists": e in ACC.users} for e, d in sorted(people.items())]})
        people, _ = ACC.scope_index(scr_rows)
        users = []
        for u in ACC.users.values():
            d = ACC.public(u)
            d["mcCount"] = len(people.get(u["email"], {}).get("keys") or [])
            users.append(d)
        users.sort(key=lambda d: (not d["active"], d["name"].lower()))
        self.send_json({"users": users, "roles": [{"id": r, "label": acc.ROLE_LABEL[r]} for r in acc.ROLES],
                        "defaultPassword": ACC.default_password()})

    def api_POST_users(self, parts, q):
        if not self.has("admin"):
            return self.error_json(403, "Admin only")
        body = self.read_json_body()
        by = self.me["email"]
        if not parts:
            u = ACC.add_user(body.get("email", ""), str(body.get("name", "")).strip(), body.get("roles") or [], by)
            return self.send_json({"ok": True, "user": ACC.public(u)})
        if parts[0] == "bulk":
            done, errors = [], []
            for row in body.get("users") or []:
                try:
                    ACC.add_user(row.get("email", ""), str(row.get("name", "")).strip(), row.get("roles") or [], by, "bulk")
                    done.append(row.get("email"))
                except ValueError as e:
                    errors.append(str(e))
            return self.send_json({"ok": True, "added": done, "errors": errors})
        if parts[0] == "scr":
            if parts[1:2] == ["mapping"]:
                ACC.save_mapping(body.get("mapping") or {}, by)
            _, rows = scr_rows()
            if not rows:
                return self.error_json(400, "Upload the SCR products file first (Data Setup → Cat Head Mapping / SCR)")
            return self.send_json({"ok": True, "result": ACC.sync_from_scr(rows, by)})
        email = acc.norm_email(parts[0])
        if email not in ACC.users:
            return self.error_json(404, "No such user")
        action = parts[1] if len(parts) > 1 else ""
        if action == "update":
            u = ACC.update_user(email, body, by)
            return self.send_json({"ok": True, "user": ACC.public(u)})
        if action == "reset-password":
            ACC.reset_password(email, by)
            return self.send_json({"ok": True})
        if action == "delete":
            if email == self.me["email"]:
                return self.error_json(400, "You cannot delete your own login")
            if email == acc.norm_email(CFG.get("admin_email")):
                return self.error_json(400, "The main admin (config.json admin_email) cannot be deleted")
            ACC.delete_user(email, by)
            return self.send_json({"ok": True})
        self.error_json(404, "Unknown endpoint")

    # ---- /api/audit  and /api/backups  (admin)
    def api_GET_audit(self, parts, q):
        if not self.has("admin"):
            return self.error_json(403, "Admin only")
        frm, to = self._range(q)
        text = (q.get("q") or [""])[0].lower()
        out = []
        for at, who, action, detail in STORE.audit_read():
            if not _in_range(at, frm, to):
                continue
            if text and text not in ("%s\t%s\t%s\t%s" % (at, who, action, detail)).lower():
                continue
            out.append({"at": at, "who": who, "action": action, "detail": detail})
        out.reverse()
        self.send_json({"items": out[:5000], "total": len(out)})

    def api_GET_backups(self, parts, q):
        if not self.has("admin"):
            return self.error_json(403, "Admin only")
        self.send_json({"items": acc.list_backups(CFG["backup_dir"]), "dir": CFG["backup_dir"],
                        "hour": CFG.get("backup_hour"), "keepDays": CFG.get("backup_keep_days")})

    def api_POST_backups(self, parts, q):
        if not self.has("admin"):
            return self.error_json(403, "Admin only")
        with BACKUP_LOCK:
            name = storage.backup_to_zip(STORE, CFG["backup_dir"], int(CFG.get("backup_keep_days") or 30), TMP_DIR)
        _audit(self.user(), "backup", name)
        self.send_json({"ok": True, "name": name})

    # ---- /api/approvals  (Watch List requests → category head)
    def _approval_items(self, days):
        out = []
        names = STORE.log_days("requests")[-days:]
        by_day = STORE.read_logs("requests", names)
        for name in names:
            reqs, _ = _replay(by_day.get(name, []))
            out.extend(r for r in reqs if r.get("approval"))
        return out

    def api_GET_approvals(self, parts, q):
        days = int((q.get("days") or ["90"])[0])
        with LOCK:
            items = self._approval_items(days)
        me = self.me["email"]
        admin = self.has("admin", "planner")
        mine = [r for r in items if admin or me in (r["approval"].get("approvers") or [])
                or (not r["approval"].get("approvers") and admin)]
        # your own requests are approved by someone else (category head / admin / planner)
        pending = [r for r in mine if r["approval"].get("status") == "pending" and (r.get("requestedByEmail") or "") != me]
        decided = [r for r in mine if r["approval"].get("status") != "pending"]
        decided.sort(key=lambda r: r["approval"].get("at") or "", reverse=True)
        pending.sort(key=lambda r: r.get("savedAt") or "")
        self.send_json({"pending": pending, "decided": decided[:300]})

    def api_POST_approvals(self, parts, q):
        rid = parts[0] if parts else ""
        body = self.read_json_body()
        status = body.get("decision")
        if status not in ("approved", "rejected"):
            return self.error_json(400, "decision must be approved or rejected")
        m = re.match(r"^REQ-(\d{4})(\d{2})(\d{2})-\d{4}$", rid)
        if not m:
            return self.error_json(400, "Bad request id")
        date = "%s-%s-%s" % m.groups()
        with LOCK:
            reqs, seq = read_requests(date)
            r = next((x for x in reqs if x.get("reqId") == rid), None)
            if not r or not r.get("approval"):
                return self.error_json(404, "Request not found or doesn't need approval")
            a = r["approval"]
            if a.get("status") != "pending":
                return self.error_json(409, "Already %s by %s" % (a.get("status"), a.get("byName") or a.get("by")))
            approvers = a.get("approvers") or []
            if (r.get("requestedByEmail") or "") == self.me["email"]:
                return self.error_json(403, "You cannot approve or reject your own request")
            if not (self.has("admin", "planner") or self.me["email"] in approvers):
                return self.error_json(403, "This request is waiting for %s" % (", ".join(approvers) or "an admin"))
            qty = None
            if status == "approved":
                if body.get("qty") is None or str(body.get("qty")).strip() == "":
                    return self.error_json(400, "Enter the quantity to approve")
                qty = int(body.get("qty"))
                if qty < 0 or qty > int(r.get("qty") or 0):
                    return self.error_json(400, "Approved qty must be between 0 and the requested %s" % r.get("qty"))
            comment = str(body.get("comment") or "")[:500]
            if status == "rejected" and not comment.strip():
                return self.error_json(400, "Please add a reason when rejecting")
            seq += 1
            rec = {"type": "approval", "seq": seq, "reqId": rid, "status": status, "qty": qty, "comment": comment,
                   "by": self.me["email"], "byName": self.me["name"], "at": _now()}
            STORE.append_log("requests", rec, date)
        _audit(self.user(), "approval", "%s %s qty=%s %s" % (rid, status, qty, comment))
        self.send_json({"ok": True, "seq": seq})

    # ---- /api/data/<key>
    def api_GET_data(self, parts, q):
        key = parts[0] if parts else ""
        if not KEY_RE.match(key):
            return self.error_json(400, "Bad dataset key")
        ver = (q.get("version") or [""])[0]
        if not ver:
            e = manifest()["datasets"].get(key)
            if not e:
                return self.error_json(404, "No data uploaded for " + key)
            ver = e.get("version", "")
        if not VER_RE.match(ver):
            return self.error_json(400, "Bad version")
        data = STORE.get_blob("ds", key, ver)
        if data is None:
            return self.error_json(404, "That version of %s is no longer stored" % key)
        accepts_gzip = "gzip" in (self.headers.get("Accept-Encoding") or "")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        if not accepts_gzip:
            data = gzip.decompress(data)
        else:
            self.send_header("Content-Encoding", "gzip")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        for i in range(0, len(data), 1 << 20):
            self.wfile.write(data[i:i + (1 << 20)])

    def api_PUT_data(self, parts, q):
        if not self.planner_ok():
            return self.error_json(403, "Only planners and admins can do this")
        key = parts[0] if parts else ""
        if not KEY_RE.match(key):
            return self.error_json(400, "Bad dataset key")
        raw = self.read_body()
        if self.headers.get("X-Body-Encoding") == "gzip":
            payload_gz = raw
            try:
                head = gzip.GzipFile(fileobj=io.BytesIO(raw)).read(64)
            except OSError:
                return self.error_json(400, "Body is not valid gzip")
        else:
            head = raw[:64]
            payload_gz = gzip.compress(raw, compresslevel=6)
        # Cheap sanity check instead of parsing tens of MB of JSON here.
        if not re.match(rb'^\s*\{\s*"rows"\s*:\s*\[', head):
            return self.error_json(400, "Body must be {\"rows\": [...]}")
        rows = int(self.headers.get("X-Rows") or 0)
        uid = self.headers.get("X-Upload-Id", "")
        if not UPLOAD_ID_RE.match(uid):
            uid = new_batch(self.user(), "file")
        with LOCK:
            ver = _new_id()
            STORE.put_blob("ds", key, ver, payload_gz)
            m = manifest()
            m["dataVersion"] += 1
            entry = {
                "version": ver,
                "rows": rows,
                "bytes": len(payload_gz),
                "label": urllib.parse.unquote(self.headers.get("X-Label", ""))[:200],
                "updatedBy": self.user(),
                "updatedAt": _now(),
                "uploadId": uid,
            }
            m["datasets"][key] = entry
            save_manifest(m)
            STORE.append_log("uploads", dict(entry, key=key, action="upload"))
        _audit(self.user(), "upload", "%s rows=%d bytes=%d %s" % (key, rows, len(payload_gz), uid))
        resp = {"ok": True, "dataVersion": m["dataVersion"], "entry": entry}
        if key == "catheadmap":  # SCR products: create logins for new buyers / category heads
            try:
                resp["users"] = ACC.sync_from_scr(storage.gz_rows(payload_gz), self.me["email"])
            except Exception as e:
                log.exception("SCR sync failed")
                resp["usersError"] = str(e)
        self.send_json(resp)

    def api_DELETE_data(self, parts, q):
        if not self.planner_ok():
            return self.error_json(403, "Only planners and admins can do this")
        key = parts[0] if parts else ""
        if not KEY_RE.match(key):
            return self.error_json(400, "Bad dataset key")
        with LOCK:
            # Only removes the file from "current" — every uploaded version
            # stays in history.
            m = manifest()
            old = m["datasets"].pop(key, None)
            if old:
                uid = self.headers.get("X-Upload-Id", "")
                if not UPLOAD_ID_RE.match(uid):
                    uid = new_batch(self.user(), "clear")
                STORE.append_log("uploads", {"key": key, "action": "clear", "version": old.get("version"),
                                             "updatedBy": self.user(), "updatedAt": _now(), "uploadId": uid})
            m["dataVersion"] += 1
            save_manifest(m)
        _audit(self.user(), "delete", key)
        self.send_json({"ok": True, "dataVersion": m["dataVersion"]})

    def api_POST_data(self, parts, q):
        """/api/data/<key>/restore?version=… → make an older upload current."""
        if not self.planner_ok():
            return self.error_json(403, "Only planners and admins can do this")
        if len(parts) < 2 or parts[1] != "restore":
            return self.error_json(404, "Unknown endpoint")
        key, ver = parts[0], (q.get("version") or [""])[0]
        if not KEY_RE.match(key) or not VER_RE.match(ver):
            return self.error_json(400, "Bad key or version")
        with LOCK:
            hist = [e for e in STORE.read_log("uploads") if e.get("key") == key and e.get("version") == ver and e.get("action") == "upload"]
            if not hist or not STORE.blob_exists("ds", key, ver):
                return self.error_json(404, "That version is no longer stored")
            src = hist[-1].get("uploadId", "")
            uid = new_batch(self.user(), "restore", note="Made current again from %s" % (src or "an earlier upload"))
            m = manifest()
            entry = {k: hist[-1][k] for k in ("version", "rows", "bytes", "label", "updatedBy", "updatedAt", "uploadId") if k in hist[-1]}
            entry["restoredBy"], entry["restoredAt"] = self.user(), _now()
            m["datasets"][key] = entry
            m["dataVersion"] += 1
            save_manifest(m)
            STORE.append_log("uploads", dict(entry, key=key, action="restore", updatedBy=self.user(), updatedAt=_now(),
                                             uploadId=uid, restoredFrom=src))
        _audit(self.user(), "restore", "%s %s" % (key, ver))
        self.send_json({"ok": True, "dataVersion": m["dataVersion"], "entry": entry})

    # ---- /api/uploads  → start an upload (get its Upload ID)
    #      /api/uploads/<UPL-…>/restore → make every file of that upload current again
    def api_POST_uploads(self, parts, q):
        if not self.planner_ok():
            return self.error_json(403, "Only planners and admins can do this")
        if not parts:
            body = self.read_json_body()
            files = [str(x)[:200] for x in (body.get("sourceFiles") or []) if x]
            uid = new_batch(self.user(), str(body.get("source") or "files")[:20], files)
            return self.send_json({"ok": True, "uploadId": uid})
        uid = parts[0]
        if len(parts) < 2 or parts[1] != "restore":
            return self.error_json(404, "Unknown endpoint")
        with LOCK:
            ups = [e for e in STORE.read_log("uploads") if e.get("uploadId") == uid and e.get("action") == "upload"]
            if not ups:
                return self.error_json(404, "No files found for " + uid)
            latest = {}
            for e in ups:
                latest[e["key"]] = e
            missing = [k for k, e in latest.items() if not STORE.blob_exists("ds", k, e["version"])]
            if missing:
                return self.error_json(404, "No longer stored: " + ", ".join(missing))
            new_uid = new_batch(self.user(), "restore", note="Made current again from %s" % uid)
            m = manifest()
            for k, e in latest.items():
                entry = {x: e[x] for x in ("version", "rows", "bytes", "label", "updatedBy", "updatedAt", "uploadId") if x in e}
                entry["restoredBy"], entry["restoredAt"] = self.user(), _now()
                m["datasets"][k] = entry
                STORE.append_log("uploads", dict(entry, key=k, action="restore", updatedBy=self.user(), updatedAt=_now(),
                                                 uploadId=new_uid, restoredFrom=uid))
            m["dataVersion"] += 1
            save_manifest(m)
        _audit(self.user(), "restore-upload", "%s -> %s (%s)" % (uid, new_uid, ", ".join(latest)))
        self.send_json({"ok": True, "uploadId": new_uid, "dataVersion": m["dataVersion"], "keys": list(latest)})

    # ---- /api/history/<uploads|requests>?from=YYYY-MM-DD&to=YYYY-MM-DD
    def _range(self, q):
        frm = (q.get("from") or [""])[0]
        to = (q.get("to") or [""])[0]
        for d in (frm, to):
            if d and not DATE_RE.match(d):
                raise ValueError("from/to must be YYYY-MM-DD")
        return frm, to

    def api_GET_history(self, parts, q):
        kind = parts[0] if parts else ""
        frm, to = self._range(q)
        if kind == "uploads":
            if not self.planner_ok():
                return self.error_json(403, "Planner or admin only")
            with LOCK:
                cur = {k: e.get("version") for k, e in manifest()["datasets"].items()}
                entries = STORE.read_log("uploads")
                batches = {b["uploadId"]: b for b in STORE.read_log("batches")}
                stored = {(k, v) for k, v, _, _ in STORE.list_blobs("ds")}
            groups = {}
            for e in entries:
                uid = e.get("uploadId")
                if not uid:  # uploaded before Upload IDs existed: group by minute + user
                    t = e.get("updatedAt") or ""
                    uid = "UPL-%s-L%s" % (t[:10].replace("-", ""), t[11:16].replace(":", ""))
                e["stored"] = bool(e.get("version")) and (e.get("key"), e.get("version")) in stored
                e["current"] = cur.get(e.get("key")) == e.get("version") and e.get("action") != "clear"
                g = groups.get(uid)
                if not g:
                    b = batches.get(uid, {})
                    g = groups[uid] = {"uploadId": uid, "at": b.get("at") or e.get("updatedAt"), "by": b.get("by") or e.get("updatedBy"),
                                       "source": b.get("source") or e.get("action"), "sourceFiles": b.get("sourceFiles") or [],
                                       "note": b.get("note", ""), "files": []}
                g["files"].append(e)
            items = [g for g in groups.values() if _in_range(g["at"], frm, to)]
            items.sort(key=lambda g: g["at"] or "", reverse=True)
            return self.send_json({"items": items, "storageBytes": STORE.storage_bytes()})
        if kind == "requests":
            items = []
            with LOCK:
                days = [d for d in STORE.log_days("requests") if not ((frm and d < frm) or (to and d > to))]
                by_day = STORE.read_logs("requests", days)
                for d in days:
                    reqs, _ = _replay(by_day.get(d, []), include_cleared=True)
                    for r in reqs:
                        if not r.get("reqId"):
                            r["reqId"] = "REQ-%s-%04d" % (d.replace("-", ""), r.get("seq") or r.get("id") or 0)
                    items.extend(reqs)
            people, _ = ACC.scope_index(scr_rows)
            items = [r for r in items if visible_to(self.me, r, people)]
            items.sort(key=lambda r: r.get("savedAt", ""), reverse=True)
            return self.send_json({"items": items})
        self.error_json(404, "Unknown history type")

    # ---- /api/runs  (saved Tool 2 assessment runs)
    def api_GET_runs(self, parts, q):
        if not self.planner_ok():
            return self.error_json(403, "Planner or admin only")
        if parts:
            rid = parts[0]
            if not VER_RE.match(rid):
                return self.error_json(400, "Bad run id")
            data = STORE.get_blob("run", "", rid)
            if data is None:
                return self.error_json(404, "Run not found")
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Encoding", "gzip")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(data)
            return
        frm, to = self._range(q)
        with LOCK:
            idx = STORE.read_log("runs")
            live = {rid for _, rid, dl, _ in STORE.list_blobs("run") if not dl}
        deleted = {e["id"] for e in idx if e.get("deleted")}
        items = [e for e in idx if not e.get("deleted") and e["id"] not in deleted and _in_range(e.get("at"), frm, to)
                 and e["id"] in live]
        for e in items:
            e.setdefault("runNo", "RUN-" + e["id"][:8] + "-" + e["id"][9:13])
        items.reverse()
        self.send_json({"items": items})

    def api_POST_runs(self, parts, q):
        if not self.planner_ok():
            return self.error_json(403, "Only planners and admins can do this")
        raw = self.read_body()
        meta = json.loads(urllib.parse.unquote(self.headers.get("X-Run-Meta", "") or "{}"))
        try:
            head = gzip.GzipFile(fileobj=io.BytesIO(raw)).read(64)
        except OSError:
            return self.error_json(400, "Body is not valid gzip")
        if not head.lstrip().startswith(b"{"):
            return self.error_json(400, "Bad run payload")
        rid = _new_id()
        meta.update({"id": rid, "at": _now(), "by": self.user(), "bytes": len(raw)})
        with LOCK:
            meta["runNo"] = _next_number("runs", "RUN")
            STORE.put_blob("run", "", rid, raw)
            STORE.append_log("runs", meta)
        _audit(self.user(), "run-saved", "%s lines=%s" % (rid, meta.get("lines")))
        self.send_json({"ok": True, "id": rid, "meta": meta})

    def api_DELETE_runs(self, parts, q):
        if not self.planner_ok():
            return self.error_json(403, "Only planners and admins can do this")
        rid = parts[0] if parts else ""
        if not VER_RE.match(rid):
            return self.error_json(400, "Bad run id")
        with LOCK:
            STORE.append_log("runs", {"id": rid, "deleted": True, "by": self.user(), "at": _now()})
            STORE.mark_blob_deleted("run", "", rid)  # recoverable by an admin
        _audit(self.user(), "run-deleted", rid)
        self.send_json({"ok": True})

    # ---- /api/params
    def api_GET_params(self, parts, q):
        self.send_json(STORE.get_doc("params", {}) or {})

    def api_PUT_params(self, parts, q):
        if not self.planner_ok():
            return self.error_json(403, "Only planners and admins can do this")
        params = self.read_json_body()
        if not isinstance(params, dict):
            return self.error_json(400, "Params must be an object")
        with LOCK:
            STORE.put_doc("params", params)
            m = manifest()
            m["paramsVersion"] += 1
            save_manifest(m)
        _audit(self.user(), "params", json.dumps({k: params.get(k) for k in ("T1", "T2", "D", "mix", "N", "wof", "peer", "strBuffer", "borderTolerance", "seasonCurrent", "seasonFallback")}))
        self.send_json({"ok": True, "paramsVersion": m["paramsVersion"]})

    # ---- /api/requests
    def _date(self, q):
        date = (q.get("date") or [datetime.now().strftime("%Y-%m-%d")])[0]
        if not DATE_RE.match(date):
            raise ValueError("date must be YYYY-MM-DD")
        return date

    def api_GET_requests(self, parts, q):
        date = self._date(q)
        with LOCK:
            reqs, seq = read_requests(date)
        if not is_staff(self.me) and CFG.get("restrict_to_own_mcs", True):
            people, _ = ACC.scope_index(scr_rows)
            reqs = [r for r in reqs if visible_to(self.me, r, people)]
        self.send_json({"date": date, "seq": seq, "requests": reqs, "today": datetime.now().strftime("%Y-%m-%d")})

    def api_POST_requests(self, parts, q):
        if not self.has("buyer", "cathead", "planner", "admin"):
            return self.error_json(403, "Not allowed")
        body = self.read_json_body()
        r = body.get("request")
        if not isinstance(r, dict):
            return self.error_json(400, "Missing request")
        date = datetime.now().strftime("%Y-%m-%d")   # server date, not the PC clock
        r["reqDate"] = date
        seen = int(body.get("seenSeq") or 0)
        # Buyers / category heads may only raise requests on the Brand x MC x MRP
        # mapped to them in the SCR products file (admins and planners: any MC).
        if not is_staff(self.me) and CFG.get("restrict_to_own_mcs", True):
            i = r.get("info") or {}
            if i.get("mcCd"):
                people, _ = ACC.scope_index(scr_rows)
                if req_key(r) not in (people.get(self.me["email"], {}).get("keys") or set()):
                    return self.error_json(403, "This Brand / MC / MRP is not mapped to you in the SCR products file. Ask the administrator.")
        with LOCK:
            reqs, seq = read_requests(date)
            if not body.get("force") and r.get("colourKey"):
                # Anyone else approve against this exact colour since the
                # client last looked? Then its headroom check is stale.
                newer = [x for x in reqs if x.get("seq", 0) > seen and x.get("colourKey") == r["colourKey"]]
                if newer:
                    by = ", ".join(sorted({x.get("requestedBy", "?") for x in newer}))
                    return self.send_json({"conflict": True, "byOthers": by, "seq": seq}, 409)
            seq += 1
            r["seq"] = seq
            r["id"] = len(reqs) + 1
            r["reqId"] = "REQ-%s-%04d" % (date.replace("-", ""), seq)
            r["requestedBy"] = self.me["name"]
            r["requestedByEmail"] = self.me["email"]
            r["savedAt"] = _now()
            r.pop("approval", None)
            if r.get("decision") == "Watch List":
                _, approvers = ACC.scope_index(scr_rows)
                r["approval"] = {"status": "pending", "approvers": approvers.get(req_key(r), []),
                                 "approverNames": [ACC.users[e]["name"] for e in approvers.get(req_key(r), []) if e in ACC.users]}
            STORE.append_log("requests", {"type": "request", "seq": seq, "request": r}, date)
        _audit(r["requestedBy"], "request", "%s %s %s qty=%s -> %s approved=%s" % (
            r.get("gm"), r.get("colour"), r.get("month"), r.get("qty"), r.get("decision"), r.get("finalApprovedQty")))
        self.send_json({"ok": True, "id": r["id"], "reqId": r["reqId"], "seq": seq, "approval": r.get("approval")})

    def api_DELETE_requests(self, parts, q):
        if not self.planner_ok():
            return self.error_json(403, "Only planners and admins can do this")
        date = self._date(q)
        with LOCK:
            _, seq = read_requests(date)
            seq += 1
            STORE.append_log("requests", {"type": "clear", "seq": seq, "by": self.user(), "at": _now()}, date)
        _audit(self.user(), "clear-requests", date)
        self.send_json({"ok": True, "seq": seq})

    # ---- /api/feedback  (everyone sends; admins see all and set status)
    def _feedback_items(self):
        items, by_id = [], {}
        for rec in STORE.read_log("feedback"):
            if rec.get("type") == "feedback":
                it = dict(rec)
                it.pop("type", None)
                it.setdefault("status", "new")
                items.append(it)
                by_id[it["id"]] = it
            elif rec.get("type") == "status" and rec.get("id") in by_id:
                it = by_id[rec["id"]]
                it["status"] = rec.get("status") or it["status"]
                it["note"] = rec.get("note", it.get("note", ""))
                it["updatedBy"], it["updatedAt"] = rec.get("byName") or rec.get("by"), rec.get("at")
        items.sort(key=lambda x: x.get("at") or "", reverse=True)
        return items

    def api_GET_feedback(self, parts, q):
        with LOCK:
            items = self._feedback_items()
        if (q.get("mine") or [""])[0] or not self.has("admin"):
            items = [x for x in items if x.get("by") == self.me["email"]]
        self.send_json({"items": items})

    def api_POST_feedback(self, parts, q):
        body = self.read_json_body()
        if parts:
            if not self.has("admin"):
                return self.error_json(403, "Admin only")
            fid = parts[0]
            status = str(body.get("status") or "")
            if status not in ("new", "in progress", "closed"):
                return self.error_json(400, "status must be new, in progress or closed")
            with LOCK:
                if fid not in {x["id"] for x in self._feedback_items()}:
                    return self.error_json(404, "No such feedback")
                STORE.append_log("feedback", {"type": "status", "id": fid, "status": status,
                                              "note": str(body.get("note") or "")[:1000], "by": self.me["email"],
                                              "byName": self.me["name"], "at": _now()})
            _audit(self.user(), "feedback-status", "%s %s" % (fid, status))
            return self.send_json({"ok": True})
        msg = str(body.get("message") or "").strip()
        if not msg:
            return self.error_json(400, "Please write your feedback")
        rating = body.get("rating")
        rating = int(rating) if isinstance(rating, (int, float)) and 1 <= int(rating) <= 5 else None
        with LOCK:
            fid = _next_number("feedback", "FB")
            rec = {"type": "feedback", "id": fid, "at": _now(), "by": self.me["email"], "byName": self.me["name"],
                   "roles": self.me.get("roles", []), "kind": str(body.get("kind") or "")[:40],
                   "screen": str(body.get("screen") or "")[:60], "article": str(body.get("article") or "")[:120],
                   "message": msg[:2000], "rating": rating, "view": str(body.get("view") or "")[:20], "status": "new"}
            STORE.append_log("feedback", rec)
        _audit(self.user(), "feedback", fid)
        self.send_json({"ok": True, "id": fid})

    # ---- /api/ai/<chat|insights>
    def api_POST_ai(self, parts, q):
        kind = parts[0] if parts else ""
        body = self.read_json_body()
        if kind == "chat":
            if not CFG.get("anthropic_api_key"):
                return self.error_json(503, "AI chat is not enabled on this server (no anthropic_api_key in config.json).")
            status, data = anthropic_call(
                str(body.get("system") or "")[:400000],
                body.get("messages") or [],
                min(int(body.get("max_tokens") or 1000), 4000),
            )
            _audit(self.user(), "ai-chat", "status=%s" % status)
            return self.send_json(data, status)
        if kind == "insights":
            prompt = str(body.get("prompt") or "")[:400000]
            if not (CFG.get("groq_api_key") or CFG.get("anthropic_api_key")):
                return self.error_json(503, "AI Insights is not enabled on this server (add anthropic_api_key or groq_api_key in config.json).")
            status, data = insights_call(prompt)
            _audit(self.user(), "ai-insights", "status=%s provider=%s" % (status, data.get("_provider", "?")))
            return self.send_json(data, status)
        self.error_json(404, "Unknown AI endpoint")


def backup_loop():
    """Nightly copy of the data folder (default 02:00, keep 30 days)."""
    while True:
        try:
            hour = int(CFG.get("backup_hour") if CFG.get("backup_hour") is not None else 2)
            if hour >= 0 and datetime.now().hour >= hour:
                today = datetime.now().strftime("%Y%m%d")
                if not any(b["name"].startswith("vmm-data-" + today) for b in acc.list_backups(CFG["backup_dir"])):
                    with BACKUP_LOCK:
                        name = storage.backup_to_zip(STORE, CFG["backup_dir"], int(CFG.get("backup_keep_days") or 30), TMP_DIR)
                    _audit("system", "backup", name)
        except Exception:
            log.exception("Backup failed")
        time.sleep(600)


def main():
    init_store(wait_seconds=900)  # at boot the database service may start after the tool
    migrate_flat_datasets()
    migrate_ids()
    prune_history()
    threading.Thread(target=backup_loop, daemon=True).start()
    addr = (CFG["host"], int(CFG["port"]))
    httpd = ThreadingHTTPServer(addr, Handler)
    httpd.daemon_threads = True
    log.info("VMM PO Governance server %s on http://%s:%s  (storage: %s, users: %d, admin: %s, AI chat %s, AI insights %s)",
             VERSION, addr[0], addr[1], STORE.describe(), len(ACC.users), CFG.get("admin_email"),
             "on" if CFG.get("anthropic_api_key") else "off",
             "on" if (CFG.get("groq_api_key") or CFG.get("anthropic_api_key")) else "off")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        httpd.server_close()


if __name__ == "__main__":
    if "--test-ai" in sys.argv:
        sys.exit(test_ai())
    if "--check" in sys.argv:
        # Prints where the data lives and what is in it (used after installing).
        init_store(wait_seconds=30)
        m = manifest()
        days = STORE.log_days("requests")
        print("Storage      :", STORE.describe())
        print("Users        : %d (%d active, admins: %s)" % (len(ACC.users), sum(1 for u in ACC.users.values() if u.get("active")),
              ", ".join(e for e, u in ACC.users.items() if "admin" in u.get("roles", []))))
        print("Current files: %d  (%s)" % (len(m["datasets"]), ", ".join("%s=%s rows" % (k, e.get("rows")) for k, e in sorted(m["datasets"].items()))))
        print("File versions: %d stored" % len(STORE.list_blobs("ds")))
        print("Tool 1 days  : %d (%s .. %s)" % (len(days), days[0] if days else "-", days[-1] if days else "-"))
        print("Saved runs   : %d" % len([1 for b in STORE.list_blobs("run") if not b[2]]))
        print("Audit lines  : %d" % len(STORE.audit_read()))
        sys.exit(0)
    if "--reset-admin" in sys.argv:
        # Emergency: admin locked out or forgot the password.
        init_store(wait_seconds=30)
        email = acc.norm_email(CFG.get("admin_email"))
        ACC.reset_password(email, "console")
        ACC.update_user(email, {"active": True, "unlock": True, "roles": ["admin", "planner"]}, "console")
        print("Admin %s reset: sign in with the default password and choose a new one." % email)
        sys.exit(0)
    main()
