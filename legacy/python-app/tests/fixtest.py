import json, time, threading, urllib.request, http.cookiejar
from playwright.sync_api import sync_playwright
B="http://localhost:8796/"; R=[]
def ok(n,c,i=""): R.append(("PASS" if c else "FAIL",n,str(i)[:200])); print(R[-1],flush=True)
class C:
    def __init__(s,email,pw):
        s.cj=http.cookiejar.CookieJar(); s.op=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(s.cj)); s.email=email
        print(email, s.call("POST","auth/login",{"email":email,"password":pw})[0])
    def call(s,m,p,body=None,hdr=False):
        r=urllib.request.Request(B+"api/"+p,data=json.dumps(body).encode() if body is not None else None,method=m,headers={"X-VMM":"1","Content-Type":"application/json"})
        try:
            with s.op.open(r,timeout=120) as x: d=x.read(); return x.status,(json.loads(d) if d else None),dict(x.headers)
        except urllib.error.HTTPError as e: return e.code,json.loads(e.read() or b'{}'),dict(e.headers)
A=C("kosaraju.srihari@vishalwholesale.co.in","Accept2026ok"); H=C("harsh.gupta1@vishalwholesale.co.in","Accept2026ok"); U=C("urvashi.chhabra@vishalwholesale.co.in","Accept2026ok")
H_KEY={"brand":"NON-PL","mcCd":"113010015","mrp":"149","flag":"Normal","division":"MENS ETHNIC"}
def wl(c,info,gm="1114125010",col="WHT"):
    return c.call("POST","requests",{"request":{"gm":gm,"colour":col,"month":"11.2026","qty":100,"engine":"A","decision":"Watch List","info":info,"reqDate":"2020-01-01"},"force":True})
# 6 server date
st,d,_=wl(H,H_KEY); today=time.strftime("%Y-%m-%d"); rid=d.get("reqId"); ok("Request filed under server date even if PC sends 2020-01-01", rid and rid.startswith("REQ-"+today.replace("-","")), rid)
ok("Watch List routed to category head", bool(d.get("approval",{}).get("approvers")), d.get("approval"))
# 9 blank qty
st,d,_=U.call("POST","approvals/"+rid,{"decision":"approved","qty":"","comment":""}); ok("Blank approve quantity refused (400)", st==400, (st,d))
# 2b own approval
st,d,_=A.call("POST","requests",{"request":{"gm":"1114125010","colour":"BLK","month":"11.2026","qty":50,"engine":"A","decision":"Watch List","info":H_KEY},"force":True}); arid=d["reqId"]
st,d,_=A.call("POST","approvals/"+arid,{"decision":"approved","qty":10,"comment":"x"}); ok("Cannot approve own request (403)", st==403, (st,d))
# 4 visibility: admin request on another buyer's bucket
S_KEY={"brand":"NON-PL","mcCd":"122020014","mrp":"799","flag":"Normal"}
st,d,_=A.call("POST","requests",{"request":{"gm":"1121091526","colour":"WHT","month":"11.2026","qty":50,"engine":"A","decision":"Allow","info":S_KEY},"force":True}); orid=d["reqId"]
st,d,_=H.call("GET","requests"); ids=[r["reqId"] for r in d["requests"]]
ok("Buyer does not receive other buyers' MC requests", orid not in ids and rid in ids, ids)
ok("Buyer still sees requests on own MCs by others (ledger)", arid in ids)
st,d,_=A.call("GET","requests"); ok("Admin sees all", orid in [r["reqId"] for r in d["requests"]])
# 1 clear all -> pending becomes cleared
st,d,_=U.call("GET","approvals"); ok("Category head sees pending before clear", rid in [r["reqId"] for r in d["pending"]])
A.call("DELETE","requests?date="+today)
st,d,_=U.call("GET","approvals"); ok("After Clear All nothing left pending", rid not in [r["reqId"] for r in d["pending"]], len(d["pending"]))
st,d,_=H.call("GET","history/requests?days=2")
items=d.get("items") or d.get("requests") or []
mine=[r for r in items if r.get("reqId")==rid]
ok("Buyer history shows the Watch List as cleared, not pending forever", bool(mine) and mine[0].get("approval",{}).get("status")=="cleared", mine[0].get("approval") if mine else list(d.keys()))
# 3 atomic update
st,d,_=A.call("GET","users"); before=[u for u in d["users"] if u["email"]=="harsh.gupta1@vishalwholesale.co.in"][0]
st,d,_=A.call("POST","users/harsh.gupta1%40vishalwholesale.co.in/update",{"name":"SHOULD NOT STICK","roles":[]})
st2,d2,_=A.call("GET","users"); after=[u for u in d2["users"] if u["email"]=="harsh.gupta1@vishalwholesale.co.in"][0]
ok("Rejected edit changes nothing", st==400 and after["name"]==before["name"] and after["roles"]==before["roles"], (st,after["name"],after["roles"]))
st,d,_=A.call("POST","users/kosaraju.srihari%40vishalwholesale.co.in/update",{"roles":["planner"],"active":False}); st2,d2,_=A.call("GET","users")
me=[u for u in d2["users"] if u["email"].startswith("kosaraju")][0]; ok("Own admin role/active protected, no partial change", st==400 and "admin" in me["roles"] and me["active"], (st,me["roles"]))
# 10 cookie refresh
st,d,h=H.call("GET","status"); ok("Session cookie refreshed on status poll", "vmm_sess" in (h.get("Set-Cookie") or ""), h.get("Set-Cookie","")[:40]); ok("Status reports server date", d.get("today")==today, d.get("today"))
# 5 backup does not block
times=[]
def bk(): A.call("POST","backups")
t=threading.Thread(target=bk); t.start(); time.sleep(0.3)
for i in range(5):
    t0=time.time(); H.call("GET","status"); times.append(round(time.time()-t0,2)); time.sleep(0.3)
t.join(); ok("Status stays fast during a backup", max(times)<2, times)
# 7 params seen by second planner + clear button hidden for buyer
with sync_playwright() as pw:
    b=pw.chromium.launch()
    def page(e,pwd):
        p=b.new_page(); p.on("dialog",lambda d:d.accept()); p.goto(B); p.wait_for_url("**/login.html**"); p.fill("#email",e); p.fill("#pw",pwd); p.click("#b-login"); p.wait_for_function("window.Shared&&Shared.online===true",timeout=180000); return p
    a2=page("kosaraju.srihari@vishalwholesale.co.in","Accept2026ok")
    t1=a2.evaluate("S.params.T1")
    st,d,_=A.call("GET","status"); p=d["params"]; p["T1"]=t1+1; A.call("PUT","params",p)
    a2.wait_for_function("S.params.T1==%d"%(t1+1),timeout=45000); ok("Second planner page picks up parameter change", True, t1+1)
    p["T1"]=t1; A.call("PUT","params",p)
    hp=page("harsh.gupta1@vishalwholesale.co.in","Accept2026ok"); hp.click("#ct-history"); hp.wait_for_timeout(800)
    ok("Clear All hidden for buyers", not hp.is_visible("[onclick='clearAllRequests()']"))
    ok("Page uses server date", hp.evaluate("_todayStr()")==today)
    b.close()
print("SUMMARY",sum(1 for r in R if r[0]=="PASS"),"/",len(R))
