# v1.1.6 (5 Nov 2026, user): Attribute Analysis and AI Insights tabs removed from the Planner view.
# Only the two tab buttons are taken out; their screens and code stay in the page (not reachable), so nothing else changes.
rep("""    <button class="tab-btn" id="pt-attranalysis" onclick="plannerTab('attranalysis')">🔎 Attribute Analysis</button>
    <button class="tab-btn" id="pt-aiinsights" onclick="plannerTab('aiinsights')">🤖 AI Insights</button>
""", "", label="v116 tabs: Attribute Analysis + AI Insights removed")
