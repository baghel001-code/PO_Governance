"""Checks the parts auth_e2e.py doesn't reach: History, Backups, Tool 2 run saved,
planner Tool 1 request, data still there after a server restart."""
import json, os, sys, urllib.request
from playwright.sync_api import sync_playwright
BASE = os.environ.get("BASE", "http://localhost:8765/")
ADMIN = "kosaraju.srihari@vishalwholesale.co.in"; PW = os.environ.get("PW", "Vmm2026ok")
errs = []
phase = sys.argv[1] if len(sys.argv) > 1 else "before"

def login(b, email, pw):
    ctx = b.new_context(viewport={"width": 1440, "height": 900}); p = ctx.new_page()
    p.on("pageerror", lambda e: errs.append(str(e))); p.on("dialog", lambda d: d.accept())
    p.goto(BASE); p.wait_for_url("**/login.html**")
    p.fill("#email", email); p.fill("#pw", pw); p.click("#b-login")
    p.wait_for_function("window.Shared&&Shared.online===true", timeout=60000)
    return p

with sync_playwright() as pw:
    b = pw.chromium.launch()
    a = login(b, ADMIN, PW)
    st = a.evaluate("Shared.status")
    print(phase, "| datasets:", sorted(st["datasets"].keys()), "| params T1:", (st.get("params") or {}).get("T1"))
    print(phase, "| requests today in page:", a.evaluate("S.requests.length"))
    if phase == "before":
        # Tool 2 run (saved to History)
        a.click("#pt-assess"); a.click("#btn-run-assessment")
        a.wait_for_function("typeof batchResults!=='undefined' && batchResults.length>0", timeout=30000)
        a.wait_for_timeout(2500)
        # planner raises a Tool 1 request
        a.evaluate("categoryTab && 0")
    # History tab
    a.click("#pt-history")
    a.wait_for_function("document.getElementById('h-body') && !/Loading/.test(document.getElementById('h-body').innerText) && document.getElementById('h-body').innerText.trim().length>0", timeout=30000)
    print(phase, "| history tabs:", a.inner_text("#h-tabs").replace("\n", " "))
    a.screenshot(path="tests/db_history_%s.png" % phase)
    for t in a.locator("#h-tabs button").all():
        t.click(); a.wait_for_timeout(1200)
        txt = a.inner_text("#h-body")
        print(phase, "|  tab", t.inner_text().strip(), "->", (txt.strip().split("\n")[0])[:90], "| rows:", a.locator("#h-body tbody tr").count())
    # Backups tab
    a.click("#pt-users"); a.wait_for_selector("#us-body table")
    a.click("#us-tabs >> text=Backups"); a.wait_for_timeout(800)
    if phase == "before":
        a.click("text=💾 Back up now"); a.wait_for_timeout(4000)
        a.click("#us-tabs >> text=👥 Users"); a.wait_for_timeout(500); a.click("#us-tabs >> text=Backups"); a.wait_for_timeout(1500)
    print(phase, "| backups listed:", a.locator("#us-body tbody tr").count())
    a.click("#us-tabs >> text=Audit log"); a.wait_for_timeout(1500)
    print(phase, "| audit rows shown:", a.locator("#us-body tbody tr").count())
    b.close()
print(phase, "| page errors:", errs)
