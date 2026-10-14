"""
User accounts, sessions, SCR-based user sync, MC scope, and backups for the
VMM PO Governance server. Standard library only.

Roles
  admin     everything + user management (seeded: see ADMIN_EMAIL in config)
  planner   upload data, parameters, Tool 2, view everything
  cathead   raise Tool 1 requests, approve Watch List items for own MCs
  buyer     raise Tool 1 requests, see own requests / own MCs
"""
import base64
import gzip
import hashlib
import hmac
import json
import os
import re
import secrets
import threading
import time
import zipfile
from datetime import datetime, timezone

ROLES = ("admin", "planner", "cathead", "buyer")
ROLE_LABEL = {"admin": "Admin", "planner": "Planner", "cathead": "Category Head", "buyer": "Buyer"}
EMAIL_RE = re.compile(r"[A-Za-z0-9._%+'-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")
PBKDF2_ITER = 200_000


def _now_iso():
    return datetime.now(timezone.utc).astimezone().isoformat(timespec="seconds")


def norm_email(e):
    return str(e or "").strip().lower()


def name_from_email(email):
    """sukhdev.singh@x.com -> Sukhdev Singh"""
    local = norm_email(email).split("@")[0]
    parts = [p for p in re.split(r"[._\d-]+", local) if p]
    return " ".join(p.capitalize() for p in parts) or email


def nkey(v):
    """Same normalisation as the page's _n(): trim, drop trailing .0, lowercase."""
    s = str(v if v is not None else "").strip()
    if s.endswith(".0"):
        s = s[:-2]
    return s.lower()


def hash_password(pw, salt=None):
    salt = salt or secrets.token_bytes(16)
    dk = hashlib.pbkdf2_hmac("sha256", pw.encode("utf-8"), salt, PBKDF2_ITER)
    return base64.b64encode(salt).decode() + "$" + base64.b64encode(dk).decode()


def check_password(pw, stored):
    try:
        s, h = stored.split("$", 1)
        salt = base64.b64decode(s)
        dk = hashlib.pbkdf2_hmac("sha256", pw.encode("utf-8"), salt, PBKDF2_ITER)
        return hmac.compare_digest(base64.b64encode(dk).decode(), h)
    except Exception:
        return False


class Accounts:
    def __init__(self, store, cfg, audit):
        """store = storage.FileStore or storage.PgStore (users, sessions and
        the SCR column mapping are documents in it)."""
        self.store = store
        self.cfg = cfg
        self.audit = audit
        self.lock = threading.RLock()
        self.users = store.get_doc("users", {}) or {}
        self._mtime = self._users_mtime()
        self.sessions = store.get_doc("sessions", {}) or {}
        self._scope_cache = (None, None)
        self._seed_admin()

    # ── persistence ───────────────────────────────────────────────
    def _users_mtime(self):
        try:
            return self.store.doc_stamp("users")
        except Exception:
            return 0

    def _maybe_reload(self):
        """Pick up changes made outside the server (e.g. reset-admin.bat)."""
        m = self._users_mtime()
        if m != self._mtime:
            with self.lock:
                self.users = self.store.get_doc("users", self.users) or self.users
                self._mtime = m

    def save_users(self):
        with self.lock:
            self.store.put_doc("users", self.users)
            self._mtime = self._users_mtime()

    def save_sessions(self):
        with self.lock:
            self.store.put_doc("sessions", self.sessions)

    # ── users ─────────────────────────────────────────────────────
    def default_password(self):
        return str(self.cfg.get("default_password") or "1234")

    def _seed_admin(self):
        admin = norm_email(self.cfg.get("admin_email") or "kosaraju.srihari@vishalwholesale.co.in")
        with self.lock:
            u = self.users.get(admin)
            if not u:
                self.users[admin] = self._new_user(admin, name_from_email(admin), ["admin", "planner"], "system")
                self.save_users()
            elif "admin" not in u["roles"]:
                u["roles"] = sorted(set(u["roles"]) | {"admin", "planner"}, key=ROLES.index)
                self.save_users()

    def _new_user(self, email, name, roles, by, source="manual"):
        return {
            "email": email, "name": name or name_from_email(email),
            "roles": [r for r in ROLES if r in roles], "active": True,
            "pw": hash_password(self.default_password()), "mustChange": True,
            "failed": 0, "lockedUntil": 0, "createdAt": _now_iso(), "createdBy": by,
            "source": source, "lastLogin": None,
        }

    def public(self, u):
        d = {k: u.get(k) for k in ("email", "name", "roles", "active", "mustChange", "createdAt", "createdBy",
                                    "source", "lastLogin", "lockedUntil")}
        d["locked"] = (u.get("lockedUntil") or 0) > time.time()
        return d

    def add_user(self, email, name, roles, by, source="manual"):
        email = norm_email(email)
        if not EMAIL_RE.fullmatch(email):
            raise ValueError("Not a valid email: %s" % email)
        roles = [r for r in roles if r in ROLES]
        if not roles:
            raise ValueError("Pick at least one role")
        with self.lock:
            if email in self.users:
                raise ValueError("%s already exists" % email)
            self.users[email] = self._new_user(email, name, roles, by, source)
            self.save_users()
        self.audit(by, "user-add", "%s roles=%s" % (email, ",".join(roles)))
        return self.users[email]

    def update_user(self, email, changes, by):
        """Validates every change first; nothing is applied if any check fails."""
        email = norm_email(email)
        with self.lock:
            u = self.users.get(email)
            if not u:
                raise KeyError(email)
            me = email == norm_email(by)
            new = dict(u)
            if "name" in changes:
                new["name"] = str(changes["name"]).strip()[:80] or u["name"]
            if "roles" in changes:
                roles = [r for r in ROLES if r in (changes["roles"] or [])]
                if not roles:
                    raise ValueError("A user needs at least one role")
                if me and "admin" in u["roles"] and "admin" not in roles:
                    raise ValueError("You can't remove your own admin role")
                new["roles"] = roles
            if "active" in changes:
                if me and not changes["active"]:
                    raise ValueError("You can't deactivate yourself")
                new["active"] = bool(changes["active"])
            if changes.get("unlock"):
                new["failed"], new["lockedUntil"] = 0, 0
            admins = sum(1 for e, x in self.users.items()
                         if (new if e == email else x).get("active") and "admin" in (new if e == email else x).get("roles", []))
            if admins == 0:
                raise ValueError("At least one active admin must remain")
            u.clear(); u.update(new)
            if not u["active"]:
                self.end_sessions(email)
            self.save_users()
        self.audit(by, "user-update", "%s %s" % (email, json.dumps({k: v for k, v in changes.items()})))
        return u

    def _admins_left(self):
        return sum(1 for u in self.users.values() if u["active"] and "admin" in u["roles"])

    def reset_password(self, email, by):
        email = norm_email(email)
        with self.lock:
            u = self.users[email]
            u["pw"] = hash_password(self.default_password())
            u["mustChange"], u["failed"], u["lockedUntil"] = True, 0, 0
            self.save_users()
            self.end_sessions(email)
        self.audit(by, "password-reset", email)

    def delete_user(self, email, by):
        """Removes a login completely (requests and history keep the name)."""
        email = norm_email(email)
        with self.lock:
            if email not in self.users:
                raise KeyError(email)
            del self.users[email]
            self.save_users()
            self.end_sessions(email)
        self.audit(by, "user-delete", email)

    def validate_new_password(self, email, pw):
        n = int(self.cfg.get("password_min_length") or 8)
        if len(pw) < n:
            return "Password must be at least %d characters" % n
        if pw == self.default_password():
            return "Choose a password different from the default"
        if pw.lower() in (email.lower(), email.split("@")[0].lower()):
            return "Password can't be your email or user name"
        if not (re.search(r"[A-Za-z]", pw) and re.search(r"\d", pw)):
            return "Use at least one letter and one number"
        return None

    def change_password(self, email, old, new):
        email = norm_email(email)
        with self.lock:
            u = self.users.get(email)
            if not u or not check_password(old, u["pw"]):
                return "Current password is incorrect"
            err = self.validate_new_password(email, new)
            if err:
                return err
            u["pw"] = hash_password(new)
            u["mustChange"] = False
            self.save_users()
        self.audit(email, "password-change", "")
        return None

    # ── login / sessions ──────────────────────────────────────────
    def login(self, email, pw, ip):
        email = norm_email(email)
        self._maybe_reload()
        max_fail = int(self.cfg.get("max_failed_logins") or 5)
        lock_min = int(self.cfg.get("lockout_minutes") or 15)
        with self.lock:
            u = self.users.get(email)
            if not u or not u.get("active"):
                self.audit(email or "?", "login-fail", "unknown or inactive user ip=%s" % ip)
                return None, "Email or password is incorrect"
            if (u.get("lockedUntil") or 0) > time.time():
                mins = int((u["lockedUntil"] - time.time()) // 60) + 1
                return None, "Too many wrong attempts. Try again in %d minute(s), or ask the admin to unlock you." % mins
            if not check_password(pw, u["pw"]):
                u["failed"] = (u.get("failed") or 0) + 1
                msg = "Email or password is incorrect"
                if u["failed"] >= max_fail:
                    u["lockedUntil"] = time.time() + lock_min * 60
                    u["failed"] = 0
                    msg = "Too many wrong attempts — locked for %d minutes." % lock_min
                    self.audit(email, "locked", "ip=%s" % ip)
                self.save_users()
                self.audit(email, "login-fail", "ip=%s" % ip)
                return None, msg
            u["failed"], u["lockedUntil"] = 0, 0
            u["lastLogin"] = _now_iso()
            self.save_users()
            token = secrets.token_urlsafe(32)
            self.sessions[token] = {"email": email, "created": time.time(), "seen": time.time(), "ip": ip}
            self._prune_sessions()
            self.save_sessions()
        self.audit(email, "login", "ip=%s" % ip)
        return token, None

    def _prune_sessions(self):
        idle = float(self.cfg.get("session_idle_hours") or 12) * 3600
        now = time.time()
        for t in [t for t, s in self.sessions.items() if now - s.get("seen", 0) > idle]:
            del self.sessions[t]

    def session_user(self, token):
        if not token:
            return None
        idle = float(self.cfg.get("session_idle_hours") or 12) * 3600
        self._maybe_reload()
        with self.lock:
            s = self.sessions.get(token)
            if not s:
                return None
            if time.time() - s.get("seen", 0) > idle:
                del self.sessions[token]
                self.save_sessions()
                return None
            u = self.users.get(s["email"])
            if not u or not u.get("active"):
                return None
            if time.time() - s["seen"] > 300:  # persist "last seen" at most every 5 min
                s["seen"] = time.time()
                self.save_sessions()
            else:
                s["seen"] = time.time()
            return u

    def logout(self, token):
        with self.lock:
            s = self.sessions.pop(token, None)
            self.save_sessions()
        if s:
            self.audit(s["email"], "logout", "")

    def end_sessions(self, email, keep=None):
        with self.lock:
            for t in [t for t, s in self.sessions.items() if s["email"] == email and t != keep]:
                del self.sessions[t]
            self.save_sessions()

    # ── SCR products → users, scope, approvers ────────────────────
    def mapping(self):
        return self.store.get_doc("scr_mapping", {}) or {}

    def save_mapping(self, m, by):
        with self.lock:
            self.store.put_doc("scr_mapping", m)
        self._scope_cache = (None, None)
        self.audit(by, "scr-mapping", json.dumps(m))

    @staticmethod
    def _emails(v):
        return [norm_email(e) for e in EMAIL_RE.findall(str(v or ""))]

    def analyse_scr(self, rows):
        """Which columns hold emails, and which role each one most likely is."""
        if not rows:
            return {"columns": [], "brandCol": None, "mcCol": None, "mrpCol": None}
        headers = list(rows[0].keys())
        for r in rows[:50]:
            for h in r.keys():
                if h not in headers:
                    headers.append(h)
        sample = rows[:500]
        cols = []
        for h in headers:
            vals = [r.get(h) for r in sample if str(r.get(h) or "").strip()]
            if not vals:
                continue
            hits = sum(1 for v in vals if EMAIL_RE.search(str(v)))
            if hits / len(vals) < 0.5:
                continue
            hl = re.sub(r"[^a-z]", " ", h.lower())
            lvl = re.sub(r"[^a-z0-9]", "", h.lower())
            if lvl in ("level1", "l1", "level1email", "level1emailid"):
                guess = "buyer"      # Level 1: Buyer / Sr Buyer / ACM / BFT
            elif lvl in ("level2", "l2", "level2email", "level2emailid"):
                guess = "buyer"      # Level 2: own MCs, no approvals yet (layer kept for a later workflow)
            elif lvl in ("level3", "l3", "level3email", "level3emailid"):
                guess = "cathead"    # Level 3: Watch List approvals park here
            elif "division" in hl or re.search(r"\bdiv\b", hl) or "dh" == hl.strip():
                guess = "skip"
            elif "buyer" in hl or "buying" in hl:
                guess = "buyer"
            elif "category" in hl or re.search(r"\bcat\b", hl) or "ch" == hl.strip():
                guess = "cathead"
            else:
                guess = "skip"
            name_col = None
            base = "buyer" if guess == "buyer" else "cat"
            for h2 in headers:
                h2l = h2.lower()
                if h2 != h and "name" in h2l and base in h2l:
                    name_col = h2
                    break
            distinct = len({e for v in vals for e in self._emails(v)})
            cols.append({"column": h, "guess": guess, "nameColumn": name_col, "distinctEmails": distinct,
                         "example": str(vals[0])[:80]})

        def find(*cands):
            for c in cands:
                for h in headers:
                    if re.sub(r"[^a-z]", "", h.lower()) == c:
                        return h
            return None
        return {"columns": cols, "brandCol": find("brandtype", "brand"), "mcCol": find("mccd", "mccode", "mc"),
                "mrpCol": find("mrp", "mrpvalue")}

    def scr_people(self, rows, mapping=None):
        """{email: {"roles": set, "name": str, "keys": set(brand|mc|mrp)}} from the SCR rows."""
        mapping = mapping if mapping is not None else self.mapping()
        info = self.analyse_scr(rows)
        roles_by_col = {c["column"]: c["guess"] for c in info["columns"]}
        roles_by_col.update(mapping.get("columns") or {})  # admin's choices win
        name_cols = {c["column"]: c.get("nameColumn") for c in info["columns"]}
        b, m, p = (mapping.get("brandCol") or info["brandCol"], mapping.get("mcCol") or info["mcCol"],
                   mapping.get("mrpCol") or info["mrpCol"])
        people = {}
        for r in rows:
            key = "|".join([nkey(r.get(b)) if b else "", nkey(r.get(m)) if m else "", nkey(r.get(p)) if p else ""])
            for col, role in roles_by_col.items():
                if role not in ("buyer", "cathead"):
                    continue
                emails = self._emails(r.get(col))
                names = [x.strip() for x in re.split(r"[,;/]", str(r.get(name_cols.get(col)) or ""))] if name_cols.get(col) else []
                for i, e in enumerate(emails):
                    d = people.setdefault(e, {"roles": set(), "name": "", "keys": set(), "catheadKeys": set()})
                    d["roles"].add(role)
                    d["keys"].add(key)
                    if role == "cathead":
                        d["catheadKeys"].add(key)
                    if not d["name"] and i < len(names) and names[i]:
                        d["name"] = names[i]
        return people

    def sync_from_scr(self, rows, by):
        """Create logins for every buyer / category head in the SCR file.
        Never deletes or demotes anyone; existing users only gain roles."""
        people = self.scr_people(rows)
        added, updated = [], []
        with self.lock:
            for email, d in people.items():
                u = self.users.get(email)
                if not u:
                    self.users[email] = self._new_user(email, d["name"], d["roles"], by, "scr")
                    added.append(email)
                else:
                    new_roles = [r for r in ROLES if r in set(u["roles"]) | d["roles"]]
                    if new_roles != u["roles"]:
                        u["roles"] = new_roles
                        updated.append(email)
            if added or updated:
                self.save_users()
        self._scope_cache = (None, None)
        self.audit(by, "scr-sync", "added=%d updated=%d" % (len(added), len(updated)))
        return {"added": added, "updated": updated, "found": len(people)}

    def scope_index(self, scr_loader):
        """(version, {email: {"keys": set, "catheadKeys": set}}, {key: [cathead emails]}) — cached per SCR version."""
        ver, rows = scr_loader()
        if self._scope_cache[0] == ver and ver is not None:
            return self._scope_cache[1]
        people = self.scr_people(rows or [])
        approvers = {}
        for e, d in people.items():
            for k in d["catheadKeys"]:
                approvers.setdefault(k, []).append(e)
        idx = (people, approvers)
        self._scope_cache = (ver, idx)
        return idx


# ── backups ────────────────────────────────────────────────────────
def make_backup(data_dir, backup_dir, keep_days):
    os.makedirs(backup_dir, exist_ok=True)
    name = "vmm-data-%s.zip" % datetime.now().strftime("%Y%m%d-%H%M%S")
    path = os.path.join(backup_dir, name)
    tmp = path + ".tmp"
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as z:
        for root, _, files in os.walk(data_dir):
            for f in files:
                if f.endswith(".tmp"):
                    continue
                full = os.path.join(root, f)
                z.write(full, os.path.relpath(full, data_dir))
    os.replace(tmp, path)
    cutoff = time.time() - keep_days * 86400
    for f in os.listdir(backup_dir):
        p = os.path.join(backup_dir, f)
        if f.startswith("vmm-data-") and f.endswith(".zip") and os.path.getmtime(p) < cutoff:
            os.remove(p)
    return name


def list_backups(backup_dir):
    if not os.path.isdir(backup_dir):
        return []
    out = []
    for f in sorted(os.listdir(backup_dir), reverse=True):
        if f.startswith("vmm-data-") and f.endswith(".zip"):
            p = os.path.join(backup_dir, f)
            out.append({"name": f, "bytes": os.path.getsize(p),
                        "at": datetime.fromtimestamp(os.path.getmtime(p)).astimezone().isoformat(timespec="seconds")})
    return out


def read_rows_gz(path):
    with gzip.open(path, "rb") as f:
        return json.loads(f.read().decode("utf-8")).get("rows") or []
