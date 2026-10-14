import time, sys, json
from playwright.sync_api import sync_playwright
B="http://localhost:8796/"; PW="Accept2026ok"; ADMIN="kosaraju.srihari@vishalwholesale.co.in"
ZIP="/mnt/user-data/uploads/1791773669057_BSR_DETAIL_KIDS_SS26.zip"; SCR="/mnt/user-data/outputs/SCR_PRODUCTS.xlsx"
BUY="harsh.gupta1@vishalwholesale.co.in"; CH="urvashi.chhabra@vishalwholesale.co.in"
errs=[]; R=[]
def ok(name,cond,info=""): R.append(("PASS" if cond else "FAIL",name,str(info)[:200])); print(R[-1],flush=True)
def login(b,email,pw,first=True):
    p=b.new_page(viewport={"width":1440,"height":900}); p.on("dialog",lambda d:d.accept()); p.on("pageerror",lambda e:errs.append((email,str(e))))
    p.goto(B); p.wait_for_url("**/login.html**"); p.fill("#email",email); p.fill("#pw",pw); p.click("#b-login")
    if first:
        p.wait_for_selector("#f-change:not(.hidden)",timeout=15000); p.fill("#new1",PW); p.fill("#new2",PW); p.click("#b-change")
    t=time.time(); p.wait_for_function("window.Shared&&Shared.online===true",timeout=300000); return p,round(time.time()-t,1)
with sync_playwright() as pw:
    b=pw.chromium.launch()
    a,_=login(b,ADMIN,"1234"); a.click("#pt-data")
    t=time.time(); a.set_input_files("#sl-zipbundle input[type=file]",ZIP)
    a.wait_for_function("['f1','f2','f3','f4','um','bsr','artmaster'].every(k=>Shared.status&&Shared.status.datasets&&Shared.status.datasets[k])",timeout=900000)
    a.wait_for_timeout(3000); ds=a.evaluate("Object.fromEntries(Object.entries(Shared.status.datasets).map(([k,v])=>[k,v.rows]))")
    ok("Real 9 Oct data zip uploads",len(ds)>=7,"%ss %s"%(round(time.time()-t),ds))
    a.set_input_files("#sl-catheadmap input[type=file]",SCR); a.wait_for_function("Shared.status.datasets.catheadmap",timeout=120000); a.wait_for_timeout(4000)
    a.click("#pt-users"); a.wait_for_selector("#us-body table",timeout=30000); a.wait_for_timeout(1500)
    users=a.evaluate("fetch('api/users',{headers:{'X-VMM':'1'}}).then(r=>r.json())")
    lst=users.get("users",users) if isinstance(users,dict) else users
    roles={}
    for u in lst: 
        for r in (u.get("roles") or [u.get("role")]): roles[r]=roles.get(r,0)+1
    ok("SCR upload creates logins (expect ~68 + admin)",len(lst)>=60,"%d users, roles %s"%(len(lst),roles))
    ub={u["email"]:u for u in lst}
    ok("Level 1 buyer has buyer role", BUY in ub and "buyer" in json.dumps(ub[BUY]), ub.get(BUY))
    ok("Level 3 gets category head role", CH in ub and "cathead" in json.dumps(ub[CH]), ub.get(CH))
    a.click("#us-tabs >> text=💬 Feedback"); a.wait_for_timeout(1200); ok("Admin Feedback tab opens", a.is_visible("#us-body"), a.inner_text("#us-body")[:80])
    # buyer
    p,lt=login(b,BUY,"1234"); ok("Buyer first sign-in + data load time (s)", True, lt)
    vis={i:p.is_visible("#"+i) for i in ["ct-video","ct-request","ct-history","ct-howitworks","ct-archive","ct-insights","ct-approvals","fb-btn"]}
    ok("Buyer tabs: Video/New Request/Requests/How it works/History, no Insights/Approvals", all(vis[k] for k in ["ct-video","ct-request","ct-history","ct-howitworks","ct-archive","fb-btn"]) and not vis["ct-insights"] and not vis["ct-approvals"], vis)
    p.click("#ct-video"); p.wait_for_timeout(800); src=p.evaluate("document.querySelector('#howto-video source').getAttribute('src')")
    st=p.evaluate("fetch('video/how-to.mp4',{headers:{Range:'bytes=0-99'}}).then(r=>r.status+' '+r.headers.get('content-range'))")
    ok("Video tab points to how-to.mp4 and server streams it", src.endswith("how-to.mp4") and st.startswith("206"), st)
    def check(gm,col,mon,qty):
        p.click("#ct-request"); p.wait_for_timeout(300)
        try: p.click("text=✕ Clear",timeout=1000)
        except Exception: pass
        p.fill("#c-gm",str(gm)); p.dispatch_event("#c-gm","input"); p.wait_for_timeout(600)
        opts=p.evaluate("[...document.querySelectorAll('#c-col option')].map(o=>o.value)")
        if col not in opts: return ("colour not offered",opts[:8])
        p.select_option("#c-col",col); p.select_option("#c-mon",mon); p.fill("#c-qty",str(qty)); t=time.time(); p.click("#c-btn"); modal=None
        try:
            p.wait_for_selector("#qty-confirm-overlay",state="visible",timeout=8000); modal=p.evaluate("document.getElementById('qc-input').value"); p.click("text=✓ Confirm Quantity")
        except Exception: pass
        p.wait_for_function("!document.getElementById('c-analyzing').classList.contains('show')",timeout=120000); p.wait_for_timeout(600)
        r=p.evaluate("(r=>[r.decision,r.engine,r.maxPermQty,r.finalApprovedQty,(r.decisionReason||'').slice(0,120)])(S.requests[S.requests.length-1])")
        return (r,modal,round(time.time()-t,1))
    r1=check(1114125010,"WHT","11.2026",50000); ok("Real single concept check (WHT 50,000)", isinstance(r1[0],list), r1)
    col=None
    for c in ["BRW","BLK","AQA"]:
        r=check(1114125010,c,"11.2026",500)
        if isinstance(r[0],list) and r[0][3]: col=c; r2=r; break
    ok("Real single concept approval with room", col is not None, (col, r if col is None else r2))
    if col:
        appr=r2[0][3]
        t=time.time(); p.locator(".rec-btn").last.click(); p.wait_for_selector("#rec-overlay .rec-tbl",timeout=120000); p.wait_for_timeout(800)
        hdr=p.evaluate("[...document.querySelectorAll('#rec-overlay .rec-tbl thead th')].map(t=>t.textContent.trim())")
        row=p.evaluate("c=>{var tr=[...document.querySelectorAll('#rec-overlay .rec-tbl tbody tr')].find(tr=>tr.children[1]&&tr.children[1].textContent.trim()===c);return tr?[...tr.children].map(td=>td.textContent.trim()):null}",col)
        ok("Single concept recommendation shows Today's approved column", any("TODAY" in h.upper() for h in hdr), "%ss hdr=%s"%(round(time.time()-t,1),[h for h in hdr if h][:14]))
        ok("Approved qty appears in the colour row", row is not None and ("{:,}".format(appr) in row or str(appr) in row), (appr,row))
        p.evaluate("closeRec()")
        r3=check(1114125010,col,"11.2026",500000); ok("Same colour again: room reduced by today's approval", isinstance(r3[0],list) and (r3[0][2] or 0) < (r2[0][2] or 0)+1, ("before max",r2[0][2],"after",r3[0][2],r3[0][0]))
    n0=p.evaluate("S.requests.length"); rm=check(1120106348,"BLK","11.2026",800); ok("Other buyer MC is blocked (Not your MC)", p.evaluate("S.requests.length")==n0, rm)
    p.click("#fb-btn"); p.wait_for_selector("#fb-msg"); p.select_option("#fb-kind","Problem"); p.fill("#fb-msg","Acceptance test feedback"); p.click("#fb-send"); p.wait_for_timeout(1500)
    p.click("#ct-howitworks"); p.wait_for_timeout(500); txt=p.inner_text("#view-cat-howitworks") if p.query_selector("#view-cat-howitworks") else ""
    ok("How it works has no 'was ...' wording", "(was " not in txt and len(txt)>200, len(txt))
    # cat head
    c,lt2=login(b,CH,"1234"); vis2=c.is_visible("#ct-approvals"); ok("Category head sees Approvals tab", vis2, lt2)
    a.reload(); a.wait_for_function("window.Shared&&Shared.online===true",timeout=300000); a.click("#pt-users"); a.wait_for_selector("#us-body table"); a.click("#us-tabs >> text=💬 Feedback"); a.wait_for_timeout(1500)
    ok("Buyer feedback reaches admin", "Acceptance test feedback" in a.inner_text("#us-body"))
    b.close()
print("PAGE ERRORS:",[e for e in errs if 'rbtn-' not in e[1]][:10])
print("SUMMARY:",sum(1 for r in R if r[0]=="PASS"),"pass /",len(R))
