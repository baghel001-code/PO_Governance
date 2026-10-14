import os, json, urllib.request
from playwright.sync_api import sync_playwright
BASE="http://localhost:8765/"; SP="/tmp/claude-0/-home-claude/efd76f1c-3131-5131-b32a-8ccfd5d70f7c/scratchpad"; errs=[]
ADMIN="kosaraju.srihari@vishalwholesale.co.in"; NEWPW="Vmm2026ok"
def login(b, email, pw="1234", first=True, name=""):
    ctx=b.new_context(viewport={"width":1440,"height":900}); p=ctx.new_page()
    p.on("pageerror",lambda e:errs.append(name+": "+str(e))); p.on("dialog",lambda d:d.accept())
    p.goto(BASE); p.wait_for_url("**/login.html**")
    p.fill("#email",email); p.fill("#pw",pw); p.click("#b-login")
    if first:
        p.wait_for_selector("#f-change:not(.hidden)")
        p.fill("#new1",NEWPW); p.fill("#new2",NEWPW); p.click("#b-change")
    p.wait_for_function("window.Shared&&Shared.online===true",timeout=60000)
    return p
with sync_playwright() as pw:
    b=pw.chromium.launch()
    # login page look
    lp=b.new_page(viewport={"width":1440,"height":900}); lp.goto(BASE); lp.wait_for_url("**/login.html**"); lp.screenshot(path="tests/au_login.png")
    lp.fill("#email",ADMIN); lp.fill("#pw","wrong"); lp.click("#b-login"); lp.wait_for_timeout(800); print("wrong pw msg:", lp.inner_text("#m-login"))
    lp.fill("#pw","1234"); lp.click("#b-login"); lp.wait_for_selector("#f-change:not(.hidden)"); lp.fill("#new1","short"); lp.wait_for_timeout(200); lp.screenshot(path="tests/au_change.png")
    lp.fill("#new1",NEWPW); lp.fill("#new2",NEWPW); lp.click("#b-change"); lp.wait_for_function("window.Shared&&Shared.online===true",timeout=60000)
    a=lp
    print("admin view:", a.evaluate("S.role"), "| tabs users/approvals visible:", a.is_visible("#pt-users"), a.is_visible("#pt-approvals"))
    a.click("#pt-data"); a.set_input_files("#sl-zipbundle input[type=file]", SP+"/bundle2.zip")
    a.wait_for_function("Shared.lastUploadId",timeout=30000); a.wait_for_timeout(800)
    st=a.evaluate("Shared.status.datasets.catheadmap && Shared.status.datasets.catheadmap.rows"); print("scr rows on server:", st)
    a.click("#pt-users"); a.wait_for_selector("#us-body table"); print("users:", a.inner_text("#us-body tbody").replace("\t"," | ").split("\n")[:12])
    a.screenshot(path="tests/au_users.png")
    a.click("text=+ Add user"); a.fill("#ue-email","ravi.planner@vishalwholesale.co.in"); a.fill("#ue-name","Ravi Planner")
    a.uncheck(".ue-role[value=buyer]"); a.check(".ue-role[value=planner]"); a.click("text=Add user >> nth=-1"); a.wait_for_timeout(800)
    print("ravi added:", "Ravi Planner" in a.inner_text("#us-body"))
    a.click("#us-tabs >> text=Import from SCR"); a.wait_for_timeout(600); a.screenshot(path="tests/au_scr.png"); print("scr columns:", a.inner_text("#us-body table >> nth=0").replace("\t"," | ").split("\n")[:5])
    # buyer
    bi=login(b,"bina.buyer@vishalwholesale.co.in",name="bina")
    print("bina role:", bi.evaluate("S.role"), "| switch role visible:", bi.is_visible(".role-btn"), "| planner tabs:", bi.is_visible("#planner-tabs"), "| approvals tab:", bi.is_visible("#ct-approvals"))
    bi.fill("#c-gm","1000001"); bi.dispatch_event("#c-gm","input"); bi.wait_for_timeout(200); bi.select_option("#c-col","GRY"); bi.select_option("#c-mon","11.2026"); bi.fill("#c-qty","300"); bi.click("#c-btn")
    bi.wait_for_timeout(2500); r=bi.evaluate("(r=>({d:r.decision,id:r.reqId,a:r.approval,by:r.requestedBy}))(S.requests[S.requests.length-1])"); print("bina request:", r)
    bi.screenshot(path="tests/au_buyer.png")
    # cat head approves
    ch=login(b,"chetan.head@vishalwholesale.co.in",name="chetan")
    print("chetan approvals badge:", ch.inner_text("#ct-approvals"))
    ch.click("#ct-approvals"); ch.wait_for_selector("#ap-body .card"); ch.screenshot(path="tests/au_approvals.png")
    ch.fill("#apq-"+r["id"],"200"); ch.fill("#apc-"+r["id"],"ok for festive"); ch.click("text=✓ Approve"); ch.wait_for_timeout(1500)
    print("after approve pending:", ch.inner_text("#ap-tabs").replace("\n"," "))
    bi.click("#ct-history"); bi.evaluate("Shared.refreshRequests().then(()=>renderCatHistory())"); bi.wait_for_timeout(800)
    print("bina sees:", bi.inner_text("#cat-hist-tbody").replace("\t"," | ")[:220])
    # restricted buyer bala
    ba=login(b,"bala.buyer@vishalwholesale.co.in",name="bala")
    ba.click("#ct-archive"); ba.wait_for_selector("#h-tabs button"); ba.wait_for_timeout(800)
    print("bala history tabs:", ba.inner_text("#h-tabs").replace("\n"," "))
    code=ba.evaluate("fetch('api/runs').then(r=>r.status)"); print("bala GET runs:", code)
    code2=ba.evaluate("fetch('api/data/f1',{method:'DELETE',headers:{'X-VMM':'1'}}).then(r=>r.status)"); print("bala DELETE data:", code2)
    # admin audit + backup
    a.click("#pt-users"); a.wait_for_timeout(500); a.click("#us-tabs >> text=Audit log"); a.wait_for_selector("#us-body table"); print("audit rows:", a.locator("#us-body tbody tr").count())
    a.screenshot(path="tests/au_audit.png")
    a.click("#us-tabs >> text=Backups"); a.wait_for_timeout(500); a.click("text=💾 Back up now"); a.wait_for_timeout(1500); print("backups:", a.locator("#us-body tbody tr").count())
    # Tool 2 run + history as admin
    a.click("#pt-assess"); a.click("#btn-run-assessment"); a.wait_for_function("document.getElementById('shared-badge').title.includes('History')||document.getElementById('shared-badge').textContent.includes('Run saved')",timeout=30000)
    a.click("#pt-history"); a.wait_for_selector("#h-body table"); print("admin history:", a.inner_text("#h-tabs").replace("\n"," "))
    # user menu switch view
    a.click("#um-btn"); print("menu switch:", a.inner_text("#um-switch")); a.click("#um-switch"); a.wait_for_timeout(500); print("view now:", a.evaluate("S.role"))
    # logout
    a.click("#um-btn"); a.click("#um-logout"); a.wait_for_url("**/login.html**"); print("after logout:", a.url)
    b.close()
print("errors:", errs)
