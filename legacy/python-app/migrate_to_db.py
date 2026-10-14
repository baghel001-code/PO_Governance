#!/usr/bin/env python3
"""
Moves the tool's data between the data folder and PostgreSQL.

  python migrate_to_db.py                 data folder  ->  PostgreSQL, then switch config.json to PostgreSQL
  python migrate_to_db.py --force         same, but first empties a database that already has data
  python migrate_to_db.py --export DIR    PostgreSQL   ->  files in DIR (same layout as the data folder)
  python migrate_to_db.py --use-files     switch config.json back to the data folder (rollback)
  python migrate_to_db.py --use-db        switch config.json to PostgreSQL

The data folder itself is never changed or deleted, so it stays as the fallback.
Users and passwords are copied exactly (passwords are stored as hashes and are
not changed). Restart the tool after switching.
"""
import json
import os
import sys
import time

BASE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, BASE)
sys.path.insert(0, os.path.join(BASE, "pylib"))
import storage  # noqa: E402

CONFIG = os.path.join(BASE, "config.json")


def say(msg=""):
    print(msg, flush=True)


def fail(msg):
    say("")
    say("ERROR: " + msg)
    say("Nothing was switched - the tool still uses its previous storage. Send a screenshot if you need help.")
    sys.exit(1)


def load_cfg():
    with open(CONFIG, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def save_cfg(cfg):
    with open(CONFIG + ".tmp", "w", encoding="utf-8") as f:
        json.dump(cfg, f, ensure_ascii=False, indent=2)
    os.replace(CONFIG + ".tmp", CONFIG)


def data_dir(cfg):
    d = os.environ.get("VMM_DATA_DIR") or cfg.get("data_dir") or "data"
    return d if os.path.isabs(d) else os.path.join(BASE, d)


def set_storage(kind):
    cfg = load_cfg()
    cfg["storage"] = kind
    save_cfg(cfg)
    say("config.json: storage = %s. Restart the tool for it to take effect." % kind)


def main():
    cfg = load_cfg()
    if "--use-files" in sys.argv:
        return set_storage("files")
    if "--use-db" in sys.argv:
        return set_storage("postgres")
    db = cfg.get("db") or {}
    if not db.get("password"):
        fail("config.json has no database login yet - run setup_db.py first.")
    try:
        pg = storage.PgStore(db, wait_seconds=30)
    except Exception as e:
        fail("Could not connect to PostgreSQL (%s). Is the VMM-PostgreSQL service running?" % e)

    if "--export" in sys.argv:
        i = sys.argv.index("--export")
        out = sys.argv[i + 1] if len(sys.argv) > i + 1 else os.path.join(BASE, "export-" + time.strftime("%Y%m%d-%H%M%S"))
        if os.path.exists(out) and os.listdir(out):
            fail("%s is not empty - choose a new folder." % out)
        say("Exporting PostgreSQL -> %s" % out)
        dst = storage.FileStore(out)
        counts = storage.copy_store(pg, dst, say)
        diffs = storage.verify_copy(pg, dst)
        if diffs:
            fail("Export check found differences: %s" % "; ".join(diffs[:10]))
        say("Export complete and checked: %s" % json.dumps(counts))
        say("To use it: stop the tool, point data_dir in config.json at this folder (or copy it over data),")
        say("run  migrate_to_db.py --use-files , then start the tool.")
        return

    src_dir = data_dir(cfg)
    if not os.path.isdir(src_dir):
        fail("Data folder not found: %s" % src_dir)
    say("Source : data folder %s" % src_dir)
    say("Target : %s" % pg.describe())

    # Bring old file layouts up to date first (same steps the server does at start).
    import server
    server.STORE = storage.FileStore(src_dir)
    server.migrate_flat_datasets()
    server.migrate_ids()
    src = server.STORE

    if not pg.is_empty():
        if "--force" not in sys.argv:
            fail("The database already contains data. Run again with --force to replace it with the data folder.")
        say("Emptying the database first (--force) ...")
        pg.truncate_all()

    say("Copying ...")
    t0 = time.time()
    counts = storage.copy_store(src, pg, say)
    say("Checking every record against the data folder ...")
    pg.close()
    pg = storage.PgStore(db)
    diffs = storage.verify_copy(src, pg)
    if diffs:
        fail("Check found differences: %s" % "; ".join(diffs[:10]))
    users = src.get_doc("users", {}) or {}
    say("")
    say("Copied and checked in %.0f s:" % (time.time() - t0))
    say("  users            : %d" % len(users))
    say("  current files    : %d" % len((src.get_doc("manifest", {}) or {}).get("datasets", {})))
    say("  file versions    : %d" % counts.get("blob:ds", 0))
    say("  Tool 1 days      : %d (%d events)" % (counts.get("request_days", 0), counts.get("log:requests", 0)))
    say("  saved Tool 2 runs: %d" % counts.get("blob:run", 0))
    say("  audit lines      : %d" % counts.get("audit", 0))
    cfg = load_cfg()
    cfg["storage"] = "postgres"
    save_cfg(cfg)
    say("")
    say("config.json switched to PostgreSQL. The data folder was not changed (kept as fallback).")
    say("Restart the tool now.")


if __name__ == "__main__":
    main()
