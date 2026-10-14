import json, gzip, urllib.request
from playwright.sync_api import sync_playwright
BASE="http://localhost:8765/"
body=gzip.compress(json.dumps({"rows":[{"MC CD":"101","Color":"X%d"%i,"OP STK":"1"} for i in range(5)]}).encode())
errs=[]
with sync_playwright() as pw:
    b=pw.chromium.launch(); c=b.new_context(); p=c.new_page(); p.on("pageerror",lambda e:errs.append(str(e)))
    p.on("dialog",lambda d:d.accept("1234"))
    p.goto(BASE); p.wait_for_function("window.Shared&&Shared.online"); p.fill("#role-user-name","Cy"); p.click(".rcard.cat"); p.wait_for_timeout(300)
    print("before", p.evaluate("S.files.f1.length"))
    rq=urllib.request.Request(BASE+"api/data/f1",data=body,method="PUT",headers={"X-Body-Encoding":"gzip","X-Rows":"5","X-Planner-Pin":"1234","X-User":"Script"})
    print(json.load(urllib.request.urlopen(rq))["dataVersion"])
    p.wait_for_selector("#shared-update-banner",state="visible",timeout=40000)
    print("banner:", p.inner_text("#shared-update-text"))
    p.click("text=Load now"); p.wait_for_timeout(1500)
    print("after", p.evaluate("S.files.f1.length"), "|", p.evaluate("document.getElementById('st-f1').textContent"))
    # planner clears BSR -> deleted on server
    p.evaluate("openRoleDialog()"); p.fill("#role-user-name","Pat"); p.click(".rcard.plan"); p.wait_for_timeout(500)
    p.evaluate("clearFileSlot('bsr')"); p.wait_for_timeout(2500)
    print("bsr on server:", "bsr" in json.load(urllib.request.urlopen(BASE+"api/status"))["datasets"])
    b.close()
print("errors",errs)
