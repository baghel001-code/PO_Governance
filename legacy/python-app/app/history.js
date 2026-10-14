/* ════════════════════════════════════════════════════════════════════
   History — everything uploaded and every result, kept on the server,
   browsable by date range.
     • Tool 2 runs     — every "Run Assessment" is saved automatically.
                         Open one to see the dashboard exactly as it was,
                         with the data files and parameters it used.
     • Tool 1 requests — every request from every user, incl. cleared ones.
     • Data uploads    — every file version ever uploaded; download any
                         of them as Excel, or make an older one current.
   ════════════════════════════════════════════════════════════════════ */
(function () {
  'use strict';
  var H = { tab: 'runs', from: '', to: '', data: {}, reqFilter: { q: '', dec: '' }, upQ: '', runQ: '', openUpload: null };
  window.History = H;

  var LABEL = {
    f1: 'F1 — Stock', f2: 'F2 — Pending PO', f3: 'F3 — Sales Trend', f4: 'F4 — Forecast',
    um: 'Universe Master', bsr: 'BSR — All Seasons', artmaster: 'Article Master',
    colmaster: 'Colour Master', attrrel: 'Attr Relevance Master', catheadmap: 'Cat Head Mapping',
    grc: 'GRC — Buying History'
  };
  function el(id) { return document.getElementById(id); }
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function ymd(d) { return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0'); }
  function when(iso) {
    if (!iso) return '—';
    return new Date(iso).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }
  function n(v) { return v == null || v === '' ? '—' : Math.round(v).toLocaleString('en-IN'); }
  function mb(b) { return b > 1048576 ? (b / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round((b || 0) / 1024)) + ' KB'; }
  function isPlanner() { return S.role === 'planner'; }
  async function getJSON(path) {
    var r = await fetch('api/' + path, { cache: 'no-store' });
    var d = await r.json().catch(function () { return {}; });
    if (!r.ok) throw new Error(d.error || ('HTTP ' + r.status));
    return d;
  }
  function qs() { return 'from=' + encodeURIComponent(H.from) + '&to=' + encodeURIComponent(H.to); }

  // ── page skeleton ─────────────────────────────────────────────────
  function skeleton() {
    var v = el('view-history');
    if (v.dataset.ready) return;
    v.dataset.ready = '1';
    var today = new Date();
    H.to = ymd(today);
    H.from = ymd(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 29));
    v.innerHTML =
      '<div style="margin-bottom:18px;display:flex;align-items:flex-start;gap:12px;flex-wrap:wrap"><div style="flex:1;min-width:280px"><div class="pg-title">🗂 History</div>' +
      '<div style="height:3px;width:60px;background:linear-gradient(90deg,var(--vmm-pink),var(--vmm-cyan));border-radius:2px;margin:5px 0 6px"></div>' +
      '<div class="pg-sub">Everything uploaded and every result, kept on the server. Pick a date range, then open, filter or download.</div></div>' +
      '<div style="display:flex;gap:8px;align-items:center" id="h-roleback"></div></div>' +
      '<div class="card" style="padding:14px 16px;margin-bottom:16px;display:flex;flex-wrap:wrap;gap:10px;align-items:flex-end">' +
        '<div><div style="font-size:10px;font-weight:700;color:var(--ink3);text-transform:uppercase;margin-bottom:4px">From</div><input type="date" id="h-from" style="padding:7px 9px;border:1.5px solid var(--brd2);border-radius:7px;font-family:var(--font)"></div>' +
        '<div><div style="font-size:10px;font-weight:700;color:var(--ink3);text-transform:uppercase;margin-bottom:4px">To</div><input type="date" id="h-to" style="padding:7px 9px;border:1.5px solid var(--brd2);border-radius:7px;font-family:var(--font)"></div>' +
        '<div style="display:flex;gap:6px;flex-wrap:wrap">' +
          ['Today:0', 'Last 7 days:6', 'Last 30 days:29', 'This month:m', 'Last 90 days:89', 'All:all'].map(function (p) {
            var a = p.split(':');
            return '<button class="btn-sec" style="font-size:11px;padding:6px 11px" onclick="History.preset(\'' + a[1] + '\')">' + a[0] + '</button>';
          }).join('') +
        '</div>' +
        '<button class="btn-pri" style="font-size:12px;padding:8px 16px;margin-left:auto" onclick="History.load()">Show</button>' +
      '</div>' +
      '<div style="display:flex;gap:6px;margin-bottom:12px" id="h-tabs"></div>' +
      '<div id="h-body"></div>';
    el('h-from').value = H.from; el('h-to').value = H.to;
    el('h-from').onchange = function () { H.from = this.value; H.load(); };
    el('h-to').onchange = function () { H.to = this.value; H.load(); };
  }
  function tabs() {
    var list = Shared.isStaff()
      ? [['runs', '📋 Tool 2 runs'], ['requests', '✏️ Tool 1 requests'], ['uploads', '📂 Data uploads']]
      : [['requests', '✏️ Tool 1 requests']];
    if (!list.some(function (t) { return t[0] === H.tab; })) H.tab = list[0][0];
    el('h-tabs').innerHTML = list.map(function (t) {
      var on = t[0] === H.tab;
      return '<button onclick="History.setTab(\'' + t[0] + '\')" style="padding:7px 14px;border-radius:8px;font-size:12px;font-weight:700;cursor:pointer;font-family:var(--font);border:1.5px solid ' +
        (on ? 'var(--vmm-pink)' : 'var(--brd2)') + ';background:' + (on ? 'var(--vmm-pink)' : '#fff') + ';color:' + (on ? '#fff' : 'var(--ink2)') + '">' + t[1] +
        (H.data[t[0]] ? ' <span style="opacity:.8;font-weight:500">(' + H.data[t[0]].length + ')</span>' : '') + '</button>';
    }).join('');
  }

  function roleButtons() {
    var btn = function (role, label, colour) {
      var on = S.role === role;
      return '<button onclick="History.backTo(\'' + role + '\')" title="Leave History and go to the ' + label + ' screens" style="padding:8px 14px;border-radius:8px;font-size:12px;font-weight:700;cursor:pointer;font-family:var(--font);border:1.5px solid ' + colour + ';background:' + (on ? colour : '#fff') + ';color:' + (on ? '#fff' : colour) + '">← ' + label + (on ? ' (you)' : '') + '</button>';
    };
    var html = '';
    if (Shared.isStaff()) html += btn('planner', 'Planner', '#E8006E');
    html += btn('category', Shared.isStaff() ? 'Category' : 'New Request', '#1A4FAF');
    el('h-roleback').innerHTML = html;
  }
  H.backTo = function (role) {
    if (Shared.historyMode) Shared.exitHistory();
    if (S.role === role) { if (role === 'planner') plannerTab('data'); else categoryTab('request'); return; }
    selectRole(role);
  };
  H.show = function () { skeleton(); roleButtons(); H.openUpload = null; tabs(); H.load(); };
  H.setTab = function (t) { H.tab = t; H.openUpload = null; tabs(); H.render(); };
  H.preset = function (p) {
    var t = new Date(), f;
    if (p === 'all') { H.from = ''; H.to = ''; }
    else {
      if (p === 'm') f = new Date(t.getFullYear(), t.getMonth(), 1);
      else f = new Date(t.getFullYear(), t.getMonth(), t.getDate() - parseInt(p, 10));
      H.from = ymd(f); H.to = ymd(t);
    }
    el('h-from').value = H.from; el('h-to').value = H.to;
    H.load();
  };
  H.load = async function () {
    if (H.from && H.to && H.from > H.to) { toast('"From" date is after "To" date'); return; }
    el('h-body').innerHTML = '<div style="padding:40px;text-align:center;color:var(--ink3)">Loading…</div>';
    try {
      var staff = Shared.isStaff();
      var jobs = [getJSON('history/requests?' + qs())];
      if (staff) { jobs.push(getJSON('history/uploads?' + qs())); jobs.push(getJSON('runs?' + qs())); }
      var res = await Promise.all(jobs);
      H.data.requests = res[0].items;
      H.data.uploads = staff ? res[1].items : null; H.storage = staff ? res[1].storageBytes : 0;
      H.data.runs = staff ? res[2].items : null;
      tabs(); H.render();
    } catch (e) {
      el('h-body').innerHTML = '<div style="padding:40px;text-align:center;color:#DC2626">Could not load history: ' + esc(e.message) + '</div>';
    }
  };

  function table(head, rows, empty) {
    if (!rows.length) return '<div class="card" style="padding:40px;text-align:center;color:var(--ink3)">' + empty + '</div>';
    return '<div class="card" style="padding:0;overflow:hidden"><div style="overflow-x:auto;max-height:68vh"><table class="sum-tbl" style="width:100%"><thead><tr>' +
      head.map(function (h) { return '<th style="position:sticky;top:0;z-index:1">' + h + '</th>'; }).join('') +
      '</tr></thead><tbody>' + rows.join('') + '</tbody></table></div></div>';
  }
  var rangeTxt = function () { return H.from || H.to ? (H.from || 'start') + ' → ' + (H.to || 'today') : 'all dates'; };

  H.render = function () {
    var body = el('h-body');
    if (H.tab === 'runs') { renderRuns(body); return wireSearch('h-run-q', function (v) { H.runQ = v; }); }
    if (H.tab === 'requests') return renderRequests(body);
    return renderUploads(body);
  };

  // ── Tool 2 runs ───────────────────────────────────────────────────
  function renderRuns(body) {
    var q = H.runQ.toLowerCase();
    var items = (H.data.runs || []).filter(function (r) { return !q || [r.runNo, r.by].join(' ').toLowerCase().indexOf(q) >= 0; });
    var rows = items.map(function (r) {
      var c = r.counts || {};
      var files = Object.keys(r.dataVersions || {}).map(function (k) { return LABEL[k] || k; }).join(', ');
      return '<tr><td><a href="javascript:void(0)" onclick="History.openRun(\'' + r.id + '\')" style="font-family:var(--mono);font-weight:700;color:var(--vmm-pink);white-space:nowrap">' + esc(r.runNo) + '</a></td>' +
        '<td style="white-space:nowrap">' + when(r.at) + '</td><td>' + esc(r.by) + '</td>' +
        '<td style="font-family:var(--mono)">' + n(r.lines) + '</td>' +
        '<td style="color:#00873E;font-family:var(--mono)">' + n(c['Retain'] || 0) + '</td>' +
        '<td style="color:#D97706;font-family:var(--mono)">' + n(c['Reduce Qty'] || 0) + '</td>' +
        '<td style="color:#DC2626;font-family:var(--mono)">' + n(c['Cancel'] || 0) + '</td>' +
        '<td style="color:#92400E;font-family:var(--mono)">' + n(c['Watch List'] || 0) + '</td>' +
        '<td style="font-family:var(--mono);font-weight:700;color:#E8006E">' + n(r.atRisk) + '</td>' +
        '<td style="font-family:var(--mono)">' + (r.valueAtRisk ? '₹' + fmtCr(r.valueAtRisk) : '—') + '</td>' +
        '<td style="font-size:10.5px;color:var(--ink3);max-width:220px">' + esc(files) + '</td>' +
        '<td style="white-space:nowrap"><button class="btn-pri" style="font-size:11px;padding:5px 12px" onclick="History.openRun(\'' + r.id + '\')">Open</button> ' +
        '<button class="btn-sec" style="font-size:11px;padding:5px 9px" title="Delete this saved run" onclick="History.deleteRun(\'' + r.id + '\')">🗑</button></td></tr>';
    });
    body.innerHTML = searchBox('h-run-q', H.runQ, 'Search Run ID or user…') + '<div style="font-size:11.5px;color:var(--ink2);margin-bottom:10px">Every <strong>Run Assessment</strong> is saved automatically with its own Run ID. <strong>Open</strong> shows that run\'s dashboard, tables and Excel export exactly as they were, using the data files and parameters of that moment. ' + items.length + ' run(s), ' + rangeTxt() + '.</div>' +
      table(['Run ID', 'Run at', 'By', 'Lines', 'Retain', 'Reduce', 'Cancel', 'Watch', 'Qty at risk', '₹ at risk', 'Data used', ''], rows, 'No saved runs in this date range.');
  }

  function searchBox(id, val, ph) {
    return '<input id="' + id + '" placeholder="' + ph + '" value="' + esc(val) + '" style="padding:7px 10px;border:1.5px solid var(--brd2);border-radius:7px;min-width:260px;font-family:var(--font);margin-bottom:10px">';
  }
  function wireSearch(id, setter) {
    var x = el(id); if (!x) return;
    x.oninput = function () { var p = this.selectionStart; setter(this.value); H.render(); var y = el(id); if (y) { y.focus(); y.setSelectionRange(p, p); } };
  }

  // ── Tool 1 requests ───────────────────────────────────────────────
  function reqRows() {
    var q = H.reqFilter.q.toLowerCase(), dec = H.reqFilter.dec;
    return (H.data.requests || []).filter(function (r) {
      if (dec && r.decision !== dec) return false;
      if (!q) return true;
      var i = r.info || {};
      return [r.reqId, r.gm, r.colour, r.requestedBy, i.mcDesc, i.brand, i.div].join(' ').toLowerCase().indexOf(q) >= 0;
    });
  }
  function renderRequests(body) {
    var all = H.data.requests || [];
    var decs = Array.from(new Set(all.map(function (r) { return r.decision; }))).sort();
    var list = reqRows();
    var totalProp = list.reduce(function (s, r) { return s + (r.qty || 0); }, 0);
    var totalAppr = list.reduce(function (s, r) { var a = _approvedQtyFor(r); return s + (a || 0); }, 0);
    var rows = list.map(function (r) {
      var i = r.info || {}, a = _approvedQtyFor(r);
      var open = H.openReq === r.reqId;
      var detail = !open ? '' : '<tr><td colspan="13" style="background:#F8FAFF;padding:12px 16px;font-size:12px;line-height:1.7">' +
        '<strong>' + esc(r.reqId) + '</strong> · ' + esc(i.brand || '') + ' · ' + esc(i.div || '') + ' · MC ' + esc(i.mcCd || '') + ' ' + esc(i.mcDesc || '') + ' · Flag ' + esc(i.flag || '—') +
        (r.maxPermQty != null ? ' · Max Permissible ' + n(r.maxPermQty) : '') + (r.artSTR != null ? ' · Colour STR ' + Math.round(r.artSTR) + 'd' : '') +
        (r.quartile_ ? ' · ' + esc(r.quartile_) + (r.percentile != null ? ' (' + r.percentile + 'th pct)' : '') : '') +
        '<div style="margin-top:6px;color:var(--ink2)"><strong>Reason:</strong> ' + esc(r.decisionReason || '—') + '</div></td></tr>';
      return '<tr style="' + (r.cleared ? 'opacity:.55' : '') + '"><td style="white-space:nowrap"><a href="javascript:void(0)" onclick="History.toggleReq(\'' + esc(r.reqId) + '\')" style="font-family:var(--mono);font-size:10.5px;font-weight:700;color:var(--vmm-pink)">' + (open ? '▾ ' : '▸ ') + esc(r.reqId || '—') + '</a></td><td style="white-space:nowrap">' + when(r.savedAt || r.createdAt) + '</td>' +
        '<td>' + esc(r.requestedBy || '—') + '</td><td style="font-family:var(--mono)">' + esc(r.gm) + '</td><td>' + esc(r.colour) + '</td>' +
        '<td style="font-size:11px">' + esc(i.mcDesc || '—') + '</td><td>' + (i.mrp ? '₹' + esc(i.mrp) : '—') + '</td><td>' + esc(r.month) + '</td>' +
        '<td style="font-family:var(--mono)">' + n(r.qty) + '</td><td style="font-size:10px">' + esc(r.engine === 'B' ? 'Single Concept' : r.engine === 'A' ? 'Multi · ' + (r.optionType || '') : 'New') + '</td>' +
        '<td>' + dpill(r.decision) + (r.cleared ? '<div style="font-size:9px;color:var(--ink3)">cleared by ' + esc(r.cleared.by || '?') + '</div>' : '') + '</td>' +
        '<td style="font-family:var(--mono);font-weight:700">' + (a != null ? n(a) : '—') + '</td>' +
        '<td style="font-size:10.5px;color:var(--ink2);max-width:320px">' + esc(String(r.decisionReason || '').slice(0, 180)) + '</td></tr>' + detail;
    });
    body.innerHTML =
      '<div style="display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin-bottom:10px">' +
        '<input id="h-req-q" placeholder="Search Request ID, GM, colour, user, MC…" value="' + esc(H.reqFilter.q) + '" style="padding:7px 10px;border:1.5px solid var(--brd2);border-radius:7px;min-width:240px;font-family:var(--font)">' +
        '<select id="h-req-dec" style="padding:7px 10px;border:1.5px solid var(--brd2);border-radius:7px;font-family:var(--font)"><option value="">All decisions</option>' +
          decs.map(function (d) { return '<option' + (d === H.reqFilter.dec ? ' selected' : '') + '>' + esc(d) + '</option>'; }).join('') + '</select>' +
        '<span style="font-size:11.5px;color:var(--ink2)">' + list.length + ' request(s) · proposed ' + n(totalProp) + ' · approved ' + n(totalAppr) + ' · ' + rangeTxt() + '</span>' +
        '<button class="btn-pri" style="font-size:11px;padding:6px 13px;margin-left:auto" onclick="History.exportRequests()">⬇ Excel</button>' +
      '</div>' +
      table(['Request ID', 'Saved at', 'By', 'Generic Material', 'Colour', 'MC Desc', 'MRP', 'Del. Month', 'Proposed', 'Engine', 'Decision', 'Approved', 'Reason'], rows,
        'No Tool 1 requests in this date range.');
    el('h-req-q').oninput = function () { H.reqFilter.q = this.value; var p = this.selectionStart; renderRequests(body); var x = el('h-req-q'); x.focus(); x.setSelectionRange(p, p); };
    el('h-req-dec').onchange = function () { H.reqFilter.dec = this.value; renderRequests(body); };
  }
  H.toggleReq = function (id) { H.openReq = H.openReq === id ? null : id; H.render(); };
  H.exportRequests = function () {
    var list = reqRows();
    if (!list.length) { toast('Nothing to export'); return; }
    var rows = list.map(function (r) {
      var i = r.info || {};
      return {
        'Request ID': r.reqId || '', 'Saved At': when(r.savedAt || r.createdAt), 'Requested By': r.requestedBy || '', 'Request Date': r.reqDate || '',
        'Generic Material': r.gm, 'Colour': r.colour, 'Brand': i.brand || '', 'Division': i.div || '', 'MC CD': i.mcCd || '',
        'MC Desc': i.mcDesc || '', 'MRP': i.mrp || '', 'Flag': i.flag || '', 'Delivery Month': r.month, 'Proposed Qty': r.qty,
        'Engine': r.engine === 'B' ? 'Single Concept MRP' : r.engine === 'A' ? 'Multi Concept MRP' : 'New Option',
        'Option Type': r.optionType || '', 'Decision': r.decision, 'Approved Qty': _approvedQtyFor(r),
        'Max Permissible Qty': r.maxPermQty != null ? r.maxPermQty : '', 'Colour STR (days)': r.artSTR != null ? Math.round(r.artSTR) : '',
        'Quartile': r.quartile_ || '', 'Decision Reason': r.decisionReason || '', 'Cleared': r.cleared ? ('by ' + (r.cleared.by || '?') + ' ' + when(r.cleared.at)) : ''
      };
    });
    var wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, XLSX.utils.json_to_sheet(rows), 'Tool 1 Requests');
    XLSX.writeFile(wb, 'Tool1_Requests_' + (H.from || 'all') + '_to_' + (H.to || 'today') + '.xlsx');
  };

  // ── Data uploads (one row per Upload ID) ──────────────────────────
  var SRC_LABEL = { zip: 'ZIP bundle', raw: 'Raw import', files: 'File upload', file: 'File upload', restore: 'Made current again', clear: 'Cleared', upload: 'File upload' };
  function fileChips(files) {
    return files.map(function (f) {
      var c = f.action === 'clear' ? '#DC2626' : f.current ? '#00873E' : '#6B7280';
      return '<span style="display:inline-block;margin:1px 3px 1px 0;padding:1px 7px;border-radius:9px;font-size:10px;font-weight:600;border:1px solid ' + c + '55;color:' + c + ';background:' + c + '0D">' +
        esc((LABEL[f.key] || f.key).split(' — ')[0]) + (f.action === 'clear' ? ' ✕' : '') + '</span>';
    }).join('');
  }
  function renderUploads(body) {
    if (H.openUpload) return renderUploadDetail(body, H.openUpload);
    var q = H.upQ.toLowerCase();
    var items = (H.data.uploads || []).filter(function (g) {
      if (!q) return true;
      return [g.uploadId, g.by, (g.sourceFiles || []).join(' '), g.files.map(function (f) { return (LABEL[f.key] || f.key) + ' ' + (f.label || ''); }).join(' ')].join(' ').toLowerCase().indexOf(q) >= 0;
    });
    var rows = items.map(function (g) {
      var rowsTotal = g.files.reduce(function (s, f) { return s + (f.action === 'clear' ? 0 : (f.rows || 0)); }, 0);
      var src = (g.sourceFiles || []).length ? esc(g.sourceFiles.slice(0, 3).join(', ')) + (g.sourceFiles.length > 3 ? ' +' + (g.sourceFiles.length - 3) + ' more' : '') : '<span style="color:var(--ink3)">' + esc(g.note || '—') + '</span>';
      return '<tr style="cursor:pointer" onclick="History.showUpload(\'' + g.uploadId + '\')">' +
        '<td><a href="javascript:void(0)" style="font-family:var(--mono);font-weight:700;color:var(--vmm-pink);white-space:nowrap">' + esc(g.uploadId) + '</a></td>' +
        '<td style="white-space:nowrap">' + when(g.at) + '</td><td>' + esc(g.by || '—') + '</td>' +
        '<td style="font-size:11px">' + esc(SRC_LABEL[g.source] || g.source || '—') + '</td>' +
        '<td style="font-size:11px;max-width:260px">' + src + '</td>' +
        '<td>' + fileChips(g.files) + '</td>' +
        '<td style="font-family:var(--mono)">' + n(rowsTotal) + '</td>' +
        '<td style="white-space:nowrap"><span style="font-size:11px;color:var(--vmm-navy);font-weight:700">View files →</span></td></tr>';
    });
    body.innerHTML = searchBox('h-up-q', H.upQ, 'Search Upload ID, user or file name…') +
      '<div style="font-size:11.5px;color:var(--ink2);margin-bottom:10px">Every upload gets its own <strong>Upload ID</strong>. Click one to see all files it contained, download them, or make them current again. ' +
      items.length + ' upload(s), ' + rangeTxt() + (H.storage ? ' · total history on server: ' + mb(H.storage) : '') +
      '. <span style="color:#00873E">Green</span> = file in use now.</div>' +
      table(['Upload ID', 'Date & time', 'By', 'Type', 'Source file(s)', 'Files in this upload', 'Rows', ''], rows, 'No uploads in this date range.');
    wireSearch('h-up-q', function (v) { H.upQ = v; });
  }
  H.showUpload = function (uid) { H.openUpload = uid; H.render(); window.scrollTo(0, 0); };
  H.closeUpload = function () { H.openUpload = null; H.render(); };
  function renderUploadDetail(body, uid) {
    var g = (H.data.uploads || []).filter(function (x) { return x.uploadId === uid; })[0];
    if (!g) { H.openUpload = null; return renderUploads(body); }
    var storedUploads = g.files.filter(function (f) { return f.action !== 'clear' && f.stored; });
    var rows = g.files.map(function (f) {
      var act = f.action === 'clear' ? '<span style="color:#DC2626">cleared</span>' : f.action === 'restore' ? '<span style="color:#7C3AED">made current again' + (f.restoredFrom ? ' (from ' + esc(f.restoredFrom) + ')' : '') + '</span>' : 'uploaded';
      var btns = '';
      if (f.action !== 'clear' && f.stored) {
        btns += '<button class="btn-sec" style="font-size:11px;padding:5px 10px" onclick="History.downloadVersion(\'' + f.key + '\',\'' + f.version + '\',\'' + uid + '\')">⬇ Excel</button> ';
        if (isPlanner() && !f.current) btns += '<button class="btn-sec" style="font-size:11px;padding:5px 10px" onclick="History.restore(\'' + f.key + '\',\'' + f.version + '\')">Make current</button>';
      } else if (f.action !== 'clear') btns = '<span style="font-size:10px;color:var(--ink3)">no longer stored</span>';
      return '<tr><td><strong>' + esc(LABEL[f.key] || f.key) + '</strong>' + (f.current ? ' <span style="font-size:9px;background:#DCFCE7;color:#14532D;border-radius:8px;padding:1px 7px;font-weight:700">IN USE NOW</span>' : '') + '</td>' +
        '<td>' + act + '</td><td style="font-family:var(--mono)">' + (f.rows != null && f.action !== 'clear' ? n(f.rows) : '—') + '</td>' +
        '<td style="font-size:10.5px;color:var(--ink3)">' + (f.bytes ? mb(f.bytes) : '') + '</td>' +
        '<td style="font-size:10.5px;color:var(--ink2);max-width:320px">' + esc(f.label || '') + '</td>' +
        '<td style="white-space:nowrap">' + when(f.updatedAt) + '</td><td style="white-space:nowrap">' + btns + '</td></tr>';
    });
    body.innerHTML =
      '<button class="btn-sec" style="font-size:12px;padding:7px 14px;margin-bottom:12px" onclick="History.closeUpload()">← Back to all uploads</button>' +
      '<div class="card" style="padding:16px 18px;margin-bottom:12px;display:flex;flex-wrap:wrap;gap:22px;align-items:center">' +
        '<div><div style="font-size:10px;color:var(--ink3);text-transform:uppercase;font-weight:700">Upload ID</div><div style="font-family:var(--mono);font-size:18px;font-weight:800;color:var(--vmm-pink)">' + esc(g.uploadId) + '</div></div>' +
        '<div><div style="font-size:10px;color:var(--ink3);text-transform:uppercase;font-weight:700">Date & time</div><div style="font-weight:700">' + when(g.at) + '</div></div>' +
        '<div><div style="font-size:10px;color:var(--ink3);text-transform:uppercase;font-weight:700">By</div><div style="font-weight:700">' + esc(g.by || '—') + '</div></div>' +
        '<div><div style="font-size:10px;color:var(--ink3);text-transform:uppercase;font-weight:700">Type</div><div style="font-weight:700">' + esc(SRC_LABEL[g.source] || g.source || '—') + '</div></div>' +
        '<div style="flex:1;min-width:200px"><div style="font-size:10px;color:var(--ink3);text-transform:uppercase;font-weight:700">Source file(s)</div><div style="font-size:12px">' + (esc((g.sourceFiles || []).join(', ')) || esc(g.note || '—')) + '</div></div>' +
        '<div style="display:flex;gap:8px">' +
          (storedUploads.length ? '<button class="btn-pri" style="font-size:12px;padding:8px 14px" onclick="History.downloadUpload(\'' + uid + '\')">⬇ All files (one Excel)</button>' : '') +
          (isPlanner() && storedUploads.length && g.source !== 'restore' ? '<button class="btn-sec" style="font-size:12px;padding:8px 14px" onclick="History.restoreUpload(\'' + uid + '\')">Make this upload current</button>' : '') +
        '</div>' +
      '</div>' +
      table(['File', 'Action', 'Rows', 'Size', 'Details', 'Saved at', ''], rows, 'No files in this upload.');
  }
  H.downloadUpload = async function (uid) {
    var g = (H.data.uploads || []).filter(function (x) { return x.uploadId === uid; })[0];
    if (!g) return;
    toast('Preparing ' + uid + '…');
    try {
      var wb = XLSX.utils.book_new(), used = {};
      for (var i = 0; i < g.files.length; i++) {
        var f = g.files[i];
        if (f.action === 'clear' || !f.stored) continue;
        var d = await getJSON('data/' + f.key + '?version=' + encodeURIComponent(f.version));
        var name = ((LABEL[f.key] || f.key).replace(/[^A-Za-z0-9 ]/g, '').trim() || f.key).slice(0, 28);
        while (used[name]) name = name.slice(0, 26) + '_' + (used[name]++);
        used[name] = 1;
        XLSX.utils.book_append_sheet(wb, XLSX.utils.json_to_sheet(d.rows || []), name);
      }
      XLSX.writeFile(wb, uid + '.xlsx');
    } catch (e) { toast('⚠ ' + e.message); }
  };
  H.restoreUpload = async function (uid) {
    if (!confirm('Make every file from ' + uid + ' the CURRENT data for everyone?\n\nFiles not in this upload are left as they are. Newer versions stay in history.')) return;
    if (!(await Shared.ensurePlanner())) return;
    var r = await fetch('api/uploads/' + encodeURIComponent(uid) + '/restore', { method: 'POST', headers: Shared.headers() });
    var d = await r.json().catch(function () { return {}; });
    if (!r.ok) { toast('⚠ ' + (d.error || ('HTTP ' + r.status))); return; }
    toast('Done — recorded as ' + d.uploadId + '. Loading the data…');
    await Shared.applyUpdate();
    H.openUpload = null;
    H.load();
  };
  H.downloadVersion = async function (key, ver, uid) {
    toast('Preparing download…');
    try {
      var d = await getJSON('data/' + key + '?version=' + encodeURIComponent(ver));
      var wb = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(wb, XLSX.utils.json_to_sheet(d.rows || []), (LABEL[key] || key).replace(/[^A-Za-z0-9 ]/g, '').slice(0, 30) || 'Data');
      XLSX.writeFile(wb, (uid ? uid + '_' : '') + key.toUpperCase() + '.xlsx');
    } catch (e) { toast('⚠ ' + e.message); }
  };
  H.restore = async function (key, ver) {
    if (!confirm('Make this older version of ' + (LABEL[key] || key) + ' the CURRENT file for everyone?\n\nThe newer version stays in history.')) return;
    if (!(await Shared.ensurePlanner())) return;
    var r = await fetch('api/data/' + key + '/restore?version=' + encodeURIComponent(ver), { method: 'POST', headers: Shared.headers() });
    if (!r.ok) { toast('⚠ Could not restore (HTTP ' + r.status + ')'); return; }
    toast('Restored — loading it now');
    await Shared.applyUpdate();
    H.load();
  };

  // ── open / delete a saved run ─────────────────────────────────────
  H.openRun = async function (id) {
    Shared.overlay(true, 'Opening saved run…');
    try {
      await Shared.enterHistoryRun(id);
      plannerTab('assess');
    } catch (e) {
      console.error(e);
      toast('⚠ Could not open run: ' + e.message);
    }
    Shared.overlay(false);
  };
  H.deleteRun = async function (id) {
    if (!confirm('Delete this saved run from history?')) return;
    if (!(await Shared.ensurePlanner())) return;
    var r = await fetch('api/runs/' + id, { method: 'DELETE', headers: Shared.headers() });
    if (!r.ok) { toast('⚠ Could not delete (HTTP ' + r.status + ')'); return; }
    H.load();
  };
})();
