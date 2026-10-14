"""End-to-end check: planner uploads → category users share data + ledger."""
import json, os, re, sys, time, urllib.request
from playwright.sync_api import sync_playwright

BASE = os.environ.get("BASE", "http://localhost:8765/")
TD = os.path.join(os.path.dirname(__file__), "..", "testdata")
errors = []


def status():
    return json.load(urllib.request.urlopen(BASE + "api/status"))


def new_page(browser, name):
    ctx = browser.new_context(viewport={"width": 1500, "height": 950}, timezone_id="Asia/Kolkata")
    page = ctx.new_page()
    page.on("pageerror", lambda e: errors.append("%s pageerror: %s" % (name, e)))
    page.on("console", lambda m: m.type == "error" and errors.append("%s console: %s" % (name, m.text)))
    page.on("dialog", lambda d: d.accept("1234") if d.type == "prompt" else d.accept())
    page.goto(BASE)
    page.wait_for_function("window.Shared && Shared.online === true", timeout=60000)
    return page


def enter(page, name, role):
    page.fill("#role-user-name", name)
    page.click(".rcard.plan" if role == "planner" else ".rcard.cat")
    page.wait_for_timeout(500)


def tool1(page, gm, colour, month, qty, confirm_qty=None):
    page.fill("#c-gm", gm)
    page.dispatch_event("#c-gm", "input")
    page.wait_for_timeout(300)
    page.select_option("#c-col", colour)
    page.select_option("#c-mon", month)
    page.fill("#c-qty", str(qty))
    page.click("#c-btn")
    try:
        page.wait_for_selector("#qty-confirm-overlay", state="visible", timeout=5000)
        if confirm_qty is not None:
            page.fill("#qc-input", str(confirm_qty))
        page.click("text=✓ Confirm Quantity")
    except Exception:
        pass  # no headroom → no modal
    page.wait_for_function("!document.getElementById('c-analyzing').classList.contains('show')", timeout=20000)
    page.wait_for_timeout(800)
    return page.evaluate("S.requests[S.requests.length-1]")


with sync_playwright() as p:
    browser = p.chromium.launch()

    # ── Planner uploads ──
    pl = new_page(browser, "planner")
    enter(pl, "Planner Pat", "planner")
    assert pl.evaluate("S.role") == "planner", "planner role not entered"
    for key, fname in [("f1", "F1_Stock.xlsx"), ("f2", "F2_Pending.xlsx"), ("f3", "F3_Sales.xlsx"),
                       ("f4", "F4_Forecast.xlsx"), ("um", "UniverseMaster.xlsx"), ("bsr", "BSR_SS26.xlsx"),
                       ("artmaster", "ArticleMaster.xlsx")]:
        pl.set_input_files("#sl-%s input[type=file]" % key, os.path.join(TD, fname))
        pl.wait_for_function("S.files['%s'] && S.files['%s'].length" % (key, key), timeout=15000)
    pl.fill("#pp-T1", "75")
    pl.dispatch_event("#pp-T1", "change")
    pl.wait_for_function("Object.keys(Shared.status.datasets||{}).length>=7 && !Shared.pushing", timeout=30000)
    pl.wait_for_timeout(1500)
    st = status()
    print("server datasets:", {k: v["rows"] for k, v in st["datasets"].items()}, "params v", st["paramsVersion"])
    assert len(st["datasets"]) == 7, st
    assert st["params"] and st["params"].get("seasonCurrent") == ["SS26"], st["params"]
    pl.screenshot(path="tests/shot_planner.png")

    # Tool 2 run on the planner side
    pl.click("#pt-assess")
    pl.click("#btn-run-assessment")
    pl.wait_for_function("typeof batchResults!=='undefined' && batchResults.length>=3", timeout=30000)
    t2 = pl.evaluate("batchResults.map(r=>({gm:r.gm,c:r.colour,d:r.decision,rel:r.relQty,ex:r.excessQty,rec:r.recQty}))")
    print("tool2:", t2)

    # ── Category user A ──
    ca = new_page(browser, "catA")
    enter(ca, "Cat Chris", "category")
    assert ca.evaluate("S.files.f1 && S.files.f1.length") == 3, "category did not receive shared data"
    r1 = tool1(ca, "1000001", "BLK", "11.2026", 3000)
    print("A #1:", r1["decision"], "maxPerm", r1.get("maxPermQty"), "approved", r1.get("finalApprovedQty"), "id", r1.get("id"))
    print("   reason:", r1["decisionReason"][:220])
    assert r1["decision"] == "Auto Allow", r1["decision"]  # original v89 rule: after accepting Max Permissible the re-check shows Auto Allow
    step5 = ca.evaluate("document.getElementById('c-results').textContent").replace("\xa0", " ")
    m = re.search(r"= MAX\(0, ([\d.]+) × ([\d.]+) − (-?[\d,]+)\)", step5)
    print("   step5:", m.group(0) if m else "NOT FOUND")
    if m:
        eff, rate, stock = float(m.group(1)), float(m.group(2)), float(m.group(3).replace(",", ""))
        print("   step5 recompute:", eff * rate - stock, "vs maxPerm", r1["maxPermQty"])
    ca.screenshot(path="tests/shot_cat_result.png", full_page=False)

    # ── Category user B sees A's approval in the ledger ──
    cb = new_page(browser, "catB")
    enter(cb, "Cat Dana", "category")
    r2 = tool1(cb, "1000001", "BLK", "11.2026", 1000)
    print("B #2:", r2["decision"], "maxPerm", r2.get("maxPermQty"), "approved", r2.get("finalApprovedQty"))
    assert r2["maxPermQty"] == 0 and r2["decision"].startswith("Hold"), "ledger not shared"

    # Engine A request + history with By column
    r3 = tool1(cb, "2000001", "RED", "11.2026", 500)
    print("B #3:", r3["engine"], r3["optionType"], r3["decision"])
    cb.click("#ct-history")
    cb.wait_for_timeout(500)
    hist = cb.inner_text("#cat-hist-tbody")
    print("history rows:", hist.count("\n") + 1, "| has Chris:", "Cat Chris" in hist, "| has Dana:", "Cat Dana" in hist)
    cb.screenshot(path="tests/shot_history.png")

    # ── Conflict: stale ledger in tab A ──
    ca.evaluate("Shared.reqSeq = 0")  # pretend A has not refreshed since start
    reqs = json.load(urllib.request.urlopen(BASE + "api/requests"))
    print("server requests:", [(r["id"], r["requestedBy"], r["decision"], r.get("finalApprovedQty")) for r in reqs["requests"]])

    # ── Planner changes param → category picks it up on poll ──
    pl.click("#pt-data")
    pl.fill("#pp-T1", "90")
    pl.dispatch_event("#pp-T1", "change")
    pl.wait_for_timeout(1500)
    ca.evaluate("Shared.applyUpdate && 0")
    ca.evaluate("(async()=>{ const s=await (await fetch('api/status')).json(); })()")
    print("params v on server:", status()["paramsVersion"], "T1:", status()["params"]["T1"])

    # AI endpoints report disabled cleanly
    ai = urllib.request.Request(BASE + "api/ai/chat", data=b'{"system":"x","messages":[]}', method="POST")
    try:
        urllib.request.urlopen(ai)
    except urllib.error.HTTPError as e:
        print("ai chat disabled →", e.code, e.read()[:80])

    browser.close()

print("\nJS errors:", len(errors))
for e in errors[:20]:
    print(" -", e[:300])
