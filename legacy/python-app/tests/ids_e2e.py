import os, json, urllib.request
from playwright.sync_api import sync_playwright
BASE="http://localhost:8765/"; SP="/tmp/claude-0/-home-claude/efd76f1c-3131-5131-b32a-8ccfd5d70f7c/scratchpad"; errs=[]
with sync_playwright() as pw:
    b=pw.chromium.launch(); ctx=b.new_context(viewport={"width":1500,"height":950},accept_downloads=True); p=ctx.new_page()
    p.on("pageerror",lambda e:errs.append(str(e))); p.on("dialog",lambda d:d.accept("1234"))
    p.goto(BASE); p.wait_for_function("window.Shared&&Shared.online")
    p.fill("#role-user-name","Pat"); p.click(".rcard.plan"); p.wait_for_timeout(400)
    p.set_input_files("#sl-zipbundle input[type=file]", SP+"/bundle.zip")
    p.wait_for_function("Shared.lastUploadId", timeout=30000); up1=p.evaluate("Shared.lastUploadId")
    print("zip upload id:", up1, "| slot:", p.inner_text("#st-f1"))
    p.set_input_files("#sl-f1 input[type=file]", SP+"/F1_v2.xlsx")
    p.wait_for_function("Shared.lastUploadId!=='%s'"%up1, timeout=30000); up2=p.evaluate("Shared.lastUploadId"); print("single upload id:", up2)
    p.click("#pt-assess"); p.click("#btn-run-assessment"); p.wait_for_function("document.getElementById('shared-badge').textContent.includes('Run saved')",timeout=30000)
    # a category request
    c=b.new_page(); c.on("pageerror",lambda e:errs.append("cat "+str(e))); c.on("dialog",lambda d:d.accept())
    c.goto(BASE); c.wait_for_function("window.Shared&&Shared.online"); c.fill("#role-user-name","Cy"); c.click(".rcard.cat"); c.wait_for_timeout(300)
    c.fill("#c-gm","1000001"); c.dispatch_event("#c-gm","input"); c.wait_for_timeout(200); c.select_option("#c-col","BLK"); c.select_option("#c-mon","11.2026"); c.fill("#c-qty","100"); c.click("#c-btn")
    try:
        c.wait_for_selector("#qty-confirm-overlay",state="visible",timeout=4000); c.fill("#qc-input","100"); c.click("text=✓ Confirm Quantity")
    except Exception: pass
    c.wait_for_timeout(1500); print("request id:", c.evaluate("S.requests[S.requests.length-1].reqId"))
    c.click("#ct-history"); c.wait_for_timeout(400); print("cat requests first cell:", c.inner_text("#cat-hist-tbody tr td >> nth=0"))
    # History
    p.click("#pt-history"); p.wait_for_selector("#h-body table"); p.wait_for_timeout(300)
    print("run id cell:", p.inner_text("#h-body tbody tr td >> nth=0"))
    p.click("#h-tabs >> text=Data uploads"); p.wait_for_timeout(300)
    print("upload rows:", [p.inner_text("#h-body tbody tr >> nth=%d"%i).split("\t")[0] for i in range(p.locator("#h-body tbody tr").count())])
    p.screenshot(path="tests/shot_ids_list.png")
    p.click("#h-body >> text="+up1); p.wait_for_timeout(300)
    print("detail files:", p.locator("#h-body tbody tr").count(), "|", p.inner_text("#h-body .card >> nth=0").replace("\n"," ")[:160])
    p.screenshot(path="tests/shot_ids_detail.png")
    with p.expect_download() as d: p.click("text=⬇ All files (one Excel)")
    print("download all:", d.value.suggested_filename)
    p.click("text=Make this upload current"); p.wait_for_timeout(3000)
    st=json.load(urllib.request.urlopen(BASE+"api/status")); print("f1 now rows", st["datasets"]["f1"]["rows"], "from", st["datasets"]["f1"].get("uploadId"))
    p.fill("#h-up-q","restore") if p.locator("#h-up-q").count() else None
    p.click("#h-tabs >> text=Tool 1 requests"); p.wait_for_timeout(300)
    p.click("#h-body tbody tr >> nth=0 >> a"); p.wait_for_timeout(200)
    print("request detail:", p.inner_text("#h-body tbody tr >> nth=1")[:140].replace("\n"," "))
    p.click("text=← Category"); p.wait_for_timeout(800); print("after role back:", p.evaluate("S.role"), p.evaluate("document.getElementById('view-cat-request').style.display"))
    b.close()
print("errors:",errs)
