"""Two users confirm against the same colour at the same time."""
import json, os, urllib.request
from playwright.sync_api import sync_playwright
BASE="http://localhost:8765/"
errs=[]
def page(b,name,who):
    c=b.new_context(timezone_id="Asia/Kolkata"); p=c.new_page()
    p.on("pageerror",lambda e: errs.append(str(e)))
    p.on("dialog",lambda d: d.accept())
    p.goto(BASE); p.wait_for_function("window.Shared&&Shared.online")
    p.fill("#role-user-name",who); p.click(".rcard.cat"); p.wait_for_timeout(300); return p
def start(p,qty):
    p.fill("#c-gm","1000001"); p.dispatch_event("#c-gm","input"); p.wait_for_timeout(200)
    p.select_option("#c-col","WHT"); p.select_option("#c-mon","11.2026"); p.fill("#c-qty",str(qty)); p.click("#c-btn")
    p.wait_for_selector("#qty-confirm-overlay",state="visible")
    return p.inner_text("#qc-max")
def confirm(p,qty):
    p.fill("#qc-input",str(qty)); p.click("text=✓ Confirm Quantity")
    p.wait_for_function("!document.getElementById('c-analyzing').classList.contains('show')"); p.wait_for_timeout(500)
    return p.evaluate("(r=>({d:r.decision,ap:r.finalApprovedQty,id:r.id,why:r.decisionReason.slice(0,160)}))(S.requests.find(x=>x.colour==='WHT'&&x.requestedBy===Shared.userName()))")
with sync_playwright() as pw:
    b=pw.chromium.launch()
    a=page(b,"A","Asha"); c=page(b,"B","Bala")
    print("A sees max",start(a,2000)); print("B sees max",start(c,2000))
    print("B:",confirm(c,2000))
    print("A:",confirm(a,2000))
    b.close()
print("errors",errs)
