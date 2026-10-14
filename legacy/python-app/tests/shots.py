import os
from playwright.sync_api import sync_playwright
BASE="http://localhost:8765/"; SP="/tmp/claude-0/-home-claude/efd76f1c-3131-5131-b32a-8ccfd5d70f7c/scratchpad"
with sync_playwright() as pw:
    b=pw.chromium.launch(); p=b.new_page(viewport={"width":1440,"height":900}); p.on("dialog",lambda d:d.accept())
    p.goto(BASE); p.wait_for_function("window.Shared&&Shared.online"); p.wait_for_timeout(500)
    p.screenshot(path="tests/ui_role.png")
    p.fill("#role-user-name","Pat"); p.click(".rcard.plan"); p.wait_for_timeout(400)
    p.screenshot(path="tests/ui_data_empty.png")
    p.set_input_files("#sl-zipbundle input[type=file]", SP+"/bundle.zip"); p.wait_for_function("Shared.lastUploadId",timeout=30000); p.wait_for_timeout(500)
    p.screenshot(path="tests/ui_data_loaded.png", full_page=True)
    p.click("#pt-assess"); p.wait_for_timeout(300); p.screenshot(path="tests/ui_assess_empty.png")
    p.click("#btn-run-assessment"); p.wait_for_function("batchResults.length>=3"); p.wait_for_timeout(1500)
    p.click("text=Detail View") if p.locator("text=Detail View").count() else None
    p.wait_for_timeout(500); p.screenshot(path="tests/ui_assess_detail.png", full_page=True)
    p.evaluate("selectRole('category')"); p.wait_for_timeout(500); p.screenshot(path="tests/ui_cat_form.png")
    p.fill("#c-gm","1000001"); p.dispatch_event("#c-gm","input"); p.wait_for_timeout(200); p.select_option("#c-col","BLK"); p.select_option("#c-mon","11.2026"); p.fill("#c-qty","3000"); p.click("#c-btn")
    p.wait_for_selector("#qty-confirm-overlay",state="visible"); p.screenshot(path="tests/ui_confirm.png"); p.click("text=✓ Confirm Quantity"); p.wait_for_timeout(1500)
    p.screenshot(path="tests/ui_cat_result.png", full_page=True)
    p.set_viewport_size({"width":1280,"height":800}); p.click("#ct-insights"); p.wait_for_timeout(500); p.screenshot(path="tests/ui_cat_insights.png", full_page=True)
    p.set_viewport_size({"width":390,"height":844}); p.click("#ct-request"); p.wait_for_timeout(400); p.screenshot(path="tests/ui_mobile.png")
    b.close()
