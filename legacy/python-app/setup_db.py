#!/usr/bin/env python3
"""
Sets up the bundled PostgreSQL for VMM PO Governance (run once, as administrator).

  python setup_db.py              create the database (safe to run again)
  python setup_db.py --status     show whether PostgreSQL is running
  python setup_db.py --uninstall  remove the PostgreSQL Windows service (keeps the data)

What it does
  1. Creates the database cluster in .\\pgdata (PostgreSQL 17 from .\\pgsql).
     Listens on localhost only, port 5433 - nothing is exposed to the network.
  2. Windows: registers it as the service "VMM-PostgreSQL" (starts automatically
     with the server) and starts it.
  3. Creates the database "pogov" and the login "vmm_app" with a random password.
  4. Writes the connection details into config.json ("db" section).
     It does NOT switch the tool over - migrate_to_db.py does that.
"""
import json
import os
import secrets
import socket
import subprocess
import sys
import time

BASE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(BASE, "pylib"))
WIN = os.name == "nt"
PG_HOME = os.path.join(BASE, "pgsql")
PG_BIN = os.path.join(PG_HOME, "bin")
PG_DATA = os.environ.get("VMM_PGDATA") or os.path.join(BASE, "pgdata")
SERVICE = "VMM-PostgreSQL"
CONFIG = os.path.join(BASE, "config.json")


def exe(name):
    return os.path.join(PG_BIN, name + (".exe" if WIN else ""))


def say(msg):
    print(msg, flush=True)


def fail(msg):
    say("")
    say("ERROR: " + msg)
    say("Send a screenshot of this window if you need help.")
    sys.exit(1)


def load_cfg():
    with open(CONFIG, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def save_cfg(cfg):
    tmp = CONFIG + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(cfg, f, ensure_ascii=False, indent=2)
    os.replace(tmp, CONFIG)


def run(cmd, check=True, env=None, quiet=False):
    if not quiet:
        say("  > " + " ".join('"%s"' % c if " " in c else c for c in cmd))
    p = subprocess.run(cmd, capture_output=True, text=True, env=env)
    out = (p.stdout or "") + (p.stderr or "")
    if check and p.returncode != 0:
        fail("Command failed (%s):\n%s" % (p.returncode, out.strip()))
    return p.returncode, out


def port_open(port, host="127.0.0.1"):
    s = socket.socket()
    s.settimeout(1)
    try:
        s.connect((host, port))
        return True
    except OSError:
        return False
    finally:
        s.close()


def wait_port(port, seconds=90):
    end = time.time() + seconds
    while time.time() < end:
        if port_open(port):
            return True
        time.sleep(1)
    return False


def pg_env():
    env = dict(os.environ)
    if not WIN:
        env["LD_LIBRARY_PATH"] = os.path.join(PG_HOME, "lib")
    return env


def service_state():
    if not WIN:
        return None
    code, out = run(["sc", "query", SERVICE], check=False, quiet=True)
    if code != 0:
        return "missing"
    return "running" if "RUNNING" in out else "stopped"


def start_server(port):
    if port_open(port):
        say("  PostgreSQL is already running on port %d." % port)
        return
    if WIN:
        st = service_state()
        if st == "missing":
            # Service runs as NetworkService: give it access to the data and program folders.
            run(["icacls", PG_DATA, "/grant", "*S-1-5-20:(OI)(CI)F", "/T", "/Q"])
            run(["icacls", PG_HOME, "/grant", "*S-1-5-20:(OI)(CI)RX", "/T", "/Q"])
            run([exe("pg_ctl"), "register", "-N", SERVICE, "-D", PG_DATA, "-S", "auto"])
            run(["sc", "description", SERVICE, "PostgreSQL database for VMM PO Governance (localhost:%d)" % port], check=False)
            run(["sc", "failure", SERVICE, "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/60000"], check=False)
        run(["sc", "start", SERVICE], check=False)
    else:
        run([exe("pg_ctl"), "-D", PG_DATA, "-l", os.path.join(PG_DATA, "server.log"), "start", "-w"], env=pg_env())
    if not wait_port(port):
        log_dir = os.path.join(PG_DATA, "log")
        tail = ""
        if os.path.isdir(log_dir):
            files = sorted(os.listdir(log_dir))
            if files:
                with open(os.path.join(log_dir, files[-1]), "r", encoding="utf-8", errors="replace") as f:
                    tail = "".join(f.readlines()[-15:])
        fail("PostgreSQL did not start on port %d.\n%s" % (port, tail))
    say("  PostgreSQL is running on localhost:%d." % port)


def init_cluster(admin_pw, port):
    if os.path.exists(os.path.join(PG_DATA, "PG_VERSION")):
        say("  Database folder already exists: %s (kept as it is)." % PG_DATA)
        return False
    if os.path.exists(PG_DATA) and os.listdir(PG_DATA):
        fail("%s exists but is not a PostgreSQL data folder. Move it away and run again." % PG_DATA)
    pwfile = os.path.join(BASE, "tmp", "pw.txt")
    os.makedirs(os.path.dirname(pwfile), exist_ok=True)
    with open(pwfile, "w", encoding="utf-8") as f:
        f.write(admin_pw + "\n")
    try:
        run([exe("initdb"), "-D", PG_DATA, "-U", "vmm_admin", "--pwfile=" + pwfile, "-A", "scram-sha-256",
             "-E", "UTF8", "--no-locale"], env=pg_env())
    finally:
        os.remove(pwfile)
    with open(os.path.join(PG_DATA, "postgresql.conf"), "a", encoding="utf-8") as f:
        f.write("\n# ---- VMM PO Governance ----\n")
        f.write("port = %d\n" % port)
        f.write("listen_addresses = 'localhost'\n")
        f.write("max_connections = 100\n")
        f.write("shared_buffers = 512MB\n")
        f.write("work_mem = 32MB\n")
        f.write("maintenance_work_mem = 256MB\n")
        f.write("logging_collector = on\n")
        f.write("log_directory = 'log'\n")
        f.write("log_filename = 'postgresql-%a.log'\n")
        f.write("log_truncate_on_rotation = on\n")
        f.write("log_rotation_age = 1d\n")
        f.write("timezone = 'Asia/Kolkata'\n")
    return True


def connect(user, pw, port, db="postgres"):
    import pg8000.native
    return pg8000.native.Connection(user, host="127.0.0.1", port=port, password=pw, database=db, timeout=30)


def main():
    if not os.path.exists(exe("initdb")):
        fail("PostgreSQL programs not found in %s - copy the whole package folder again." % PG_BIN)
    cfg = load_cfg()
    db = dict(cfg.get("db") or {})
    port = int(db.get("port") or 5433)
    if "--status" in sys.argv:
        say("Service : %s" % (service_state() or "n/a (not Windows)"))
        say("Port %d: %s" % (port, "open" if port_open(port) else "closed"))
        return
    if "--uninstall" in sys.argv:
        if WIN:
            run(["sc", "stop", SERVICE], check=False)
            time.sleep(3)
            run([exe("pg_ctl"), "unregister", "-N", SERVICE], check=False)
        else:
            run([exe("pg_ctl"), "-D", PG_DATA, "stop"], check=False, env=pg_env())
        say("PostgreSQL service removed. The data folder %s was kept." % PG_DATA)
        return

    say("Step 1/4  Database folder")
    admin_pw = db.get("admin_password") or secrets.token_urlsafe(18)
    created = init_cluster(admin_pw, port)
    if created:
        db["admin_user"], db["admin_password"] = "vmm_admin", admin_pw
        db.update(host="127.0.0.1", port=port)
        cfg["db"] = db
        save_cfg(cfg)  # keep the admin password even if a later step fails
    elif not db.get("admin_password"):
        fail("pgdata exists but config.json has no db.admin_password - cannot sign in to it.")

    say("Step 2/4  Start PostgreSQL" + (" (Windows service %s)" % SERVICE if WIN else ""))
    start_server(port)

    say("Step 3/4  Database 'pogov' and login 'vmm_app'")
    try:
        c = connect(db.get("admin_user") or "vmm_admin", db["admin_password"], port)
    except Exception as e:
        fail("Could not sign in to PostgreSQL as admin: %s" % e)
    app_pw = db.get("password") or secrets.token_urlsafe(18)
    if c.run("SELECT 1 FROM pg_roles WHERE rolname = 'vmm_app'"):
        c.run("ALTER ROLE vmm_app WITH LOGIN PASSWORD '%s'" % app_pw.replace("'", "''"))
        say("  Login vmm_app exists - password refreshed.")
    else:
        c.run("CREATE ROLE vmm_app WITH LOGIN PASSWORD '%s'" % app_pw.replace("'", "''"))
        say("  Login vmm_app created.")
    if c.run("SELECT 1 FROM pg_database WHERE datname = 'pogov'"):
        say("  Database pogov exists.")
    else:
        c.run("CREATE DATABASE pogov OWNER vmm_app ENCODING 'UTF8' TEMPLATE template0")
        say("  Database pogov created.")
    c.close()
    c = connect(db.get("admin_user") or "vmm_admin", db["admin_password"], port, "pogov")
    c.run("GRANT ALL ON SCHEMA public TO vmm_app")
    c.run("ALTER SCHEMA public OWNER TO vmm_app")
    c.close()

    say("Step 4/4  Save connection in config.json")
    db.update(host="127.0.0.1", port=port, name="pogov", user="vmm_app", password=app_pw)
    cfg["db"] = db
    cfg.setdefault("storage", "files")
    save_cfg(cfg)
    import storage
    st = storage.PgStore(db)  # creates the tables
    st.close()
    say("")
    say("PostgreSQL is ready (localhost:%d, database pogov). Next: migrate_to_db.py" % port)


if __name__ == "__main__":
    main()
