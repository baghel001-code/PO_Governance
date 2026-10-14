import os, json
from playwright.sync_api import sync_playwright
BASE=os.environ.get("BASE","http://localhost:8766/"); TD="testdata"; errs=[]
with sync_playwright() as pw:
    b=pw.chromium.launch(); p=b.new_page(viewport={"width":1500,"height":950})
    p.on("pageerror",lambda e:errs.append(str(e))); p.on("dialog",lambda d:d.accept())
    p.goto(BASE); p.wait_for_function("window.Shared&&Shared.online")
    print("status ai:", p.evaluate("Shared.ai"))
    p.fill("#role-user-name","Pat"); p.click(".rcard.plan"); p.wait_for_timeout(300)
    for k,f in [("f1","F1_Stock.xlsx"),("f2","F2_Pending.xlsx"),("f3","F3_Sales.xlsx"),("f4","F4_Forecast.xlsx"),("um","UniverseMaster.xlsx"),("bsr","BSR_SS26.xlsx"),("artmaster","ArticleMaster.xlsx")]:
        p.set_input_files("#sl-%s input[type=file]"%k, os.path.join(TD,f)); p.wait_for_function("S.files['%s']&&S.files['%s'].length"%(k,k))
    p.click("#pt-assess"); p.click("#btn-run-assessment"); p.wait_for_function("batchResults.length>=3")
    p.click("#pt-aiinsights"); p.click("#btn-ai-insights")
    p.wait_for_function("!document.getElementById('btn-ai-insights').disabled", timeout=20000)
    print("insights cards:", p.inner_text("#ai-risk-cards")[:160].replace("\n"," | "))
    print("insights action:", p.inner_text("#ai-action-card")[:120].replace("\n"," | "))
    p.screenshot(path="tests/shot_insights.png")
    p.evaluate("toggleAIChat()"); p.wait_for_timeout(300)
    p.fill("#ai-chat-input","Which line is riskiest?"); p.click("#ai-chat-send-btn")
    p.wait_for_function("document.getElementById('ai-chat-send-btn').textContent==='Send'", timeout=20000)
    print("chat:", p.inner_text("#ai-chat-messages")[-160:].replace("\n"," | "))
    p.screenshot(path="tests/shot_chat.png")
    b.close()
print("errors", errs)
