"""Builds app/index.html from the original page (v89 (6), rules unchanged):
offline assets and the hooks the shared server needs. Every edit must match exactly the
expected number of times, so a changed source fails loudly instead of
silently skipping a fix."""
import re
import sys

SRC = "orig.html"
OUT = "app/index.html"
html = open(SRC, encoding="utf-8").read()
log = []


def rep(old, new, count=1, label=""):
    global html
    n = html.count(old)
    if n != count:
        sys.exit("PATCH FAILED [%s]: expected %d match(es), found %d\n  %r" % (label, count, n, old[:160]))
    html = html.replace(old, new)
    log.append(label)


# ═════════════════════ 1. OFFLINE ASSETS ═════════════════════
rep('<link href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500&family=Barlow+Condensed:wght@700;800;900&display=swap" rel="stylesheet">',
    '<link href="vendor/fonts/fonts.css" rel="stylesheet">', label="fonts local")
rep('<script src="https://cdn.jsdelivr.net/npm/xlsx-js-style@1.2.0/dist/xlsx.min.js"></script>',
    '<script src="vendor/xlsx.min.js"></script>', label="xlsx local")
rep('<script src="https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js"></script>',
    '<script src="vendor/jszip.min.js"></script>', label="jszip local")

# Sections 2–7 (logic fixes, help-text edits, dead-code removal) were REMOVED
# on 6 Oct 2026 at the business owner's request: the decision rules, labels
# and help text are kept exactly as in VMM_PO_Governance_v89 (6). Only the
# offline assets and the shared-server hooks below are applied.

# ═════════════════════ 8. SHARED-SERVER HOOKS ═════════════════════
# Role dialog: people now sign in, so it only chooses the view.
rep("""    <div style="font-size:12px;color:#6b7a99;margin-bottom:28px">Select your role</div>""",
    """    <div style="font-size:12px;color:#6b7a99;margin-bottom:28px">Choose which screens to open</div>""",
    label="role dialog text")

# Top bar: server status badge + user chip
rep("""  <div style="margin-left:12px"><button class="role-btn" onclick="openRoleDialog()" """,
    """  <div id="shared-user" style="margin-left:auto;font-size:10.5px;color:rgba(255,255,255,0.7);white-space:nowrap"></div>
  <div id="shared-badge" style="margin-left:12px;font-size:10.5px;color:rgba(255,255,255,0.85);background:rgba(255,255,255,0.08);border:1px solid rgba(255,255,255,0.18);padding:4px 10px;border-radius:12px;cursor:pointer;white-space:nowrap">Connecting…</div>
  <div style="margin-left:12px"><button class="role-btn" onclick="openRoleDialog()" """, label="topbar badge")

# Request history: all users, with a "By" column and a "mine only" filter
rep("""<div class="pg-title">🕒 My Requests</div>""", """<div class="pg-title">🕒 Today's Requests</div>""", label="history title")
rep(""">My Requests</button>""", """>Requests</button>""", label="history tab")
rep("""<div class="sum-hdr-t">Today's Requests</div><button class="btn-sec" style="font-size:11px;padding:5px 12px" onclick="clearAllRequests()">Clear All</button>""",
    """<div class="sum-hdr-t">Today's Requests — all users</div><label style="margin-left:auto;margin-right:12px;font-size:11px;color:rgba(255,255,255,0.85);display:flex;align-items:center;gap:5px;cursor:pointer"><input type="checkbox" onchange="Shared.toggleMine(this.checked)"> Only mine</label><button class="btn-sec" style="font-size:11px;padding:5px 12px" onclick="clearAllRequests()">Clear All</button>""",
    label="history header")
rep("<th>Decision</th><th>Approved Qty</th></tr></thead>\n          <tbody id=\"cat-hist-tbody\">",
    "<th>Decision</th><th>Approved Qty</th><th>By</th></tr></thead>\n          <tbody id=\"cat-hist-tbody\">", label="history By col")
rep("""  var todayReqs = S.requests.filter(function(r){ return r.reqDate===todayStr; });""",
    """  var todayReqs = S.requests.filter(function(r){ return r.reqDate===todayStr; });
  if (window.Shared) todayReqs = Shared.visibleRequests(todayReqs);""", label="history filter")
rep("""      + '<td style="font-family:var(--mono);font-weight:700">' + (approved!==null ? fmt(approved) : '—') + '</td>'
      + '</tr>';""",
    """      + '<td style="font-family:var(--mono);font-weight:700">' + (approved!==null ? fmt(approved) : '—') + '</td>'
      + '<td style="font-size:11px">' + String(r.requestedBy||'—').replace(/[<>&]/g,'') + '</td>'
      + '</tr>';""", label="history By cell")

# Tool 1 submit: refresh the shared ledger before deciding, save after.
rep("""  setTimeout(async function() {
    try {
      var info  = resolveGM(gm);
      var route = routeRequest(gm, colour);
      S.reqCount++;""",
    """  setTimeout(async function() {
    try {
      // Pull every user's approvals for today first, so the Max Permissible
      // ledger reflects the whole team, not just this browser.
      if (window.Shared && Shared.online) await Shared.refreshRequests();
      var info  = resolveGM(gm);
      var route = routeRequest(gm, colour);
      S.reqCount++;""", label="submit refresh ledger")
rep("""      result.reqDate=_todayStr();
      S.requests.push(result);
      S.activeResultsArea = m.results;""",
    """      result.reqDate=_todayStr();
      S.requests.push(result);
      if (window.Shared && Shared.online) await Shared.saveRequest(result);
      S.activeResultsArea = m.results;""", label="submit save")

rep("""function clearAllRequests() {
  S.requests = []; S.reqCount = 0;""",
    """async function clearAllRequests() {
  if (window.Shared && Shared.online) { if (!(await Shared.clearRequests())) return; }
  S.requests = []; S.reqCount = 0;""", label="clear requests shared")

# ═════════════════════ 9. AI THROUGH THE SERVER ═════════════════════
rep("""function _aiKeyGet(){ try{ return localStorage.getItem('vmm_llm_apikey')||''; }catch(e){ return ''; } }""",
    """// Keys now live on the server (config.json) — never in the browser.
function _aiKeyGet(){ return (window.Shared && Shared.ai && Shared.ai.chat) ? 'server' : ''; }""", label="ai key get")
rep("""function openAIChatSettings(){
  $g('ai-chat-key-input').value='';
  $g('ai-chat-key-clear-row').style.display = _aiKeyGet() ? 'block' : 'none';
  $g('ai-chat-keysetup').style.display='block';""",
    """function openAIChatSettings(){
  $g('ai-chat-keysetup').style.display='block';""", label="ai settings")
rep("""  var key = _aiKeyGet();
  if(!key){ openAIChatSettings(); return; }

  _aiChatAppendMsg('user', q);""",
    """  if(!_aiKeyGet()){ openAIChatSettings(); return; }

  _aiChatAppendMsg('user', q);""", label="ai send key check")
rep("""  fetch('https://api.anthropic.com/v1/messages', {
    method:'POST',
    headers:{
      'Content-Type':'application/json',
      'x-api-key': key,
      'anthropic-version':'2023-06-01',
      'anthropic-dangerous-direct-browser-access':'true'
    },
    body: JSON.stringify({
      model:'claude-sonnet-4-6',
      max_tokens:1000,
      system: sysPrompt,
      messages: messages
    })
  }).then(function(resp){
    return resp.json().then(function(data){ return {ok:resp.ok, status:resp.status, data:data}; });
  }).then(function(res){""",
    """  Shared.aiChat(sysPrompt, messages).then(function(res){""", label="ai chat via server")
rep("""      _aiChatAppendMsg('error', 'Error: '+msg+(res.status===401?' — check your API key (gear icon above).':''));""",
    """      if(res.data&&typeof res.data.error==='string') msg=res.data.error;
      _aiChatAppendMsg('error', 'Error: '+msg);""", label="ai chat error msg")
rep("""    _aiChatAppendMsg('error', 'Network error: '+err.message+'. If this persists, your browser or network may be blocking direct calls to api.anthropic.com.');""",
    """    _aiChatAppendMsg('error', 'Could not reach the portal server: '+err.message);""", label="ai chat net err")

keysetup = re.search(r'<div id="ai-chat-keysetup"[^>]*>.*?</div>\n    </div>\n', html, re.S)
if not keysetup:
    sys.exit("PATCH FAILED [ai keysetup]")
html = html[:keysetup.start()] + """<div id="ai-chat-keysetup" style="display:none;padding:16px;font-size:12px;color:var(--ink2);flex:1;overflow-y:auto">
      <div style="font-weight:700;margin-bottom:6px;color:var(--vmm-navy)">AI chat is not enabled on this server</div>
      <div style="line-height:1.6">The assistant runs through the portal server, which holds the company API key — users never paste keys into the browser.<br><br>
      To turn it on, an administrator adds an Anthropic API key as <code>anthropic_api_key</code> in <code>config.json</code> on the VM and restarts the service. The VM also needs outbound HTTPS access to <code>api.anthropic.com</code> (directly or through <code>outbound_proxy</code>).<br><br>
      Everything else in the portal works without it.</div>
    </div>
""" + html[keysetup.end():]
log.append("ai keysetup panel")

rep("""  var key = (document.getElementById('ai-api-key') || {}).value;
  var focus = (document.getElementById('ai-focus') || {}).value || 'risk';

  key = key ? key.trim() : '';
  if (!key) { toast('Paste a free Groq API key in the sidebar first'); return; }""",
    """  var focus = (document.getElementById('ai-focus') || {}).value || 'risk';
  if (!(window.Shared && Shared.ai && Shared.ai.insights)) { toast('AI Insights is not enabled on this server — ask your administrator (see README)'); return; }""",
    label="insights key check")
rep("""    var res = await fetch('https://api.groq.com/openai/v1/chat/completions', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': 'Bearer ' + key
      },
      body: JSON.stringify({
        model: 'llama-3.3-70b-versatile',
        messages: [{ role: 'user', content: prompt }],
        temperature: 0.5,
        response_format: { type: 'json_object' }
      })
    });
    var data = await res.json();
    if (data.error) {
      findingCards.innerHTML = '';
      fallbackWrap.style.display = 'block';
      textEl.textContent = 'API error: ' + data.error.message;
      toast('Groq API error — check your key');""",
    """    var sres = await Shared.aiInsights(prompt);
    var data = sres.data || {};
    if (!sres.ok || data.error) {
      findingCards.innerHTML = '';
      fallbackWrap.style.display = 'block';
      textEl.textContent = 'AI error: ' + ((data.error && data.error.message) || data.error || ('HTTP ' + sres.status));
      toast('AI Insights error — see message');""", label="insights via server")
rep("""    toast('Could not reach Groq — check your connection');""",
    """    toast('Could not reach the portal server');""", label="insights net err")
groq_key_ui = re.search(r'\s*<label[^>]*>Groq API Key</label>\s*<input type="password" id="ai-api-key"[^>]*>\s*<div[^>]*>Get one free at.*?</div>', html, re.S)
if not groq_key_ui:
    sys.exit("PATCH FAILED [groq key ui]")
html = html[:groq_key_ui.start()] + """
      <div style="font-size:10px;color:rgba(255,255,255,.5);line-height:1.6;margin-bottom:10px">Runs through the portal server using the company key in <code>config.json</code>. If it's not configured, ask your administrator.</div>""" + html[groq_key_ui.end():]
log.append("groq key ui")

rep("Runs the current batch assessment results through Groq (free tier, Llama 3.3) to surface risk patterns in plain language",
    "Runs the current batch assessment results through the AI service configured on the portal server to surface risk patterns in plain language",
    label="insights sidebar text")
rep("VMM PO Governance · AI Insights (Groq, free tier)", "VMM PO Governance · AI Insights", label="insights footer")
rep("Run an assessment in <strong>PO Assessment</strong>, then paste a free Groq API key here and click <strong>Generate Insights</strong>.",
    "Run an assessment in <strong>PO Assessment</strong>, then click <strong>Generate Insights</strong> in the sidebar.",
    label="insights empty text")

# ═════════════════════ 9b. HISTORY (date-range archive) ═════════════════════
rep("""    <button class="tab-btn" id="pt-aiinsights" onclick="plannerTab('aiinsights')">🤖 AI Insights</button>""",
    """    <button class="tab-btn" id="pt-aiinsights" onclick="plannerTab('aiinsights')">🤖 AI Insights</button>
    <button class="tab-btn" id="pt-history" onclick="plannerTab('history')">🗂 History</button>""", label="planner history tab")
rep("""    <button id="ct-howitworks" onclick="categoryTab('howitworks')" """,
    """    <button id="ct-archive" onclick="categoryTab('archive')" style="padding:5px 14px;border-radius:6px;font-size:11px;font-weight:600;cursor:pointer;border:1px solid rgba(255,255,255,0.2);background:rgba(255,255,255,0.08);color:rgba(255,255,255,0.75);transition:all .18s;font-family:var(--font)">🗂 History</button>
    <button id="ct-howitworks" onclick="categoryTab('howitworks')" """, label="category history tab")
rep("""  <div id="view-analytics" style="display:none">""",
    """  <div id="view-history" style="display:none"></div>

  <div id="view-analytics" style="display:none">""", label="history view")
rep("""  <div id="sb-aiinsights" style="display:none">""",
    """  <div id="sb-history" style="display:none">
    <div class="sb-sec">
      <div class="sb-lbl">▸ History</div>
      <div style="font-size:11px;color:rgba(255,255,255,0.6);line-height:1.7">Everything is kept on the server: every file version you upload, every Tool 2 run and every Tool 1 request.<br><br>Choose a date range, then <strong>Open</strong> a saved run to see its dashboard and export exactly as it was, or download any older file version as Excel.</div>
    </div>
  </div>

  <div id="sb-aiinsights" style="display:none">""", label="history sidebar")
rep("""var PVIEWS = ['view-data','view-assess','view-analytics','view-attranalysis','view-aiinsights'];
var CVIEWS = ['view-cat-request','view-cat-history','view-cat-insights','view-cat-howitworks'];
var PSB    = ['sb-data','sb-assess','sb-analytics','sb-attranalysis','sb-aiinsights'];""",
    """var PVIEWS = ['view-data','view-assess','view-analytics','view-attranalysis','view-aiinsights','view-history'];
var CVIEWS = ['view-cat-request','view-cat-history','view-cat-insights','view-cat-howitworks'];
var PSB    = ['sb-data','sb-assess','sb-analytics','sb-attranalysis','sb-aiinsights','sb-history'];""", label="view lists")
rep("""  ['pt-data','pt-assess','pt-analytics','pt-attranalysis','pt-aiinsights'].forEach(function(id) {""",
    """  ['pt-data','pt-assess','pt-analytics','pt-attranalysis','pt-aiinsights','pt-history'].forEach(function(id) {""", label="planner tab ids")
rep("""  var sbMap = { data:'sb-data', assess:'sb-assess', analytics:'sb-analytics', attranalysis:'sb-attranalysis', aiinsights:'sb-aiinsights' };
  var vwMap = { data:'view-data', assess:'view-assess', analytics:'view-analytics', attranalysis:'view-attranalysis', aiinsights:'view-aiinsights' };""",
    """  var sbMap = { data:'sb-data', assess:'sb-assess', analytics:'sb-analytics', attranalysis:'sb-attranalysis', aiinsights:'sb-aiinsights', history:'sb-history' };
  var vwMap = { data:'view-data', assess:'view-assess', analytics:'view-analytics', attranalysis:'view-attranalysis', aiinsights:'view-aiinsights', history:'view-history' };""", label="planner tab maps")
rep("""  if (tab === 'attranalysis') renderAttrAnalysis();""",
    """  if (tab === 'attranalysis') renderAttrAnalysis();
  if (tab === 'history' && window.History) History.show();""", label="planner history show")
rep("""  ['ct-request','ct-history','ct-insights','ct-howitworks'].forEach(function(id) {""",
    """  ['ct-request','ct-history','ct-insights','ct-archive','ct-howitworks'].forEach(function(id) {""", label="category tab ids")
rep("""  var vw = document.getElementById('view-cat-' + tab);
  if (vw) vw.style.display = 'block';""",
    """  var vw = document.getElementById(tab === 'archive' ? 'view-history' : 'view-cat-' + tab);
  if (vw) vw.style.display = 'block';
  if (tab === 'archive' && window.History) History.show();""", label="category history show")
rep("""        renderAssessmentDashboard();
        renderPlannerDashboard();
        _exportCatHeadSelection.clear(); // fresh run — clear any stale selection from a previous dataset""",
    """        renderAssessmentDashboard();
        renderPlannerDashboard();
        if (window.Shared) Shared.saveRun(); // keep every run in History
        _exportCatHeadSelection.clear(); // fresh run — clear any stale selection from a previous dataset""", label="save run hook")
rep("""  if (!gm || !colour || !month || !qty || qty <= 0) { toast('Please fill all 4 fields.'); return; }""",
    """  if (!gm || !colour || !month || !qty || qty <= 0) { toast('Please fill all 4 fields.'); return; }
  if (window.Shared && Shared.historyMode) { toast('You are viewing a saved run — click "Back to current data" first.'); return; }""", label="submit history guard")

rep("""    return '<tr><td>' + r.id + '</td>'""", """    return '<tr><td style="font-family:var(--mono);font-size:10.5px;white-space:nowrap">' + (r.reqId || r.id) + '</td>'""", label="history req id")
rep("<thead><tr><th>#</th><th>Generic Material</th><th>Colour</th><th>MC Desc</th><th>MRP</th><th>Del. Month</th><th>Proposed Qty</th><th>Engine</th><th>Option Type</th><th>Decision</th><th>Approved Qty</th><th>By</th>", "<thead><tr><th>Request ID</th><th>Generic Material</th><th>Colour</th><th>MC Desc</th><th>MRP</th><th>Del. Month</th><th>Proposed Qty</th><th>Engine</th><th>Option Type</th><th>Decision</th><th>Approved Qty</th><th>By</th>", label="history req id head")

# ═════════════════════ 9c. USERS, APPROVALS ═════════════════════
rep("""    <button class="tab-btn" id="pt-history" onclick="plannerTab('history')">🗂 History</button>""",
    """    <button class="tab-btn" id="pt-history" onclick="plannerTab('history')">🗂 History</button>
    <button class="tab-btn" id="pt-approvals" onclick="plannerTab('approvals')" style="display:none">✅ Approvals</button>
    <button class="tab-btn" id="pt-users" onclick="plannerTab('users')" style="display:none">👥 Users</button>""", label="planner admin tabs")
rep("""    <button id="ct-archive" onclick="categoryTab('archive')" """,
    """    <button id="ct-approvals" onclick="categoryTab('approvals')" style="display:none;padding:5px 14px;border-radius:6px;font-size:11px;font-weight:600;cursor:pointer;border:1px solid rgba(255,255,255,0.2);background:rgba(255,255,255,0.08);color:rgba(255,255,255,0.75);transition:all .18s;font-family:var(--font)">✅ Approvals</button>
    <button id="ct-archive" onclick="categoryTab('archive')" """, label="category approvals tab")
rep("""  <div id="view-history" style="display:none"></div>""",
    """  <div id="view-history" style="display:none"></div>
  <div id="view-approvals" style="display:none"></div>
  <div id="view-users" style="display:none"></div>""", label="users/approvals views")
rep("""  <div id="sb-history" style="display:none">""",
    """  <div id="sb-users" style="display:none">
    <div class="sb-sec"><div class="sb-lbl">▸ Users &amp; Security</div>
      <div style="font-size:11px;color:rgba(255,255,255,0.6);line-height:1.7">Add people, set roles, reset passwords, unlock accounts, import buyers and category heads from the SCR products file, and see the audit trail and backups.</div></div>
  </div>
  <div id="sb-approvals" style="display:none">
    <div class="sb-sec"><div class="sb-lbl">▸ Approvals</div>
      <div style="font-size:11px;color:rgba(255,255,255,0.6);line-height:1.7">Tool 1 requests that came out as <strong>Watch List</strong> wait here for the category head of that MC (from the SCR products file). Only approved quantities count against the day's headroom.</div></div>
  </div>
  <div id="sb-history" style="display:none">""", label="users/approvals sidebars")
rep("""var PVIEWS = ['view-data','view-assess','view-analytics','view-attranalysis','view-aiinsights','view-history'];""",
    """var PVIEWS = ['view-data','view-assess','view-analytics','view-attranalysis','view-aiinsights','view-history','view-approvals','view-users'];""", label="pviews 2")
rep("""var PSB    = ['sb-data','sb-assess','sb-analytics','sb-attranalysis','sb-aiinsights','sb-history'];""",
    """var PSB    = ['sb-data','sb-assess','sb-analytics','sb-attranalysis','sb-aiinsights','sb-history','sb-approvals','sb-users'];""", label="psb 2")
rep("""  ['pt-data','pt-assess','pt-analytics','pt-attranalysis','pt-aiinsights','pt-history'].forEach(function(id) {""",
    """  ['pt-data','pt-assess','pt-analytics','pt-attranalysis','pt-aiinsights','pt-history','pt-approvals','pt-users'].forEach(function(id) {""", label="pt ids 2")
rep("""aiinsights:'sb-aiinsights', history:'sb-history' };""", """aiinsights:'sb-aiinsights', history:'sb-history', approvals:'sb-approvals', users:'sb-users' };""", label="sbmap 2")
rep("""aiinsights:'view-aiinsights', history:'view-history' };""", """aiinsights:'view-aiinsights', history:'view-history', approvals:'view-approvals', users:'view-users' };""", label="vwmap 2")
rep("""  if (tab === 'history' && window.History) History.show();""",
    """  if (tab === 'history' && window.History) History.show();
  if (tab === 'approvals' && window.Approvals) Approvals.show();
  if (tab === 'users' && window.Users) Users.show();""", label="planner show 2")
rep("""  ['ct-request','ct-history','ct-insights','ct-archive','ct-howitworks'].forEach(function(id) {""",
    """  ['ct-request','ct-history','ct-insights','ct-approvals','ct-archive','ct-howitworks'].forEach(function(id) {""", label="ct ids 2")
rep("""  var vw = document.getElementById(tab === 'archive' ? 'view-history' : 'view-cat-' + tab);
  if (vw) vw.style.display = 'block';
  if (tab === 'archive' && window.History) History.show();""",
    """  var vw = document.getElementById(tab === 'archive' ? 'view-history' : tab === 'approvals' ? 'view-approvals' : 'view-cat-' + tab);
  if (vw) vw.style.display = 'block';
  if (tab === 'archive' && window.History) History.show();
  if (tab === 'approvals' && window.Approvals) Approvals.show();""", label="category show 2")
rep("""      + '<td>' + dpill(r.decision) + '</td>'
      + '<td style="font-family:var(--mono);font-weight:700">' + (approved!==null ? fmt(approved) : '—') + '</td>'""",
    """      + '<td>' + dpill(r.decision) + (window.Shared ? Shared.approvalBadge(r) : '') + '</td>'
      + '<td style="font-family:var(--mono);font-weight:700">' + (approved!==null ? fmt(approved) : '—') + '</td>'""", label="history approval badge")
rep("""  const allReqs = S.requests || [];""", """  const allReqs = window.Shared ? Shared.visibleRequests(S.requests || [], true) : (S.requests || []);""", label="insights visibility")
rep("""<div class="us-name">Cat Head Mapping</div><div class="us-desc">Brand×MC CD×MRP → Category Head (email) — powers per-person Watchlist/Reduce-Cancel links</div>""",
    """<div class="us-name">SCR Products / Cat Head Mapping</div><div class="us-desc">Brand×MC CD×MRP → Buyer &amp; Category Head emails. Powers per-person Watchlist links, routes Watch List approvals, and <strong>creates logins</strong> for new buyers and category heads (Users tab).</div>""", label="scr slot label")

# ═════════════════════ 10. OVERLAY, BANNER, SCRIPT ═════════════════════
rep("\n</body>", """
  <!-- shared-server: loading overlay + "new data" banner -->
  <div id="shared-overlay" style="position:fixed;inset:0;z-index:10000;background:rgba(10,20,60,0.55);display:none;align-items:center;justify-content:center">
    <div style="background:#fff;border-radius:14px;padding:22px 28px;box-shadow:0 20px 60px rgba(0,0,0,.3);font-size:13px;color:#1A4FAF;font-weight:600;display:flex;align-items:center;gap:12px">
      <span style="width:18px;height:18px;border:3px solid #C7D9F8;border-top-color:#1A4FAF;border-radius:50%;display:inline-block;animation:sharedspin 0.8s linear infinite"></span>
      <span id="shared-overlay-text">Loading…</span>
    </div>
  </div>
  <div id="shared-update-banner" style="position:fixed;left:50%;top:64px;transform:translateX(-50%);z-index:9500;background:#1A4FAF;color:#fff;border-radius:10px;padding:10px 14px;box-shadow:0 10px 30px rgba(0,0,0,.25);display:none;align-items:center;gap:12px;font-size:12px;max-width:92vw">
    <span id="shared-update-text"></span>
    <button onclick="Shared.applyUpdate()" style="background:#E8006E;color:#fff;border:none;border-radius:7px;padding:6px 12px;font-weight:700;cursor:pointer;font-size:11px">Load now</button>
    <button onclick="this.parentElement.style.display='none'" style="background:transparent;color:#fff;border:1px solid rgba(255,255,255,.4);border-radius:7px;padding:5px 10px;cursor:pointer;font-size:11px">Later</button>
  </div>
  <div id="shared-history-banner" style="position:fixed;left:50%;bottom:18px;transform:translateX(-50%);z-index:9400;background:#4C1D95;color:#fff;border-radius:10px;padding:10px 14px;box-shadow:0 10px 30px rgba(0,0,0,.3);display:none;align-items:center;gap:12px;font-size:12px;max-width:92vw">
    <span id="shared-history-text"></span>
    <button onclick="Shared.exitHistory()" style="background:#E8006E;color:#fff;border:none;border-radius:7px;padding:6px 12px;font-weight:700;cursor:pointer;font-size:11px;white-space:nowrap">Back to current data</button>
  </div>
  <style>@keyframes sharedspin{to{transform:rotate(360deg)}}
    /* top bar: more tabs now (History, Approvals, Users) — keep it on one line */
    .topbar{gap:10px}
    .tb-name{white-space:nowrap}
    #shared-badge{max-width:180px;overflow:hidden;text-overflow:ellipsis}
    @media (max-width:1560px){ .tab-btn{padding:5px 8px} .tb-sub{display:none} #shared-badge{max-width:120px} }
    @media (max-width:1300px){ .tab-btn{font-size:10.5px;padding:5px 6px} #shared-badge{display:none} .tb-name{font-size:15px} }
    @media (max-width:900px){ .topbar{height:auto;flex-wrap:wrap;padding:8px 12px} #planner-tabs,#category-tabs{order:5;width:100%;overflow-x:auto} }
  </style>
  <script src="shared.js"></script>
  <script src="history.js"></script>
  <script src="users.js"></script>
</body>""", label="overlay+script")


# ═════════════════════ 11. RELEASE v1.1 (12 Oct 2026, approved by the business owner) ═════════════════════
# 11a. Category view: Insights tab hidden.
rep("<style>@keyframes sharedspin{to{transform:rotate(360deg)}}",
    "<style>@keyframes sharedspin{to{transform:rotate(360deg)}}\n    #ct-insights{display:none!important}\n    #ai-chat-fab,#ai-chat-panel{display:none!important}", label="hide insights")

# 11b. Single Concept recommendation counts today's approved Tool 1 requests (all users),
#      the same ledger Tool 1 uses for Max Permissible, shown in its own column.
rep("""  // FIX [raw F2 only, no session merge]: this recommendation view should
  // show only PO actually released and sitting in F2 — not today's
  // Tool-1-approved-but-not-yet-in-F2 requests. Merging those in is a
  // separate, deliberate integration to design later — for now, leave them
  // out entirely rather than silently blend two different sources.
  var forecastByMon=_buildF4Map(mc,mrp,brand,flag);
  var f2AllCols=_buildF2RawMap(mc,mrp,brand,flag);
""", """  // Pending PO = released PO in F2 + quantities approved today in Tool 1 (all
  // users), the same ledger Tool 1 uses for Max Permissible. Both are shown
  // separately in the table (Pending PO / Today's approved).
  var forecastByMon=_buildF4Map(mc,mrp,brand,flag);
  var f2OnlyCols=_buildF2RawMap(mc,mrp,brand,flag);
  var f2AllCols=_mergeSessionLedgerIntoF2Map(JSON.parse(JSON.stringify(f2OnlyCols||{})),mc,mrp,brand,flag);
""", label="runB ledger")
rep("""    var mdata=months.map(function(mon){
      if(watchList) return {mon:mon,pend:_ePOWithCarry(f2Raw,mon,_carry),rec:0,str:null,dr:0,action:'Watch List'};
      var cs=_coreColourSTRFast(stk,mixRatio,forecastByMon,f2Raw,mon);
      var pend=_ePOWithCarry(f2Raw,mon,_carry);
      if(!cs) return {mon:mon,pend:pend,rec:0,str:null,dr:0,action:'No data'};
      return {mon:mon,pend:pend,rec:cs.rec,str:cs.str,dr:cs.dr,eff:cs.eff,action:cs.action};
    });""", """    var _f2Only=f2OnlyCols[col]||{};
    var mdata=months.map(function(mon){
      var appr=Math.max(0,((f2Raw||{})[mon]||0)-(_f2Only[mon]||0));
      if(watchList) return {mon:mon,pend:_ePOWithCarry(f2Raw,mon,_carry)-appr,appr:appr,rec:0,str:null,dr:0,action:'Watch List'};
      var cs=_coreColourSTRFast(stk,mixRatio,forecastByMon,f2Raw,mon);
      var pend=_ePOWithCarry(f2Raw,mon,_carry)-appr;
      if(!cs) return {mon:mon,pend:pend,appr:appr,rec:0,str:null,dr:0,action:'No data'};
      return {mon:mon,pend:pend,appr:appr,rec:cs.rec,str:cs.str,dr:cs.dr,eff:cs.eff,action:cs.action};
    });""", label="runB appr per month")
rep("""html+='<th colspan="'+(months.length*2)+'" style="text-align:center;border-left:2px solid rgba(255,255,255,0.2)">Recommendation Qty Month wise</th>';""",
    """html+='<th colspan="'+(months.length*3)+'" style="text-align:center;border-left:2px solid rgba(255,255,255,0.2)">Recommendation Qty Month wise</th>';""", label="rec colspan")
rep("""        html+='<th colspan="2" style="text-align:center;color:#CADCFC;font-size:9px;border-left:2px solid rgba(255,255,255,0.15)">'+m+'</th>';""",
    """        html+='<th colspan="3" style="text-align:center;color:#CADCFC;font-size:9px;border-left:2px solid rgba(255,255,255,0.15)">'+m+'</th>';""", label="rec month colspan")
rep("""      months.forEach(function(){html+='<th style="color:#CADCFC;font-size:9px;border-left:2px solid rgba(255,255,255,0.15)">Pending PO</th><th style="color:#CADCFC;font-size:9px">Max Permissible Qty</th>';});""",
    """      months.forEach(function(){html+='<th style="color:#CADCFC;font-size:9px;border-left:2px solid rgba(255,255,255,0.15)">Pending PO</th><th style="color:#CADCFC;font-size:9px" title="Quantities approved today in Tool 1 (all users), landing in this month">Today\\'s approved</th><th style="color:#CADCFC;font-size:9px">Max Permissible Qty</th>';});""", label="rec header appr")
rep("""          html+='<td style="text-align:right;font-family:var(--mono);color:#9CA3AF;border-left:2px solid #F1F5F9">'+_fmt(md.pend)+'</td>';""",
    """          html+='<td style="text-align:right;font-family:var(--mono);color:#9CA3AF;border-left:2px solid #F1F5F9">'+_fmt(md.pend)+'</td>';
          html+='<td style="text-align:right;font-family:var(--mono);'+(md.appr>0?'color:#7C3AED;font-weight:700':'color:#CBD5E1')+'" title="Approved today in Tool 1, not yet in F2">'+(md.appr>0?_fmt(md.appr):'—')+'</td>';""", label="rec body appr")

# 11c. Revised SCR file (Level 1 / Level 3 columns, MC heading) also maps category heads.
rep("""      const mcCd = r['MC CD']||r['MC_CD']||r['MC Cd']||'';
      const mrp = r['MRP']||'';
      const email = String(r['Category_Email_ID']||r['Category Email ID']||r['Category_Email']||'').trim();""",
    """      const mcCd = r['MC CD']||r['MC_CD']||r['MC Cd']||r['MC']||'';
      const mrp = r['MRP']||'';
      const email = String(r['Category_Email_ID']||r['Category Email ID']||r['Category_Email']||r['Level 3']||'').trim();""", label="cathead map level3")
rep("""    const email = String(r['Category_Email_ID']||r['Category Email ID']||r['Category_Email']||'').trim();
    if(!email || _catHeadDisplayName(email)!==targetName) return;
    const brand = r['Brand Type']||r['Brand']||'';
    const mcCd = r['MC CD']||r['MC_CD']||r['MC Cd']||'';""",
    """    const email = String(r['Category_Email_ID']||r['Category Email ID']||r['Category_Email']||r['Level 3']||'').trim();
    if(!email || _catHeadDisplayName(email)!==targetName) return;
    const brand = r['Brand Type']||r['Brand']||'';
    const mcCd = r['MC CD']||r['MC_CD']||r['MC Cd']||r['MC']||'';""", label="bucket level3")

# 11d. Video tab on the category side (plays video/how-to.mp4 from the app folder).
rep("""    <button id="ct-approvals" onclick="categoryTab('approvals')" """,
    """    <button id="ct-video" onclick="categoryTab('video')" style="padding:5px 14px;border-radius:6px;font-size:11px;font-weight:600;cursor:pointer;border:1px solid rgba(255,255,255,0.2);background:rgba(255,255,255,0.08);color:rgba(255,255,255,0.75);transition:all .18s;font-family:var(--font)">▶ Video</button>
    <button id="ct-approvals" onclick="categoryTab('approvals')" """, label="video tab button")
rep("""  <div id="view-history" style="display:none"></div>""",
    """  <div id="view-cat-video" style="display:none">
    <div style="max-width:1100px;margin:0 auto;padding:22px 24px">
      <div class="pg-title" style="margin-bottom:4px">▶ How to use the tool</div>
      <div class="pg-sub" style="margin-bottom:14px">Topic by topic: single concept, multi concept (true repeat and BOBs), View Recommendation, category head approvals, and how the tool works today and with SAP. Click a topic to jump to it; change the speed below the video.</div>
      <div id="hv-wrap" style="display:grid;grid-template-columns:minmax(0,1fr) 260px;gap:16px;align-items:start">
        <div>
          <video id="howto-video" controls preload="metadata" style="width:100%;border-radius:12px;background:#0d1630;box-shadow:0 10px 30px rgba(15,27,61,.18)"><source src="video/how-to.mp4" type="video/mp4"><source src="video/how-to.webm" type="video/webm"></video>
          <div style="display:flex;flex-wrap:wrap;align-items:center;gap:6px;margin-top:10px;font-size:12px;color:var(--ink3)">
            <span style="font-weight:700;color:var(--ink2)">Speed</span>
            <span id="hv-speeds"></span>
            <span style="margin-left:auto">Questions or problems: 💬 Feedback at the top of the page.</span>
          </div>
        </div>
        <div style="background:#fff;border:1px solid #E5E7EB;border-radius:12px;padding:12px 12px 8px;box-shadow:0 4px 14px rgba(15,27,61,.06)">
          <div style="font-size:11px;font-weight:800;letter-spacing:.06em;text-transform:uppercase;color:#1A4FAF;margin-bottom:8px">Topics</div>
          <div id="hv-chapters" style="font-size:12.5px;color:var(--ink3)">Loading…</div>
        </div>
      </div>
      <style>
        @media (max-width:900px){#hv-wrap{grid-template-columns:1fr!important}}
        .hv-sp{border:1px solid #CBD5E1;background:#fff;border-radius:999px;padding:3px 10px;font-size:11.5px;font-weight:700;cursor:pointer;color:#334155;font-family:var(--font)}
        .hv-sp.on{background:#1A4FAF;border-color:#1A4FAF;color:#fff}
        .hv-ch{display:flex;gap:8px;width:100%;text-align:left;border:0;background:transparent;padding:6px 6px;border-radius:8px;cursor:pointer;font-size:12.5px;color:#1F2937;font-family:var(--font);line-height:1.35}
        .hv-ch:hover{background:#F1F5FF}
        .hv-ch.on{background:#E8EEFF;font-weight:700}
        .hv-ch .t{font-family:var(--mono);font-size:11px;color:#64748B;min-width:38px;padding-top:1px}
      </style>
      <script>
      (function(){
        function v(){return document.getElementById('howto-video');}
        var speeds=[1,1.25,1.5,1.75,2], cur=1, chs=[];
        try{cur=parseFloat(localStorage.getItem('hv-speed'))||1;}catch(e){}
        function drawSpeeds(){
          var el=document.getElementById('hv-speeds'); if(!el) return;
          el.innerHTML=speeds.map(function(s){return '<button class="hv-sp'+(s===cur?' on':'')+'" data-s="'+s+'">'+s+'×</button>';}).join(' ');
          [].forEach.call(el.querySelectorAll('button'),function(b){b.onclick=function(){setSpeed(parseFloat(b.getAttribute('data-s')));};});
        }
        function setSpeed(s){cur=s; var x=v(); if(x) x.playbackRate=s; try{localStorage.setItem('hv-speed',String(s));}catch(e){} drawSpeeds();}
        function fmtT(t){t=Math.floor(t);return Math.floor(t/60)+':'+String(t%60).padStart(2,'0');}
        function drawChapters(){
          var el=document.getElementById('hv-chapters'); if(!el) return;
          if(!chs.length){el.textContent='';return;}
          el.innerHTML=chs.map(function(c,i){return '<button class="hv-ch" data-i="'+i+'"><span class="t">'+fmtT(c.t)+'</span><span>'+String(c.title).replace(/[<>&]/g,'')+'</span></button>';}).join('');
          [].forEach.call(el.querySelectorAll('.hv-ch'),function(b){b.onclick=function(){var x=v(),c=chs[+b.getAttribute('data-i')]; if(!x) return; x.currentTime=c.t; x.playbackRate=cur; x.play();};});
        }
        function mark(){
          var x=v(); if(!x||!chs.length) return; var t=x.currentTime, k=0;
          chs.forEach(function(c,i){if(t>=c.t-0.2) k=i;});
          [].forEach.call(document.querySelectorAll('.hv-ch'),function(b,i){b.classList.toggle('on',i===k);});
        }
        function init(){
          drawSpeeds();
          var x=v(); if(x){ x.addEventListener('loadedmetadata',function(){x.playbackRate=cur;}); x.addEventListener('play',function(){x.playbackRate=cur;}); x.addEventListener('timeupdate',mark); }
          fetch('video/chapters.json',{cache:'no-cache'}).then(function(r){return r.ok?r.json():[];}).then(function(j){chs=Array.isArray(j)?j:[];drawChapters();}).catch(function(){chs=[];drawChapters();});
          // tab order: Video, + New Request, Requests, Approvals, How it works, History
          var rq=document.getElementById('ct-request'), vd=document.getElementById('ct-video'), hw=document.getElementById('ct-howitworks'), ar=document.getElementById('ct-archive');
          if(rq&&vd&&rq.parentNode===vd.parentNode) rq.parentNode.insertBefore(vd,rq);
          if(hw&&ar&&hw.parentNode===ar.parentNode) ar.parentNode.insertBefore(hw,ar);
        }
        if(document.readyState==='loading') document.addEventListener('DOMContentLoaded',init); else init();
      })();
      </script>
    </div>
  </div>
  <div id="view-history" style="display:none"></div>""", label="video view")
rep("""var CVIEWS = ['view-cat-request','view-cat-history','view-cat-insights','view-cat-howitworks'];""",
    """var CVIEWS = ['view-cat-request','view-cat-history','view-cat-insights','view-cat-howitworks','view-cat-video'];""", label="cviews video")
rep("""  ['ct-request','ct-history','ct-insights','ct-approvals','ct-archive','ct-howitworks'].forEach(function(id) {""",
    """  ['ct-request','ct-history','ct-insights','ct-video','ct-approvals','ct-archive','ct-howitworks'].forEach(function(id) {
    if (id !== 'ct-video') { var hv = document.getElementById('howto-video'); if (hv && !hv.paused && tab !== 'video') hv.pause(); }""", label="ct ids video")

# 11g. Buyers / category heads can only validate articles on MCs mapped to them
#      in the SCR products file (server enforces the same rule).
rep("""  if (missing.length) { toast('Upload required files first: ' + missing.join(', ')); return; }
  $g(m.analyzing).classList.add('show');""",
    """  if (missing.length) { toast('Upload required files first: ' + missing.join(', ')); return; }
  if (window.Shared && Shared.user && Shared.user.restricted) {
    var _si = resolveGM(gm) || {};
    if (_si.mcCd) {
      var _sk = [_n(_si.brand), _n(_si.mcCd), _n(_si.mrp)].join('|');
      var _ss = Shared._scope || (Shared._scope = new Set(Shared.user.scopeKeys || []));
      if (!_ss.has(_sk)) {
        toast('Not your MC: ' + (_si.brand||'') + ' · ' + (_si.mcCd||'') + ' · ₹' + (_si.mrp||'') + ' is not mapped to you in the SCR products file. Ask the administrator if this is wrong.');
        return;
      }
    }
  }
  $g(m.analyzing).classList.add('show');""", label="own-MC check")

# 11h. "Today" = the server's date (a PC with a wrong clock must not use another day's ledger).
rep("""function _todayStr(){ const d=new Date(); return""",
    """function _todayStr(){ if(window.Shared&&Shared.serverToday) return Shared.serverToday; const d=new Date(); return""", label="server today")

# 11e. Feedback (all users) + admin roll-up: feedback.js
rep("""  <script src="users.js"></script>""", """  <script src="users.js"></script>
  <script src="feedback.js"></script>""", label="feedback script")

# 11f. How it works: describes the rules as they run.
rep("""<div class="hw-badge">Planning Framework v6 · Dual-Gate Border Zone</div>""",
    """<div class="hw-badge">Rules in use · VMM PO Governance</div>""", label="hw badge")
rep("""<span style="color:#00873E;font-weight:600">Q3 + gap &le; 10% → Allow</span> (was Watch List)<br><span style="color:#D97706;font-weight:600">Q4 + gap &le; 10% → Watch List</span> (was Block)<br>""",
    """<span style="color:#00873E;font-weight:600">Q3 + gap &le; 10% → Allow</span><br><span style="color:#D97706;font-weight:600">Q4 + gap &le; 10% → Watch List</span><br>""", label="hw was")
rep("""F1 Op Stock — same fix applies""", """F1 Op Stock — same rule as Tool 1""", label="hw same fix")
rep("""Legacy MM.YYYY monthly files still supported.""", """Monthly MM.YYYY columns are also accepted.""", label="hw legacy mm")
rep("""<strong>Formula (legacy fallback — monthly F3 only, no daily columns):</strong>""", """<strong>Formula (when F3 has monthly columns only):</strong>""", label="hw legacy formula")
rep("""else legacy monthly average (last N=""", """else monthly average (last N=""", label="hw legacy avg")
rep("""Min peer pool size. If CY BSR &lt; 5 styles &rarr; fallback to LY BSR (SS25+AW25 combined).""",
    """Min peer pool size. If the current season(s) have fewer than 5 qualifying styles &rarr; the fallback season(s) chosen in Season Selection are used (rule: the same season last year, e.g. AW26 &rarr; AW25, SS26 &rarr; SS25).""", label="hw peer min")
rep("""margin-bottom:3px">LY BSR (SS25+AW25)</div><div style="font-size:11px;color:var(--ink2);line-height:1.55">Last year BSR files — fallback peer pool when CY has &lt; 5 qualifying styles.""",
    """margin-bottom:3px">BSR — fallback season</div><div style="font-size:11px;color:var(--ink2);line-height:1.55">BSR of the fallback season(s) chosen in Season Selection — used as the peer pool when the current season has &lt; 5 qualifying styles.""", label="hw ly bsr")
rep("""Peer min=5 not met &rarr; LY SS25+AW25 combined &rarr; 21 styles available.""", """Peer min=5 not met &rarr; fallback season BSR used &rarr; 21 styles available.""", label="hw example fallback")
html = html.replace("&pound;", "₹"); log.append("hw rupee")
rep("""(If D changes, the number of days summed changes — the daily rate shown here assumes the default D=60.)""",
    """(This example uses D=60 to keep the arithmetic simple; the tool uses the D set in Parameters, shown in the table above.)""", label="hw D example")
rep("""<strong>Max Permissible &gt; 0 but &lt; Proposed Qty:</strong> &rarr; <strong>Reduce</strong>. Raise only Max Permissible units.<br><strong>Max Permissible &ge; Proposed Qty:</strong> &rarr; <strong>Allow</strong>. Full quantity fits within headroom.""",
    """<strong>Max Permissible &gt; 0:</strong> the <strong>Confirm Final Quantity</strong> pop-up opens, showing the proposed quantity and the Max Permissible, pre-filled with the Max Permissible. The buyer enters the quantity to raise (up to the Max Permissible) and confirms &rarr; <strong>Auto Allow</strong> at that quantity. <strong>Don't Raise</strong> records 0.<br><strong>The confirmed quantity</strong> counts against the same colour's Max Permissible for every later request that day (all users).""", label="hw step6")
rep("""<div style="font-size:12px;font-weight:700;color:#00873E;margin-bottom:3px">&#10003; Auto Allow</div><div style="font-size:11px;color:var(--ink2)">STR &le; T&times;(1+Buffer%) with proposed qty included (default: STR &le; <span class="hwb-effT">78.75</span>d) &middot; OR Max Permissible &ge; Proposed Qty.</div></div>
          <div style="background:#FFF7ED;border-left:4px solid #D97706;border-radius:0 8px 8px 0;padding:10px 14px;margin-bottom:6px"><div style="font-size:12px;font-weight:700;color:#D97706;margin-bottom:3px">&#8595; Reduce Qty</div><div style="font-size:11px;color:var(--ink2)">STR &gt; effective threshold but Max Permissible &gt; 0. Raise only Max Permissible units — cancel balance.</div></div>""",
    """<div style="font-size:12px;font-weight:700;color:#00873E;margin-bottom:3px">&#10003; Auto Allow (at the confirmed quantity)</div><div style="font-size:11px;color:var(--ink2)">Max Permissible &gt; 0. The buyer confirms a quantity up to the Max Permissible in the pop-up; that quantity is allowed. When the proposed quantity is above the Max Permissible, only the Max Permissible (or less) can be confirmed — raise in SAP only the confirmed quantity.</div></div>""", label="hw outcomes")
rep("""      <button class="hw-tab" onclick="hwTab('t2-export',this)">Tool 2 &middot; Recommendations &amp; Excel Export</button>
    </div>""", """      <button class="hw-tab" onclick="hwTab('t2-export',this)">Tool 2 &middot; Recommendations &amp; Excel Export</button>
      <button class="hw-tab" onclick="hwTab('access',this)">Sign-in, Approvals &amp; Today's Requests</button>
    </div>

    <div id="hw-access" class="hw-tab-panel" style="display:none">
      <div class="hw-note"><strong>Who can do what, how Watch List approvals work, and how today's approved requests are counted.</strong></div>
      <div class="hw-sec-title">Sign-in and roles</div>
      <div style="display:grid;gap:8px;margin-bottom:14px">
        <div style="padding:10px 14px;border:1px solid #1A4FAF22;border-radius:8px;background:#fff;font-size:11.5px;color:var(--ink2);line-height:1.65">Everyone signs in with their official email ID. The first password is the default set by the administrator; the tool asks for a new one at first sign-in (at least 8 characters, a letter and a number). Forgotten password: the administrator resets it from the Users screen. Five wrong attempts lock the account for 15 minutes (the administrator can unlock it).</div>
        <div style="padding:10px 14px;border:1px solid #1A4FAF22;border-radius:8px;background:#fff;font-size:11.5px;color:var(--ink2);line-height:1.65">Logins and responsibilities come from the <strong>SCR Products</strong> file (Brand × MC CD × MRP): <strong>Level 1</strong> (Buyer / Sr Buyer / ACM / BFT) and <strong>Level 2</strong> sign in as buyers and see their own MCs; <strong>Level 3</strong> signs in as Category Head and approves Watch List requests for those MCs. Planners upload data, set parameters and run Tool 2; administrators also manage users.</div>
      </div>
      <div class="hw-sec-title">Watch List approval</div>
      <div style="padding:10px 14px;border:1px solid #D9770622;border-radius:8px;background:#fff;font-size:11.5px;color:var(--ink2);line-height:1.65;margin-bottom:14px">A Tool 1 request that comes out as <strong>Watch List</strong> goes to the Level 3 Category Head of that Brand × MC × MRP, under <strong>Approvals</strong>. The Category Head approves the full quantity or a smaller one, or rejects with a reason. The buyer sees the decision in <strong>Requests</strong>. An approved quantity counts against that colour's Max Permissible for the rest of the day; a rejected request counts as 0.</div>
      <div class="hw-sec-title">Today's approved requests</div>
      <div style="padding:10px 14px;border:1px solid #7C3AED22;border-radius:8px;background:#fff;font-size:11.5px;color:var(--ink2);line-height:1.65;margin-bottom:8px">Every quantity approved today in Tool 1 (Auto Allow at the confirmed quantity, or a Watch List approved by the Category Head) is treated as pending PO in its delivery month for the same Brand × MC × MRP × Flag × Colour. It is used by:<br>&bull; <strong>Tool 1</strong> — later requests for the same colour see the reduced Max Permissible;<br>&bull; <strong>View Recommendation, Single Concept</strong> — shown in the <strong>Today's approved</strong> column next to Pending PO and deducted from the Max Permissible Qty;<br>&bull; <strong>View Recommendation, Multi Concept</strong> — included in the attribute and colour pending PO.<br>Other colours are not affected: each colour has its own share of the plan.</div>
      <div style="padding:10px 14px;border:1px solid #7C3AED22;border-radius:8px;background:#fff;font-size:11.5px;color:var(--ink2);line-height:1.65;margin-bottom:14px">The count covers <strong>today only</strong>. From the next day the tool uses the pending POs in the F2 file uploaded by planning: a PO raised and released in SAP appears there and is counted; a request that was never raised is not. Raise the PO in SAP on the same day you validate it.</div>
      <div class="hw-sec-title">Requests, History, Video and Feedback</div>
      <div style="padding:10px 14px;border:1px solid #1A4FAF22;border-radius:8px;background:#fff;font-size:11.5px;color:var(--ink2);line-height:1.65">&bull; <strong>Requests</strong> — today's checks on your MCs, with the decision, approved quantity and approval status.<br>&bull; <strong>History</strong> — earlier days, by date range (record only; it does not change any calculation).<br>&bull; <strong>Video</strong> — a walk-through of every result type and View Recommendation.<br>&bull; <strong>💬 Feedback</strong> (top of the page) — report a problem or suggest an improvement; the administrator reviews every entry.</div>
    </div>""", label="hw access tab")

open(OUT, "w", encoding="utf-8").write(html)
print("OK — %d patches applied → %s (%d bytes)" % (len(log), OUT, len(html.encode("utf-8"))))
