/* ════════════════════════════════════════════════════════════════════
   VMM PO Governance — shared-server layer
   ────────────────────────────────────────────────────────────────────
   Makes the single-page tool work for a team from one Windows VM:
     • Planner uploads files once → parsed rows are saved on the server
       → every Category user who opens the URL gets the same data.
     • Parameters and Season Selection set by the Planner are shared.
     • Tool 1 requests are saved centrally, so the same-day Max
       Permissible ledger counts EVERY user's approvals, not just the
       ones raised in one browser tab.
     • AI features call the server, which holds the API keys.
   The decision engines themselves are untouched and still run in the
   browser — this file only moves data in and out of S.files / S.params
   / S.requests.
   ════════════════════════════════════════════════════════════════════ */
(function () {
  'use strict';

  var API = 'api/';
  var DATA_KEYS_LABEL = {
    f1: 'F1 — Stock', f2: 'F2 — Pending PO', f3: 'F3 — Sales Trend', f4: 'F4 — Forecast',
    um: 'Universe Master', bsr: 'BSR — All Seasons', artmaster: 'Article Master',
    colmaster: 'Colour Master', attrrel: 'Attr Relevance Master', catheadmap: 'Cat Head Mapping',
    grc: 'GRC — Buying History'
  };
  // Request fields that hold whole BSR row sets — large, and not needed
  // once the result has been rendered. Everything else is kept.
  var REQ_DROP = { peerPool: 1, cleanPool: 1, siblings: 1, qualSibs: 1, sibRanked: 1, repSibling: 1 };
  var PARAM_INPUTS = ['T1', 'T2', 'D', 'mix', 'N', 'wof', 'peer', 'strBuffer', 'borderTolerance'];

  var Shared = {
    online: false,
    status: null,
    dataVersion: 0,
    paramsVersion: 0,
    reqSeq: 0,
    suppress: 0,          // >0 while applying server data (don't echo it back)
    dirtyFiles: {},
    dirtyParams: false,
    pushTimer: null,
    paramTimer: null,
    pushing: false,
    mineOnly: false,
    historyMode: null,    // set while viewing a saved Tool 2 run
    ai: { chat: false, insights: false }
  };
  window.Shared = Shared;

  // ── small helpers ────────────────────────────────────────────────
  function lsGet(k) { try { return localStorage.getItem(k) || ''; } catch (e) { return ''; } }
  function lsSet(k, v) { try { localStorage.setItem(k, v); } catch (e) {} }
  function ssGet(k) { try { return sessionStorage.getItem(k) || ''; } catch (e) { return ''; } }
  function ssSet(k, v) { try { sessionStorage.setItem(k, v); } catch (e) {} }
  function el(id) { return document.getElementById(id); }
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function fmtBytes(n) { return n > 1048576 ? (n / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round(n / 1024)) + ' KB'; }
  function fmtTime(iso) {
    if (!iso) return '';
    var d = new Date(iso);
    return d.toLocaleString('en-IN', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
  }
  // ── signed-in user ────────────────────────────────────────────────
  Shared.user = null;
  Shared.userName = function () { return Shared.user ? Shared.user.name : ''; };
  Shared.hasRole = function () {
    var roles = (Shared.user && Shared.user.roles) || [];
    for (var i = 0; i < arguments.length; i++) if (roles.indexOf(arguments[i]) >= 0) return true;
    return false;
  };
  Shared.isStaff = function () { return Shared.hasRole('admin', 'planner'); };
  function toLogin(change) {
    location.replace('login.html' + (change ? '?change=1' : '?next=' + encodeURIComponent(location.pathname)));
  }
  var _userReady;
  Shared.userReady = new Promise(function (r) { _userReady = r; });

  function headers(extra) {
    var h = { 'X-VMM': '1' };
    if (extra) for (var k in extra) h[k] = extra[k];
    return h;
  }
  Shared.headers = function () { return headers(); };
  async function api(method, path, body, extraHeaders) {
    var opts = { method: method, headers: headers(extraHeaders), cache: 'no-store' };
    if (body !== undefined) {
      if (body instanceof Blob || body instanceof ArrayBuffer || body instanceof Uint8Array) opts.body = body;
      else { opts.body = JSON.stringify(body); opts.headers['Content-Type'] = 'application/json'; }
    }
    var res = await fetch(API + path, opts);
    var data = null;
    var ct = res.headers.get('Content-Type') || '';
    if (ct.indexOf('application/json') >= 0) { try { data = await res.json(); } catch (e) { data = null; } }
    if (res.status === 401) { toLogin(false); throw new Error('Signed out'); }
    if (res.status === 403 && data && data.mustChange) { toLogin(true); throw new Error('Password change needed'); }
    if (res.status === 403) toast('⚠ ' + ((data && data.error) || 'Not allowed for your role'));
    return { ok: res.ok, status: res.status, data: data };
  }

  async function gzip(text) {
    if (typeof CompressionStream === 'undefined') return { body: new Blob([text], { type: 'application/json' }), enc: 'identity' };
    var stream = new Blob([text]).stream().pipeThrough(new CompressionStream('gzip'));
    var buf = await new Response(stream).arrayBuffer();
    return { body: new Blob([buf], { type: 'application/json' }), enc: 'gzip' };
  }

  // ── sync badge in the top bar ──────────────────────────────────────
  function badge(state, text, title) {
    var b = el('shared-badge');
    if (!b) return;
    var colours = { ok: '#16a34a', busy: '#d97706', err: '#dc2626', off: '#6b7280' };
    b.innerHTML = '<span style="display:inline-block;width:7px;height:7px;border-radius:50%;background:' + (colours[state] || '#6b7280') + ';margin-right:6px"></span>' + esc(text);
    b.title = title || '';
  }
  // ── user chip + menu (change password, sign out) ────────────────
  function renderUserChip() {
    var u = el('shared-user');
    if (!u || !Shared.user) return;
    var labels = (Shared.user.roleLabels || []).join(' · ');
    u.innerHTML = '<button id="um-btn" style="display:flex;align-items:center;gap:8px;background:rgba(255,255,255,0.1);border:1px solid rgba(255,255,255,0.22);color:#fff;border-radius:18px;padding:3px 12px 3px 4px;cursor:pointer;font-family:var(--font)">' +
      '<span style="width:24px;height:24px;border-radius:50%;background:#E8006E;display:flex;align-items:center;justify-content:center;font-size:10.5px;font-weight:800">' + esc(initials(Shared.user.name)) + '</span>' +
      '<span style="text-align:left;line-height:1.15"><span style="display:block;font-size:11px;font-weight:700">' + esc(Shared.user.name) + '</span><span style="display:block;font-size:9px;opacity:.7">' + esc(labels) + '</span></span> ▾</button>' +
      '<div id="um-menu" style="display:none;position:absolute;right:0;top:40px;background:#fff;border-radius:10px;box-shadow:0 12px 32px rgba(0,0,0,.25);min-width:220px;overflow:hidden;z-index:9800">' +
        '<div style="padding:10px 14px;border-bottom:1px solid #eef1f7;font-size:11px;color:#6b7a99">' + esc(Shared.user.email) + '</div>' +
        '<a href="javascript:void(0)" id="um-switch" style="display:none;padding:10px 14px;font-size:12.5px;color:#0f1b3d;text-decoration:none">⇄ Switch view</a>' +
        '<a href="login.html?change=1" style="display:block;padding:10px 14px;font-size:12.5px;color:#0f1b3d;text-decoration:none;border-top:1px solid #eef1f7">🔑 Change password</a>' +
        '<a href="javascript:void(0)" id="um-logout" style="display:block;padding:10px 14px;font-size:12.5px;color:#DC2626;text-decoration:none;border-top:1px solid #eef1f7">⎋ Sign out</a>' +
      '</div>';
    u.style.position = 'relative';
    el('um-btn').onclick = function (e) { e.stopPropagation(); var m = el('um-menu'); m.style.display = m.style.display === 'block' ? 'none' : 'block'; };
    document.addEventListener('click', function () { var m = el('um-menu'); if (m) m.style.display = 'none'; });
    el('um-switch').onclick = function () { window.selectRole(S.role === 'planner' ? 'category' : 'planner'); };
    el('um-logout').onclick = async function () {
      if (Object.keys(Shared.dirtyFiles).length && !confirm('Some uploads are still being saved. Sign out anyway?')) return;
      await fetch(API + 'auth/logout', { method: 'POST', headers: headers() });
      location.replace('login.html');
    };
  }
  function initials(n) { return String(n || '?').split(/\s+/).filter(Boolean).slice(0, 2).map(function (w) { return w[0].toUpperCase(); }).join(''); }

  // Kept for older callers: "can this user do planner things?"
  Shared.ensurePlanner = async function () {
    if (Shared.isStaff()) return true;
    toast('Only planners and admins can do this');
    return false;
  };

  // ── watch S.files / S.params for changes made by the page ──────────
  function installWatchers() {
    S.files = new Proxy(S.files, {
      set: function (t, k, v) {
        t[k] = v;
        if (!Shared.suppress && k !== 'grcPeriodType') { Shared.dirtyFiles[k] = true; schedulePush(); }
        if (!Shared.suppress && k === 'grcPeriodType') { Shared.dirtyFiles.grc = true; schedulePush(); }
        return true;
      },
      deleteProperty: function (t, k) {
        delete t[k];
        if (!Shared.suppress) { Shared.dirtyFiles[k] = true; schedulePush(); }
        return true;
      }
    });
    S.params = new Proxy(S.params, {
      set: function (t, k, v) {
        t[k] = v;
        if (!Shared.suppress) { Shared.dirtyParams = true; scheduleParamPush(); }
        return true;
      }
    });
  }

  function isPlanner() { return S.role === 'planner'; }

  function schedulePush() {
    clearTimeout(Shared.pushTimer);
    Shared.pushTimer = setTimeout(pushFiles, 900);
  }
  function scheduleParamPush() {
    clearTimeout(Shared.paramTimer);
    Shared.paramTimer = setTimeout(pushParams, 700);
  }

  function slotLabel(key) {
    var st = el('st-' + key);
    return st ? st.textContent : '';
  }

  async function pushFiles() {
    if (!Shared.online) return;
    if (Shared.historyMode) {
      if (Object.keys(Shared.dirtyFiles).length) toast('Not saved — you are viewing a saved run. Click "Back to current data" before uploading.');
      Shared.dirtyFiles = {};
      return;
    }
    if (!isPlanner()) { Shared.dirtyFiles = {}; return; }
    if (Shared.pushing) { schedulePush(); return; }
    var keys = Object.keys(Shared.dirtyFiles);
    if (!keys.length) return;
    Shared.pushing = true;
    Shared.dirtyFiles = {};
    var uploadId = '';
    try {
      // One upload action (a zip, one or more files) = one Upload ID.
      var src = Shared.pendingSource || { source: 'files', files: [] };
      Shared.pendingSource = null;
      var anyData = keys.some(function (k) { return S.files[k] && S.files[k].length; });
      var b = await api('POST', 'uploads', { source: anyData ? src.source : 'clear', sourceFiles: src.files });
      if (!b.ok) throw new Error((b.data && b.data.error) || ('HTTP ' + b.status) + ' starting upload');
      uploadId = b.data.uploadId;
      for (var i = 0; i < keys.length; i++) {
        var key = keys[i];
        var rows = S.files[key];
        var label = DATA_KEYS_LABEL[key] || key;
        if (!/^[a-z0-9_]+$/i.test(key)) continue;
        if (rows == null || (Array.isArray(rows) && !rows.length)) {
          badge('busy', 'Removing ' + label + '…');
          var del = await api('DELETE', 'data/' + key, undefined, { 'X-Upload-Id': uploadId });
          if (del.ok && del.data) {
            Shared.dataVersion = del.data.dataVersion;
            if (Shared.status && Shared.status.datasets) delete Shared.status.datasets[key];
          }
          continue;
        }
        badge('busy', 'Saving ' + label + ' to server…');
        var payload = { rows: rows };
        if (key === 'grc') payload.meta = { grcPeriodType: S.files.grcPeriodType || null };
        var z = await gzip(JSON.stringify(payload));
        var res = await api('PUT', 'data/' + key, z.body, {
          'Content-Type': 'application/json',
          'X-Body-Encoding': z.enc,
          'X-Rows': String(rows.length || 0),
          'X-Label': encodeURIComponent(slotLabel(key).slice(0, 200)),
          'X-Upload-Id': uploadId
        });
        if (!res.ok) throw new Error((res.data && res.data.error) || ('HTTP ' + res.status) + ' while saving ' + label);
        Shared.dataVersion = res.data.dataVersion;
        if (Shared.status) { Shared.status.datasets = Shared.status.datasets || {}; Shared.status.datasets[key] = res.data.entry; }
        markSlot(key, res.data.entry);
        badge('busy', 'Saved ' + label + ' (' + fmtBytes(z.body.size) + ')');
      }
      Shared.lastUploadId = uploadId;
      badge('ok', 'Saved · ' + uploadId, 'Everyone who opens this page now gets the data you uploaded. Find it in History → Data uploads.');
      toast('Saved to server — Upload ID ' + uploadId);
    } catch (e) {
      console.error(e);
      keys.forEach(function (k) { Shared.dirtyFiles[k] = true; });
      badge('err', 'Save failed — click to retry', e.message);
      toast('⚠ Could not save to server: ' + e.message);
    } finally {
      Shared.pushing = false;
    }
  }

  async function pushParams() {
    if (!Shared.online || !Shared.dirtyParams) return;
    if (Shared.historyMode) { Shared.dirtyParams = false; return; }
    if (!isPlanner()) { Shared.dirtyParams = false; return; }
    Shared.dirtyParams = false;
    Shared.paramsPushing = true;
    var plain = JSON.parse(JSON.stringify(S.params));
    var res;
    try { res = await api('PUT', 'params', plain); } finally { Shared.paramsPushing = false; }
    if (res.ok) { Shared.paramsVersion = res.data.paramsVersion; }
    else { Shared.dirtyParams = true; badge('err', 'Parameters not saved', 'HTTP ' + res.status); }
  }

  // ── apply server data into the page ────────────────────────────────
  function applyParams(p) {
    if (!p) return;
    Shared.suppress++;
    try {
      Object.keys(p).forEach(function (k) { S.params[k] = p[k]; });
      PARAM_INPUTS.forEach(function (k) {
        var inp = el('pp-' + k);
        if (inp && p[k] != null) inp.value = p[k];
      });
      if (typeof RS !== 'undefined') RS.dataVersion = (RS.dataVersion || 0) + 1;
      if (typeof renderEngineBHelp === 'function') renderEngineBHelp();
    } finally { Shared.suppress--; }
  }

  function markSlot(key, info) {
    var slot = el('sl-' + key), st = el('st-' + key);
    if (slot) { slot.classList.add('loaded'); slot.classList.remove('err'); }
    if (st) {
      st.textContent = '✓ ' + (info.rows || 0).toLocaleString('en-IN') + ' rows · ' + (info.uploadId ? info.uploadId + ' · ' : 'on server · ') + (info.updatedBy || '?') + ', ' + fmtTime(info.updatedAt);
      st.className = 'us-st ok';
      st.title = info.label || '';
    }
  }
  function unmarkSlot(key) {
    var slot = el('sl-' + key), st = el('st-' + key);
    if (slot) { slot.classList.remove('loaded'); }
    if (st) { st.textContent = 'Click to upload'; st.className = 'us-st'; }
  }

  async function loadDatasets(keys, manifest) {
    var total = keys.length, done = 0;
    for (var i = 0; i < keys.length; i++) {
      var key = keys[i];
      overlayText('Loading shared data… ' + (DATA_KEYS_LABEL[key] || key) + ' (' + (++done) + '/' + total + ')');
      var res = await fetch(API + 'data/' + key, { cache: 'no-store' });
      if (!res.ok) throw new Error('Could not load ' + key + ' (HTTP ' + res.status + ')');
      var payload = await res.json();
      Shared.suppress++;
      try {
        S.files[key] = payload.rows;
        if (key === 'grc') S.files.grcPeriodType = (payload.meta && payload.meta.grcPeriodType) || (payload.rows[0] && payload.rows[0].periodType) || 'month';
      } finally { Shared.suppress--; }
      markSlot(key, manifest[key]);
    }
  }

  function afterDataChange(changedKeys) {
    if (typeof _idx !== 'undefined') { _idx.um = null; _idx.cybsr = null; _idx.resolveKey = null; }
    if (typeof RS !== 'undefined') RS.dataVersion = (RS.dataVersion || 0) + 1;
    if (typeof updateDots === 'function') updateDots();
    if (changedKeys.indexOf('artmaster') >= 0 && typeof renderSeasonPills === 'function') {
      Shared.suppress++; try { renderSeasonPills(); } finally { Shared.suppress--; }
    }
    if (changedKeys.indexOf('grc') >= 0 && S.files.grc && S.files.grc.length && typeof _renderGRCMonthPicker === 'function') {
      var periods = Array.from(new Set(S.files.grc.map(function (r) { return r.period; }))).sort(function (a, b) {
        var A = a.split('.'), B = b.split('.');
        return (parseInt(A[1]) * 100 + parseInt(A[0])) - (parseInt(B[1]) * 100 + parseInt(B[0]));
      });
      try { _renderGRCMonthPicker(periods); } catch (e) { console.warn(e); }
    }
  }

  async function syncFromServer(opts) {
    opts = opts || {};
    var st = await api('GET', 'status');
    if (!st.ok || !st.data) throw new Error('Server did not respond');
    var s = st.data;
    var prev = Shared.status;
    Shared.status = s;
    if (s.today) Shared.serverToday = s.today;   // the server's date, not this PC's clock
    Shared.ai = s.ai || { chat: false, insights: false };

    if (!prev || s.paramsVersion !== Shared.paramsVersion) {
      if (s.params) applyParams(s.params);
      Shared.paramsVersion = s.paramsVersion;
    }
    var manifest = s.datasets || {};
    var prevManifest = (prev && prev.datasets) || {};
    var changed = [];
    Object.keys(manifest).forEach(function (k) {
      var local = S.files[k];
      var p = prevManifest[k];
      if (!prev || !local || !p || p.updatedAt !== manifest[k].updatedAt) changed.push(k);
    });
    var removed = Object.keys(prevManifest).filter(function (k) { return !manifest[k]; });
    if (opts.initial) {
      // keys that exist locally but not on the server — nothing to do at boot
    }
    if (changed.length) await loadDatasets(changed, manifest);
    removed.forEach(function (k) {
      Shared.suppress++; try { S.files[k] = null; } finally { Shared.suppress--; }
      unmarkSlot(k);
    });
    if (changed.length || removed.length) afterDataChange(changed.concat(removed));
    Shared.dataVersion = s.dataVersion;
    Shared.reqSeq = s.reqSeq || 0;
    return { changed: changed, removed: removed };
  }

  // ── polling: tell users when the planner uploads something new ────
  async function poll() {
    if (!Shared.online) return;
    try {
      var st = await api('GET', 'status');
      if (!st.ok) throw new Error('HTTP ' + st.status);
      var s = st.data;
      if (s.today) Shared.serverToday = s.today;
      if ((s.paramsVersion || 0) > (Shared.paramsVersion || 0) && !Shared.historyMode && !Shared.dirtyParams && !Shared.paramsPushing) {
        applyParams(s.params); Shared.paramsVersion = s.paramsVersion;
      }
      if (s.dataVersion !== Shared.dataVersion && !Shared.pushing && !Shared.historyMode) {
        showUpdateBanner(s);
      }
      if ((s.reqSeq || 0) !== Shared.reqSeq) {
        await Shared.refreshRequests();
        rerenderRequestViews();
      }
      badge('ok', 'Connected · data v' + Shared.dataVersion, 'Shared server: ' + location.host);
      setApprovalCount(s.pendingApprovals || 0);
    } catch (e) {
      badge('err', 'Server unreachable', e.message);
    }
  }
  function showUpdateBanner(s) {
    var b = el('shared-update-banner');
    if (!b) return;
    var latest = null;
    Object.keys(s.datasets || {}).forEach(function (k) {
      var d = s.datasets[k];
      if (!latest || d.updatedAt > latest.updatedAt) latest = Object.assign({ key: k }, d);
    });
    var who = latest ? ((DATA_KEYS_LABEL[latest.key] || latest.key) + ' updated by ' + (latest.updatedBy || '?') + ' at ' + fmtTime(latest.updatedAt)) : 'Shared data changed';
    el('shared-update-text').textContent = '🔄 New data on the server — ' + who + '.';
    b.style.display = 'flex';
  }
  Shared.applyUpdate = async function () {
    if (Shared.historyMode) return Shared.exitHistory();
    el('shared-update-banner').style.display = 'none';
    overlay(true, 'Loading updated data…');
    try {
      var r = await syncFromServer();
      toast('Loaded latest data' + (r.changed.length ? ' (' + r.changed.length + ' file(s))' : ''));
    } catch (e) { toast('⚠ ' + e.message); }
    overlay(false);
  };

  // ── Tool 1 requests: central ledger ────────────────────────────────
  function colourKey(r) {
    var n = function (v) { return String(v || '').trim().replace(/\.0$/, '').toLowerCase(); };
    var i = r.info || {};
    return [n(i.brand), n(i.mcCd), n(i.mrp), n(i.flag), n(r.colour)].join('|');
  }
  function slimRequest(r) {
    var out = {};
    Object.keys(r).forEach(function (k) {
      if (REQ_DROP[k]) return;
      var v = r[k];
      if (typeof v === 'function') return;
      if (v && typeof v === 'object') {
        var s = JSON.stringify(v);
        if (s && s.length > 60000) return; // keep the stored record small
      }
      out[k] = v;
    });
    return out;
  }
  Shared.refreshRequests = async function () {
    if (!Shared.online) return;
    var res = await api('GET', 'requests?date=' + encodeURIComponent(_todayStr()));
    if (!res.ok || !res.data) return;
    S.requests = res.data.requests || [];
    Shared.reqSeq = res.data.seq || 0;
    S.reqCount = S.requests.reduce(function (m, r) { return Math.max(m, r.id || 0); }, 0);
    var cte = el('cat-count-top'); if (cte) cte.textContent = S.requests.length;
  };
  // Saves a freshly-decided request. If someone else approved against the
  // SAME colour after we last refreshed, re-check the headroom first so two
  // people can't both spend the same Max Permissible quantity.
  Shared.saveRequest = async function (r) {
    if (!Shared.online) return;
    r.requestedBy = Shared.userName();
    r.createdAt = new Date().toISOString();
    r.colourKey = colourKey(r);
    var body = { request: slimRequest(r), seenSeq: Shared.reqSeq };
    var res = await api('POST', 'requests', body);
    if (res.status === 409 && res.data) {
      // Someone else saved a request for this colour in the meantime.
      var idx = S.requests.indexOf(r);
      if (idx >= 0) S.requests.splice(idx, 1);
      await Shared.refreshRequests();
      if (r.engine === 'B' && typeof _approvedQtyFor === 'function' && (_approvedQtyFor(r) || 0) > 0) {
        var want = _approvedQtyFor(r);
        var chk = runEngineB(r.gm, r.colour, r.month, want, r.info);
        if ((chk.maxPermQty || 0) < want) {
          var capped = chk.maxPermQty || 0;
          r.finalApprovedQty = capped;
          r.decision = capped > 0 ? 'Auto Allow' : "Hold — Don't Raise";  // same labels as the original v89 rules
          r.decisionReason = 'Headroom changed while you were confirming — ' + (res.data.byOthers || 'another user') +
            ' approved a request for this colour moments earlier. Approved qty capped from ' + fmt(want) + ' to ' + fmt(capped) + ' units. ' + (r.decisionReason || '');
          toast('⚠ Another request for this colour was approved first — your approved qty was capped to ' + fmt(capped));
        }
      }
      S.requests.push(r);
      body = { request: slimRequest(r), seenSeq: Shared.reqSeq, force: true };
      res = await api('POST', 'requests', body);
    }
    if (res.ok && res.data) {
      r.id = res.data.id;
      r.reqId = res.data.reqId;
      r.approval = res.data.approval || null;
      Shared.reqSeq = res.data.seq;
      if (r.approval) {
        var who = (r.approval.approverNames || []).join(', ');
        toast('Watch List — sent to ' + (who || 'the admin (no category head mapped)') + ' for approval');
      }
    } else {
      toast('⚠ Request decided but NOT saved to server (HTTP ' + res.status + ')');
    }
  };
  Shared.clearRequests = async function () {
    if (!window.confirm("Clear ALL of today's requests for EVERY user?\n\nThis resets today's Max Permissible ledger. Watch List requests still waiting for approval are cancelled (shown as 'Cleared'). The requests stay visible in History (marked as cleared).")) return false;
    if (!(await Shared.ensurePlanner())) return false;
    var res = await api('DELETE', 'requests?date=' + encodeURIComponent(_todayStr()));
    if (!res.ok) { toast('⚠ Could not clear (HTTP ' + res.status + ')'); return false; }
    Shared.reqSeq = res.data.seq;
    return true;
  };
  function rerenderRequestViews() {
    var cte = el('cat-count-top'); if (cte) cte.textContent = (S.requests || []).length;
    var hist = el('view-cat-history'), ins = el('view-cat-insights');
    if (hist && hist.style.display !== 'none' && typeof renderCatHistory === 'function') renderCatHistory();
    if (ins && ins.style.display !== 'none' && typeof renderCatInsights === 'function') renderCatInsights();
  }
  // Buyers / category heads see their own requests and those on their MCs.
  Shared.visibleRequests = function (list, ignoreMine) {
    var u = Shared.user;
    if (!u) return list;
    var scope = Shared._scope || (Shared._scope = new Set(u.scopeKeys || []));
    var out = list;
    if (u.restricted) {
      out = out.filter(function (r) {
        if (r.requestedByEmail === u.email) return true;
        if (r.approval && (r.approval.approvers || []).indexOf(u.email) >= 0) return true;
        var i = r.info || {};
        return scope.has([_n(i.brand), _n(i.mcCd), _n(i.mrp)].join('|'));
      });
    }
    if (Shared.mineOnly && !ignoreMine) out = out.filter(function (r) { return r.requestedByEmail === u.email; });
    return out;
  };
  // "⏳ Pending — X" / "✓ Approved by X" / "✕ Rejected by X" under a Watch List pill
  Shared.approvalBadge = function (r) {
    var a = r && r.approval;
    if (!a) return '';
    var st = { pending: ['#B45309', '#FFF7ED', '⏳ Awaiting ' + ((a.approverNames || []).join(', ') || 'admin')],
               approved: ['#00873E', '#DCFCE7', '✓ Approved by ' + (a.byName || a.by || '?') + (a.qty != null ? ' · ' + fmt(a.qty) : '')],
               rejected: ['#DC2626', '#FEF2F2', '✕ Rejected by ' + (a.byName || a.by || '?')],
               cleared: ['#6B7280', '#F3F4F6', '⊘ Cleared by admin – no longer waiting'] }[a.status] || ['#6B7280', '#F3F4F6', a.status];
    return '<div title="' + esc(a.comment || '') + '" style="margin-top:4px;display:inline-block;font-size:9.5px;font-weight:700;color:' + st[0] + ';background:' + st[1] + ';border-radius:8px;padding:2px 7px">' + esc(st[2]) + '</div>';
  };
  Shared.toggleMine = function (on) { Shared.mineOnly = !!on; renderCatHistory(); };

  // ── AI proxy ───────────────────────────────────────────────────────
  Shared.aiChat = async function (system, messages) {
    var res = await api('POST', 'ai/chat', { system: system, messages: messages, max_tokens: 1000 });
    return res;
  };
  Shared.aiInsights = async function (prompt) {
    var res = await api('POST', 'ai/insights', { prompt: prompt });
    return res;
  };

  // ── loading overlay ────────────────────────────────────────────────
  function overlay(show, text) {
    var o = el('shared-overlay');
    if (!o) return;
    o.style.display = show ? 'flex' : 'none';
    if (text) overlayText(text);
  }
  function overlayText(t) { var x = el('shared-overlay-text'); if (x) x.textContent = t; }
  Shared.overlay = overlay;

  // ── Tool 2 runs: save every run, reopen any of them later ─────────
  // Results reference BSR rows (peer pools, siblings) that many lines share.
  // Store each row once and point to it by index, or a run would balloon.
  var POOL_KEYS = ['peerPool', 'cleanPool', 'siblings', 'qualSibs'];
  function packResults(results) {
    var idx = new Map(), rows = [];
    function ref(o) {
      if (!o || typeof o !== 'object') return o;
      var i = idx.get(o);
      if (i === undefined) { i = rows.length; idx.set(o, i); rows.push(o); }
      return i;
    }
    var out = results.map(function (r) {
      var c = {};
      Object.keys(r).forEach(function (k) {
        var v = r[k];
        if (POOL_KEYS.indexOf(k) >= 0 && Array.isArray(v)) c[k] = { __rows: v.map(ref) };
        else if (k === 'sibRanked' && Array.isArray(v)) c[k] = { __sib: v.map(function (x) { var y = Object.assign({}, x); y.row = ref(x.row); return y; }) };
        else if (k === 'repSibling' && v && typeof v === 'object') c[k] = { __row: ref(v) };
        else c[k] = v;
      });
      return c;
    });
    return { rows: rows, results: out };
  }
  function unpackResults(p) {
    var rows = p.rows || [];
    return (p.results || []).map(function (c) {
      Object.keys(c).forEach(function (k) {
        var v = c[k];
        if (v && typeof v === 'object' && !Array.isArray(v)) {
          if (v.__rows) c[k] = v.__rows.map(function (i) { return rows[i]; });
          else if (v.__sib) c[k] = v.__sib.map(function (x) { x.row = rows[x.row]; return x; });
          else if (v.__row !== undefined) c[k] = rows[v.__row];
        }
      });
      return c;
    });
  }
  function runSummary(list) {
    var counts = {}, atRisk = 0, value = 0;
    list.forEach(function (r) {
      counts[r.decision] = (counts[r.decision] || 0) + 1;
      if (r.decision !== 'Retain' && r.decision !== 'Cannot Validate') {
        var cut = r.excessQty != null ? r.excessQty : r.relQty;
        atRisk += cut || 0;
        var rate = r.poRate != null ? r.poRate : Number(r.mrp);
        if (isFinite(rate) && rate > 0) value += rate * (cut || 0);
      }
    });
    return { counts: counts, atRisk: Math.round(atRisk), valueAtRisk: Math.round(value) };
  }
  Shared.saveRun = async function () {
    if (!Shared.online || !isPlanner()) return;
    if (Shared.historyMode) { toast('This re-run on historical data was not saved to History'); return; }
    try {
      if (Object.keys(Shared.dirtyFiles).length || Shared.pushing) {
        clearTimeout(Shared.pushTimer);
        while (Shared.pushing) await new Promise(function (r) { setTimeout(r, 300); });
        await pushFiles();
      }
      var ds = (Shared.status && Shared.status.datasets) || {};
      var used = {}, versions = {};
      Object.keys(DATA_KEYS_LABEL).forEach(function (k) {
        if (S.files[k] && S.files[k].length && ds[k]) { used[k] = ds[k]; versions[k] = ds[k].version; }
      });
      var sum = runSummary(batchResults);
      var payload = { params: JSON.parse(JSON.stringify(S.params)), dataVersions: used, packed: packResults(batchResults) };
      var meta = { lines: batchResults.length, counts: sum.counts, atRisk: sum.atRisk, valueAtRisk: sum.valueAtRisk,
                   dataVersions: versions, T2: S.params.T2, D: S.params.D };
      badge('busy', 'Saving run to History…');
      var z = await gzip(JSON.stringify(payload));
      if (z.enc !== 'gzip') throw new Error('This browser cannot compress — use a current Chrome or Edge');
      var res = await api('POST', 'runs', z.body, { 'Content-Type': 'application/json', 'X-Run-Meta': encodeURIComponent(JSON.stringify(meta)) });
      if (!res.ok) throw new Error((res.data && res.data.error) || ('HTTP ' + res.status));
      badge('ok', 'Run saved to History', 'Open it any time from the History tab.');
      toast('Run saved to History');
    } catch (e) {
      console.error(e);
      badge('err', 'Run not saved to History', e.message);
      toast('⚠ Run finished but was not saved to History: ' + e.message);
    }
  };

  function historyBanner(show, text) {
    var b = el('shared-history-banner');
    if (!b) return;
    b.style.display = show ? 'flex' : 'none';
    if (text) el('shared-history-text').innerHTML = text;
  }
  Shared.enterHistoryRun = async function (id) {
    var res = await fetch(API + 'runs/' + encodeURIComponent(id), { cache: 'no-store' });
    if (!res.ok) throw new Error('HTTP ' + res.status);
    var payload = await res.json();
    var meta = ((History.data && History.data.runs) || []).filter(function (r) { return r.id === id; })[0] || {};
    Shared.historyMode = { id: id, meta: meta };
    el('shared-update-banner').style.display = 'none';
    var used = payload.dataVersions || {}, missing = [];
    var keys = Object.keys(DATA_KEYS_LABEL);
    for (var i = 0; i < keys.length; i++) {
      var k = keys[i], e = used[k];
      Shared.suppress++;
      try {
        if (!e) { S.files[k] = null; unmarkSlot(k); continue; }
        overlayText('Loading ' + DATA_KEYS_LABEL[k] + ' as of ' + fmtTime(e.updatedAt) + '…');
        var r = await fetch(API + 'data/' + k + '?version=' + encodeURIComponent(e.version), { cache: 'no-store' });
        if (!r.ok) { missing.push(DATA_KEYS_LABEL[k]); S.files[k] = null; unmarkSlot(k); continue; }
        var d = await r.json();
        S.files[k] = d.rows;
        if (k === 'grc') S.files.grcPeriodType = (d.meta && d.meta.grcPeriodType) || 'month';
        markSlot(k, e);
        var st = el('st-' + k); if (st) st.textContent = '📜 version of ' + fmtTime(e.updatedAt) + ' (history)';
      } finally { Shared.suppress--; }
    }
    applyParams(payload.params);
    afterDataChange(keys);
    batchResults = unpackResults(payload.packed || {});
    renderAssessmentDashboard();
    try { renderPlannerDashboard(); } catch (e2) { console.warn(e2); }
    try { _exportCatHeadSelection.clear(); _syncCatHeadExportPanels(); } catch (e3) {}
    var lc = el('ass-line-count'); if (lc) lc.textContent = batchResults.length;
    var st2 = el('ass-status'); if (st2) st2.textContent = '📜 Saved run — ' + batchResults.length + ' lines';
    historyBanner(true, '📜 <strong>Viewing saved run</strong> of ' + esc(fmtTime(meta.at)) + (meta.by ? ' by ' + esc(meta.by) : '') +
      ' — dashboard, tables, Excel export and parameters are as they were then.' +
      (missing.length ? ' <span style="color:#FDE68A">Not available any more: ' + esc(missing.join(', ')) + '.</span>' : ''));
    badge('busy', 'Viewing history', 'Uploads and parameter changes are not saved while viewing history.');
  };
  Shared.exitHistory = async function () {
    overlay(true, 'Loading current data…');
    try {
      Shared.historyMode = null;
      historyBanner(false);
      Shared.suppress++;
      try { Object.keys(DATA_KEYS_LABEL).forEach(function (k) { S.files[k] = null; unmarkSlot(k); }); } finally { Shared.suppress--; }
      Shared.status = null; Shared.paramsVersion = -1;
      await syncFromServer();
      afterDataChange(Object.keys(DATA_KEYS_LABEL));
      batchResults = [];
      var r = el('ass-results'); if (r) r.style.display = 'none';
      var e = el('ass-empty'); if (e) e.style.display = 'block';
      var lc = el('ass-line-count'); if (lc) lc.textContent = '0';
      var st = el('ass-status'); if (st) st.textContent = '';
      badge('ok', 'Connected · data v' + Shared.dataVersion);
      toast('Back to current data');
    } catch (err) { toast('⚠ ' + err.message); }
    overlay(false);
  };

  // ── views allowed by the user's roles ──────────────────────────────
  // Planner view: admin / planner.  Category view: everyone who can raise
  // Tool 1 requests (buyers, category heads — and planners/admins too).
  function allowedViews() {
    var v = [];
    if (Shared.isStaff()) v.push('planner');
    if (Shared.hasRole('buyer', 'cathead', 'planner', 'admin')) v.push('category');
    return v;
  }
  var _origSelectRole = window.selectRole;
  window.selectRole = async function (role) {
    await Shared.userReady;
    var views = allowedViews();
    if (views.indexOf(role) < 0) role = views[0] || 'category';
    var ov = el('role-overlay'); if (ov) ov.classList.remove('visible');
    _origSelectRole(role);
    if (role !== 'planner') { Shared.dirtyFiles = {}; Shared.dirtyParams = false; }
    applyRoleUi();
  };
  var _origOpenRole = window.openRoleDialog;
  window.openRoleDialog = function () {
    var views = allowedViews();
    if (views.length < 2) return;
    _origOpenRole();
  };
  function applyRoleUi() {
    var views = allowedViews();
    // the view switch lives in the user menu now (the top bar is full)
    document.querySelectorAll('.role-btn').forEach(function (b) { b.style.display = 'none'; });
    var sw = el('um-switch');
    if (sw) { sw.style.display = views.length > 1 ? 'block' : 'none'; sw.textContent = S.role === 'planner' ? '⇄ Switch to Category view (raise requests)' : '⇄ Switch to Planner view'; }
    var pc = document.querySelector('.rcard.plan'), cc = document.querySelector('.rcard.cat');
    if (pc) pc.style.display = views.indexOf('planner') >= 0 ? '' : 'none';
    if (cc) cc.style.display = views.indexOf('category') >= 0 ? '' : 'none';
    var show = function (id, on) { var x = el(id); if (x) x.style.display = on ? '' : 'none'; };
    show('pt-users', Shared.hasRole('admin'));
    show('pt-approvals', Shared.isStaff());
    show('ct-approvals', Shared.hasRole('cathead', 'admin', 'planner'));
    Array.prototype.forEach.call(document.querySelectorAll('[onclick="clearAllRequests()"]'), function (b) { b.style.display = Shared.isStaff() ? '' : 'none'; });
    var tb = el('tb-badge');
    if (tb && Shared.user) tb.textContent = (S.role === 'planner' ? 'Planner View' : 'Category View');
  }
  Shared.applyRoleUi = applyRoleUi;
  function setApprovalCount(n) {
    ['pt-approvals', 'ct-approvals'].forEach(function (id) {
      var b = el(id); if (!b) return;
      var c = b.querySelector('.appr-count');
      if (!c) { c = document.createElement('span'); c.className = 'appr-count'; c.style.cssText = 'margin-left:6px;background:#E8006E;color:#fff;border-radius:9px;padding:0 6px;font-size:10px;font-weight:800'; b.appendChild(c); }
      c.textContent = n; c.style.display = n ? '' : 'none';
    });
  }
  Shared.setApprovalCount = setApprovalCount;

  // Remember which file(s) the planner picked, so the Upload ID can list them.
  document.addEventListener('change', function (ev) {
    var t = ev.target;
    if (!t || t.type !== 'file' || !t.files || !t.files.length) return;
    var slot = t.closest && t.closest('.uslot');
    var id = slot ? slot.id : '';
    var source = id === 'sl-zipbundle' ? 'zip' : id === 'sl-rawimport' ? 'raw' : 'files';
    var names = Array.prototype.map.call(t.files, function (f) { return f.name; });
    if (Shared.pendingSource && Shared.pendingSource.source === source) names = Shared.pendingSource.files.concat(names);
    Shared.pendingSource = { source: source, files: names };
  }, true);

  // ── boot ───────────────────────────────────────────────────────────
  installWatchers();
  document.addEventListener('DOMContentLoaded', async function () {
    var bb = el('shared-badge');
    if (bb) bb.onclick = function () { if (Object.keys(Shared.dirtyFiles).length) pushFiles(); else poll(); };
    if (location.protocol === 'file:') {
      badge('off', 'Offline copy — not shared', 'Open this page through the server URL.');
      Shared.user = { name: 'Offline', roles: ['admin', 'planner'], roleLabels: [] };
      _userReady();
      return;
    }
    try {
      var me = await fetch(API + 'auth/me', { cache: 'no-store' });
      if (me.status === 401) { toLogin(false); return; }
      var md = await me.json();
      if (md.user.mustChange) { toLogin(true); return; }
      Shared.user = md.user;
    } catch (e) {
      badge('err', 'Server unreachable', e.message);
      overlay(true, 'Cannot reach the server — check your network and reload.');
      return;
    }
    _userReady();
    renderUserChip();
    // pick the view: last used if allowed, else the first allowed
    var saved = lsGet('vmm_role');
    var views = allowedViews();
    await window.selectRole(views.indexOf(saved) >= 0 ? saved : views[0]);
    overlay(true, 'Loading shared data…');
    try {
      await syncFromServer({ initial: true });
      Shared.online = true;
      await Shared.refreshRequests();
      rerenderRequestViews();
      badge('ok', 'Connected · data v' + Shared.dataVersion, 'Shared server: ' + location.host);
      setApprovalCount((Shared.status && Shared.status.pendingApprovals) || 0);
    } catch (e) {
      console.error(e);
      badge('err', 'Server unreachable', e.message);
      toast('⚠ Could not load shared data: ' + e.message);
    }
    overlay(false);
    setInterval(poll, 30000);
  });
})();
