import os, json, time, urllib.request, openpyxl
from playwright.sync_api import sync_playwright
BASE="http://localhost:8765/"; TD="testdata"; errs=[]
def st(): return json.load(urllib.request.urlopen(BASE+"api/status"))
# a second F1 version with more stock
wb=openpyxl.load_workbook(TD+"/F1_Stock.xlsx"); ws=wb.active; ws.append([1000001,'PL',101,499,'NON-BOGO','RED',50]); ws.cell(2,7).value=9000; wb.save("/tmp/claude-0/-home-claude/efd76f1c-3131-5131-b32a-8ccfd5d70f7c/scratchpad/F1_v2.xlsx")
with sync_playwright() as pw:
    b=pw.chromium.launch(); ctx=b.new_context(viewport={"width":1500,"height":950},accept_downloads=True); p=ctx.new_page()
    p.on("pageerror",lambda e:errs.append(str(e))); p.on("dialog",lambda d:d.accept("1234"))
    p.goto(BASE); p.wait_for_function("window.Shared&&Shared.online")
    p.fill("#role-user-name","Pat"); p.click(".rcard.plan"); p.wait_for_timeout(400)
    for k,f in [("f1","F1_Stock.xlsx"),("f2","F2_Pending.xlsx"),("f3","F3_Sales.xlsx"),("f4","F4_Forecast.xlsx"),("um","UniverseMaster.xlsx"),("bsr","BSR_SS26.xlsx"),("artmaster","ArticleMaster.xlsx")]:
        p.set_input_files("#sl-%s input[type=file]"%k, os.path.join(TD,f)); p.wait_for_function("S.files['%s']&&S.files['%s'].length"%(k,k))
    p.wait_for_function("Object.keys(Shared.status.datasets).length>=7&&!Shared.pushing",timeout=30000); p.wait_for_timeout(1200)
    def run():
        p.click("#pt-assess"); p.click("#btn-run-assessment")
        p.wait_for_function("document.getElementById('btn-run-assessment').textContent.includes('Run Assessment')&&batchResults.length>=3")
        p.wait_for_function("document.getElementById('shared-badge').textContent.includes('Run saved')",timeout=20000)
        return p.evaluate("batchResults.map(r=>r.decision+':'+r.colour).join(',')")
    r1=run(); print("run1:",r1)
    # upload v2 of F1 and run again
    p.click("#pt-data"); p.set_input_files("#sl-f1 input[type=file]","/tmp/claude-0/-home-claude/efd76f1c-3131-5131-b32a-8ccfd5d70f7c/scratchpad/F1_v2.xlsx")
    p.wait_for_function("S.files.f1.length===4"); p.wait_for_timeout(2500)
    r2=run(); print("run2:",r2)
    # History tab
    p.click("#pt-history"); p.wait_for_selector("#h-body table")
    print("tabs:", p.inner_text("#h-tabs").replace("\n"," "))
    print("runs rows:", p.locator("#h-body tbody tr").count())
    # open the OLDER run (last row)
    p.locator("#h-body tbody tr").last.locator("text=Open").click()
    p.wait_for_selector("#shared-history-banner",state="visible")
    print("history mode f1 rows:", p.evaluate("S.files.f1.length"), "| results:", p.evaluate("batchResults.map(r=>r.decision+':'+r.colour).join(',')"))
    print("banner:", p.inner_text("#shared-history-text")[:120])
    print("detail tree has Brand:", p.evaluate("!!document.getElementById('ass-results') && document.getElementById('ass-results').style.display"))
    with p.expect_download(timeout=60000) as dl: p.evaluate("exportAssXLSX()")
    print("export from saved run:", dl.value.suggested_filename)
    p.screenshot(path="tests/shot_hist_run.png")
    p.click("text=Back to current data"); p.wait_for_timeout(1500)
    print("back: f1 rows", p.evaluate("S.files.f1.length"), "historyMode", p.evaluate("Shared.historyMode"))
    # uploads tab: download + make current old f1
    p.click("#pt-history"); p.wait_for_selector("#h-body table"); p.click("text=📂 Data uploads"); p.wait_for_timeout(500)
    print("uploads rows:", p.locator("#h-body tbody tr").count())
    with p.expect_download() as dl2: p.locator("#h-body tbody tr", has_text="F1 — Stock").last.locator("text=⬇ Excel").click()
    print("version download:", dl2.value.suggested_filename)
    p.locator("#h-body tbody tr", has_text="F1 — Stock").last.locator("text=Make current").click(); p.wait_for_timeout(2500)
    print("after restore f1 rows:", p.evaluate("S.files.f1.length"), "| server f1 rows:", st()["datasets"]["f1"]["rows"])
    p.screenshot(path="tests/shot_hist_uploads.png")
    # requests tab & date range
    p.click("text=✏️ Tool 1 requests"); p.wait_for_timeout(300)
    print("requests in last 30d:", p.locator("#h-body tbody tr").count())
    p.fill("#h-from","2026-08-01"); p.dispatch_event("#h-from","change"); p.wait_for_timeout(800)
    print("requests from 1 Aug:", p.locator("#h-body tbody tr").count(), p.inner_text("#h-body tbody tr >> nth=0")[:80].replace("\t"," "))
    with p.expect_download() as dl3: p.click("#h-body >> text=⬇ Excel")
    print("requests export:", dl3.value.suggested_filename)
    p.screenshot(path="tests/shot_hist_requests.png")
    # category view
    c=b.new_page(); c.on("pageerror",lambda e:errs.append("cat "+str(e))); c.goto(BASE); c.wait_for_function("window.Shared&&Shared.online")
    c.fill("#role-user-name","Cat"); c.click(".rcard.cat"); c.wait_for_timeout(300); c.click("#ct-archive"); c.wait_for_selector("#h-tabs button")
    print("category tabs:", c.inner_text("#h-tabs").replace("\n"," "))
    b.close()
print("errors:",errs)
