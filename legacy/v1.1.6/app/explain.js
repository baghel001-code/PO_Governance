/* ════════════════════════════════════════════════════════════════════
   v1.1.6 (16.35) - "How this number was worked out", single concept only.
   Ported from v1.2.5/v1.2.7 app/explain.js: the multi concept part and the
   workflow wording are left out (16.35 has no workflow and multi concept
   is unchanged). A side panel with numbered steps in simple English for:
     - Single concept Recommendation (page engine B: _runB / _coreColourSTRFast)
     - New PO request result card, single concept (runEngineB)
   Also the BOBs sibling list on a BOBs result card (display only).
   Plus two Excel downloads with live formulas: "this calculation" and
   "all lines" (same house look as every other download).
   DISPLAY ONLY: every number shown is the tool's own number; nothing here
   changes a calculation. Formulas in the Excel restate the tool's arithmetic
   so a reader can follow and re-check it (tests recalculate them and compare).
   ════════════════════════════════════════════════════════════════════ */
(function () {
  'use strict';
  var X = window.Explain = {};
  // v1.1.6: small stand-ins for the v1.2 VMM helpers
  var VMM = {
    esc: function (v) { return String(v == null ? '' : v).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); },
    toast: function (m) { if (typeof window.toast === 'function') window.toast(m); },
    saveBook: function (wb, name) { XLSX.writeFile(wb, name); },
    me: function () { return window.Shared && Shared.user ? { name: Shared.user.name } : null; }
  };

  // ── small helpers ──────────────────────────────────────────────────
  function esc(s) { return VMM.esc(s); }
  function n(v) { return v == null || v === '' || isNaN(v) ? '—' : Math.round(Number(v)).toLocaleString('en-IN'); }
  function d1(v) { return v == null || isNaN(v) ? '—' : (Math.round(v * 10) / 10).toLocaleString('en-IN', { minimumFractionDigits: 1, maximumFractionDigits: 1 }); }
  function d2(v) { return v == null || isNaN(v) ? '—' : Number(v).toFixed(2); }
  function pc(v, dp) { return v == null || isNaN(v) ? '—' : (v * 100).toFixed(dp == null ? 1 : dp) + '%'; }
  var MN = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  function pdate(iso) { var m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(iso || '')); return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null; }
  function day(iso) { var d = pdate(iso); return d ? d.getDate() + ' ' + MN[d.getMonth()] + ' ' + d.getFullYear() : '—'; }
  function dayS(iso) { var d = pdate(iso); return d ? d.getDate() + ' ' + MN[d.getMonth()] : '—'; }
  function mon(mk) { var p = String(mk || '').split('.'); return p.length === 2 ? MN[+p[0] - 1] + '-' + String(p[1]).slice(2) : String(mk || ''); }
  function monL(mk) { var p = String(mk || '').split('.'); return p.length === 2 ? MN[+p[0] - 1] + ' ' + p[1] : String(mk || ''); }
  function mkOf(y, m) { return (m < 10 ? '0' : '') + m + '.' + y; }
  function dim(y, m) { return new Date(y, m, 0).getDate(); }
  function addM(y, m, k) { var t = y * 12 + (m - 1) + k; return { y: Math.floor(t / 12), m: t % 12 + 1 }; }
  function iso(d) { return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0'); }
  function addD(isoS, k) { var d = pdate(isoS); d.setDate(d.getDate() + k); return iso(d); }
  function P() { try { return (typeof S !== 'undefined' && S && S.params) || {}; } catch (e) { return {}; } }
  X._h = { n: n, d1: d1, mon: mon, day: day };

  // ── the side panel ─────────────────────────────────────────────────
  var CSS = '#xp-ov{position:fixed;inset:0;background:rgba(15,27,61,.28);z-index:9990}' +
    '#xp{position:fixed;top:0;right:0;bottom:0;width:min(560px,100vw);background:#fff;z-index:9991;box-shadow:-12px 0 32px rgba(15,27,61,.22);display:flex;flex-direction:column;font-size:13px;color:#14213D}' +
    '#xp .xp-hd{padding:16px 20px 12px;border-bottom:1px solid #E6EAF2;display:flex;gap:12px;align-items:flex-start}' +
    '#xp .xp-k{font-size:11px;font-weight:700;letter-spacing:.06em;color:#5A6480}' +
    '#xp .xp-t{font-size:17px;font-weight:700;color:#0F2F6B;margin-top:3px;line-height:1.3}' +
    '#xp .xp-s{font-size:12px;color:#5A6480;margin-top:3px}' +
    '#xp .xp-x{margin-left:auto;border:0;background:#F1F4FA;border-radius:8px;width:32px;height:32px;font-size:16px;cursor:pointer;flex:0 0 auto}' +
    '#xp .xp-b{overflow:auto;padding:14px 20px 20px;flex:1}' +
    '#xp .xp-st{display:flex;gap:10px;margin:0 0 12px}' +
    '#xp .xp-no{flex:0 0 26px;height:26px;border-radius:13px;background:#1A4FAF;color:#fff;font-weight:700;font-size:12px;display:flex;align-items:center;justify-content:center}' +
    '#xp .xp-no.l{background:#0F2F6B;width:auto;padding:0 8px;flex:0 0 auto}' +
    '#xp .xp-tx{line-height:1.55;flex:1;min-width:0}' +
    '#xp .xp-tx b.v{color:#0F2F6B}' +
    '#xp .xp-h{font-weight:700;color:#0F2F6B;margin:16px 0 8px;font-size:13.5px;border-top:1px solid #EEF1F7;padding-top:12px}' +
    '#xp .xp-res{background:#E8F6EE;border:1px solid #BFE3CC;border-radius:10px;padding:10px 12px;margin:6px 0 12px}' +
    '#xp .xp-warn{background:#FFF7ED;border:1px solid #FED7AA;color:#7C2D12;border-radius:10px;padding:8px 12px;margin:6px 0 12px;font-size:12px}' +
    '#xp details{border:1px solid #E6EAF2;border-radius:10px;padding:8px 12px;margin:8px 0;background:#FAFBFE}' +
    '#xp summary{cursor:pointer;font-weight:700;color:#1A4FAF}' +
    '#xp table.xp-t2{border-collapse:collapse;width:100%;font-size:11.5px;margin:6px 0}' +
    '#xp table.xp-t2 th{background:#1F2A55;color:#fff;font-weight:700;padding:4px 6px;text-align:right;white-space:nowrap}' +
    '#xp table.xp-t2 th:first-child,#xp table.xp-t2 td:first-child{text-align:left}' +
    '#xp table.xp-t2 td{padding:4px 6px;border-bottom:1px solid #EEF1F7;text-align:right;white-space:nowrap}' +
    '#xp table.xp-t2 tr.me td{background:#EAF0FB;font-weight:700}' +
    '#xp .xp-ft{border-top:1px solid #E6EAF2;padding:12px 20px;display:flex;gap:8px;flex-wrap:wrap}' +
    '#xp .xp-btn{border:1px solid #C9CFDB;background:#fff;border-radius:8px;padding:7px 12px;font:inherit;font-size:12.5px;cursor:pointer}' +
    '#xp .xp-btn.p{background:#1A4FAF;color:#fff;border-color:#1A4FAF;font-weight:700}' +
    '#xp .xp-more{border:0;background:none;color:#1A4FAF;font-weight:700;cursor:pointer;padding:4px 0}' +
    '.xp-click{cursor:pointer;text-decoration:underline dotted;text-underline-offset:3px}' +
    '.xp-link{color:#1A4FAF;font-weight:700;cursor:pointer;text-decoration:underline;font-size:12px}' +
    '.xp-sub{display:block;font-size:10.5px;color:#6B7280;font-weight:400;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:220px}';
  function css() { if (document.getElementById('xp-css')) return; var s = document.createElement('style'); s.id = 'xp-css'; s.textContent = CSS; document.head.appendChild(s); }

  X.close = function () { ['xp', 'xp-ov'].forEach(function (id) { var e = document.getElementById(id); if (e) e.remove(); }); };
  // spec = {kicker, title, sub, body (html), buttons:[{label, primary, fn}]}
  X.panel = function (spec) {
    css(); X.close();
    var ov = document.createElement('div'); ov.id = 'xp-ov'; ov.onclick = X.close;
    var p = document.createElement('div'); p.id = 'xp'; p.setAttribute('role', 'dialog'); p.setAttribute('aria-label', spec.title || 'How this number was worked out');
    p.innerHTML = '<div class="xp-hd"><div><div class="xp-k">' + esc(spec.kicker || 'HOW THIS NUMBER WAS WORKED OUT') + '</div><div class="xp-t">' + esc(spec.title || '') + '</div>' +
      (spec.sub ? '<div class="xp-s">' + spec.sub + '</div>' : '') + '</div><button class="xp-x" type="button" aria-label="Close">✕</button></div>' +
      '<div class="xp-b">' + (spec.body || '') + '</div>' +
      ((spec.buttons || []).length ? '<div class="xp-ft">' + spec.buttons.map(function (b, i) { return '<button type="button" class="xp-btn' + (b.primary ? ' p' : '') + '" data-i="' + i + '">' + esc(b.label) + '</button>'; }).join('') + '</div>' : '');
    document.body.appendChild(ov); document.body.appendChild(p);
    p.querySelector('.xp-x').onclick = X.close;
    p.querySelectorAll('.xp-ft button').forEach(function (b) { b.onclick = function () { try { spec.buttons[+b.getAttribute('data-i')].fn(); } catch (e) { console.error(e); VMM.toast('Download failed: ' + e.message, 'err'); } }; });
    p.querySelectorAll('[data-xp-more]').forEach(function (b) {
      b.onclick = function () { p.querySelectorAll('.' + b.getAttribute('data-xp-more')).forEach(function (r) { r.style.display = ''; }); b.remove(); };
    });
    return p;
  };
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && document.getElementById('xp')) X.close(); });
  function step(no, html, last) { return '<div class="xp-st"><div class="xp-no' + (String(no).length > 2 ? ' l' : '') + '">' + esc(no) + '</div><div class="xp-tx">' + html + '</div></div>'; }
  function tbl(head, rows, meIdx) {
    return '<table class="xp-t2"><thead><tr>' + head.map(function (h) { return '<th>' + esc(h) + '</th>'; }).join('') + '</tr></thead><tbody>' +
      rows.map(function (r, i) { return '<tr' + (i === meIdx ? ' class="me"' : '') + (r._cls ? ' class="' + r._cls + '" style="display:none"' : '') + '>' + r.map(function (c) { return '<td>' + c + '</td>'; }).join('') + '</tr>'; }).join('') + '</tbody></table>';
  }
  X.step = step; X.tbl = tbl;

  // ══════════════════════════════════════════════════════════════════
  // Excel with formulas: X.book(filename, sheets)
  // sheet = {name, title, subtitle, cols:[{key,label,width,fmt}], rows:[{id, cells:{key: cell}, style}], note}
  // cell  = number | string | null | {v, f, s:'in'|'link'|'out'|'bold', fmt}
  // f may use {rowId.colKey} (same sheet) or {Sheet!rowId.colKey}; {rowId.colKey:rowId2.colKey} for a range.
  // Rows 1-3 title / who-when / blank, row 4 header, data from row 5 (the house layout).
  // ══════════════════════════════════════════════════════════════════
  var FMT = { int: '[>=10000000]##\\,##\\,##\\,##0;[>=100000]##\\,##\\,##0;##,##0', d1: '#,##0.0', d2: '0.00', d4: '0.0000', pct: '0.00%', pct1: '0.0%', date: 'dd mmm yyyy', txt: '@' };
  function colL(i) { var s = ''; i++; while (i > 0) { var m = (i - 1) % 26; s = String.fromCharCode(65 + m) + s; i = Math.floor((i - 1) / 26); } return s; }
  function xlDate(isoS) { var d = pdate(isoS); return d ? (Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()) - Date.UTC(1899, 11, 30)) / 864e5 : null; }
  X.xlDate = xlDate;
  X.book = function (filename, sheets) {
    if (!window.XLSX) { VMM.toast('The Excel download could not start: reload the page and try again.', 'err'); return null; }
    var wb = XLSX.utils.book_new(), addr = {};
    var thin = { style: 'thin', color: { rgb: 'D6DCE8' } }, border = { top: thin, bottom: thin, left: thin, right: thin };
    sheets.forEach(function (sh) {
      sh._name = String(sh.name).replace(/[\\\/?*\[\]:]/g, ' ').slice(0, 31);
      var ck = {}; sh.cols.forEach(function (c, i) { ck[c.key] = colL(i); });
      sh._ck = ck;
      sh.rows.forEach(function (r, i) { if (r.id != null) addr[sh._name + '!' + r.id] = 5 + i; });
    });
    function resolve(f, sh) {
      return f.replace(/\{([^{}]+)\}/g, function (all, ref) {
        var parts = ref.split(':').map(function (one) {
          var sn = sh._name, k = one;
          var bang = one.indexOf('!');
          if (bang >= 0) { sn = one.slice(0, bang); k = one.slice(bang + 1); }
          var dot = k.lastIndexOf('.'), rid = k.slice(0, dot), key = k.slice(dot + 1);
          var tsh = sheets.filter(function (s) { return s._name === sn; })[0];
          var row = addr[sn + '!' + rid], c = tsh && tsh._ck[key];
          if (!row || !c) throw new Error('Excel formula reference not found: ' + one);
          return { sn: sn, a: c + row };
        });
        var pre = parts[0].sn !== sh._name ? "'" + parts[0].sn + "'!" : '';
        return pre + parts.map(function (p) { return p.a; }).join(':');
      });
    }
    sheets.forEach(function (sh) {
      var ws = {}, ncol = sh.cols.length, R = 5 + sh.rows.length;
      var put = function (a, cell) { ws[a] = cell; };
      put('A1', { t: 's', v: sh.title || filename, s: { font: { bold: true, sz: 15, color: { rgb: '0F1B3D' } } } });
      put('A2', { t: 's', v: (sh.subtitle ? sh.subtitle + '. ' : '') + stamp(), s: { font: { sz: 9, color: { rgb: '5B6478' } } } });
      sh.cols.forEach(function (c, i) {
        put(colL(i) + '4', { t: 's', v: c.label, s: { font: { bold: true, color: { rgb: 'FFFFFF' }, sz: 10 }, fill: { patternType: 'solid', fgColor: { rgb: '1F2A55' } }, alignment: { horizontal: c.fmt && c.fmt !== 'txt' ? 'right' : 'left', vertical: 'center', wrapText: true }, border: border } });
      });
      sh.rows.forEach(function (r, ri) {
        var rn = 5 + ri;
        sh.cols.forEach(function (c, ci) {
          var cell = (r.cells || {})[c.key];
          if (cell == null || cell === '') { if (r.style) put(colL(ci) + rn, { t: 's', v: '', s: rowStyle(r, ri, null, c, border) }); return; }
          var o = (typeof cell === 'object') ? cell : { v: cell };
          var out = {};
          if (o.f) { out.f = resolve(o.f, sh).replace(/^=/, ''); out.t = typeof o.v === 'string' ? 's' : 'n'; out.v = o.v == null ? 0 : o.v; }
          else if (typeof o.v === 'number' && isFinite(o.v)) { out.t = 'n'; out.v = o.v; }
          else { out.t = 's'; out.v = o.v == null ? '' : String(o.v); }
          var fm = o.fmt || c.fmt;
          if (fm && out.t === 'n' && FMT[fm]) out.z = FMT[fm];
          out.s = rowStyle(r, ri, o, c, border, fm);
          put(colL(ci) + rn, out);
        });
      });
      if (sh.note) put('A' + (R + 1), { t: 's', v: sh.note, s: { font: { sz: 9, italic: true, color: { rgb: '5B6478' } } } });
      ws['!ref'] = 'A1:' + colL(ncol - 1) + (R + (sh.note ? 1 : 0));
      ws['!merges'] = [{ s: { r: 0, c: 0 }, e: { r: 0, c: Math.max(0, ncol - 1) } }, { s: { r: 1, c: 0 }, e: { r: 1, c: Math.max(0, ncol - 1) } }];
      ws['!cols'] = sh.cols.map(function (c) { return { wch: c.width || 12 }; });
      ws['!rows'] = [{ hpt: 24 }, { hpt: 16 }, { hpt: 8 }, { hpt: 30 }];
      if (sh.rows.length) ws['!autofilter'] = { ref: 'A4:' + colL(ncol - 1) + (4 + sh.rows.length) };
      XLSX.utils.book_append_sheet(wb, ws, sh._name);
    });
    wb.Workbook = { CalcPr: { fullCalcOnLoad: '1' } };
    return wb;
  };
  function rowStyle(r, ri, o, c, border, fm) {
    var st = { border: border, font: { sz: 10, color: { rgb: '0F1B3D' } }, alignment: { vertical: 'top', wrapText: (c.width || 0) > 34 } };
    if (ri % 2) st.fill = { patternType: 'solid', fgColor: { rgb: 'F6F8FC' } };
    if (r.style === 'head') { st.font = { sz: 10, bold: true, color: { rgb: '0F2F6B' } }; st.fill = { patternType: 'solid', fgColor: { rgb: 'EAF0FB' } }; }
    if (r.style === 'total') { st.font.bold = true; st.fill = { patternType: 'solid', fgColor: { rgb: 'E3F4E8' } }; }
    if (o) {
      if (o.s === 'in') st.font.color = { rgb: '0000FF' };            // data from a file / setting
      else if (o.s === 'link') st.font.color = { rgb: '008000' };     // link to another sheet
      else if (o.s === 'bold') st.font.bold = true;
      if (fm && FMT[fm]) st.numFmt = FMT[fm];
    }
    return st;
  }
  function stamp() {
    var d = new Date(), me = VMM.me();
    return 'Downloaded ' + d.toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' }) + ', ' +
      d.toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' }) + (me ? ' by ' + me.name : '') +
      '. Blue = data from the files or settings, black = formula, green = link to another sheet';
  }
  X.save = function (wb, name) { if (wb) return VMM.saveBook(wb, name.replace(/[\\/:*?"<>|]/g, '_'), 4); };
  function cIn(v, fmt) { return { v: v, s: 'in', fmt: fmt }; }
  function cF(f, v, fmt, s) { return { f: f, v: v, fmt: fmt, s: s }; }
  function cL(f, v, fmt) { return { f: f, v: v, fmt: fmt, s: 'link' }; }
  X.cIn = cIn; X.cF = cF; X.cL = cL;

  // ══════════════════════════════════════════════════════════════════
  // SINGLE CONCEPT
  // ctx = {colour, month (MM.YYYY), stock (F1 today), mix (share used, 0..1), mixNote, T1, buf (%), D, mixT (%),
  //        watch, rec, pre:[{mkey,y,m,fc,days,counted,po,sale,open,close}], chain:[{y,m,mkey,openStock,poIn|ePO,colPlan,closeStock,fc,demandDays,totalDays}],
  //        request: {qty} (New PO request) | null, title, group}
  // ══════════════════════════════════════════════════════════════════
  function singleNumbers(c) {
    var ch = c.chain || [], D = c.D, d1 = 1;
    var wstart = ch[d1] ? ch[d1].openStock : 0, budget = D, wdem = 0, wrows = [];
    for (var k = d1; k < ch.length && budget > 0; k++) {
      var days = dim(ch[k].y, ch[k].m), take = Math.min(days, budget);
      wdem += ch[k].colPlan * (take / days); budget -= take;
      wrows.push({ k: k, mkey: ch[k].mkey, take: take, days: days, sale: ch[k].colPlan * (take / days) });
    }
    var rate = D > 0 && wdem > 0 ? wdem / D : 0, eff = c.T1 * (1 + c.buf / 100);
    var ch0 = ch[0] || { openStock: 0, colPlan: 0 };
    var po0 = ch0.poIn != null ? ch0.poIn : (ch0.ePO || 0);
    var startNoProp = c.request ? ch0.openStock + po0 - ch0.colPlan : wstart;   // Tool 1: unclamped, without the asked qty
    var can = Math.max(0, Math.floor(eff * rate - startNoProp));
    return { wstart: wstart, wdem: wdem, rate: rate, eff: eff, need: eff * rate, startNoProp: startNoProp, can: can, wrows: wrows, po0: po0 };
  }
  X.singleNumbers = singleNumbers;
  // v1.2.6 item 2: colour mix switch ON -> each month carries its own share (x.mix); OFF -> every month = c.mix
  function mxOf(c, x) { return x && x.mix != null ? x.mix : c.mix; }
  function perMonthMix(c) {
    return (c.pre || []).concat(c.chain || []).some(function (x) { return x && x.mix != null && Math.abs(x.mix - c.mix) > 1e-12; });
  }
  X.perMonthMix = perMonthMix;
  function lastDay(mk) { var p = mk.split('.'); return dim(+p[1], +p[0]) + ' ' + MN[+p[0] - 1]; }

  X.single = function (c) {
    var t = new Date(), curKey = mkOf(t.getFullYear(), t.getMonth() + 1);
    var ch = c.chain || [], ch0 = ch[0] || {}, ch1 = ch[1] || {};
    var dm = c.month, nextKey = ch1.mkey || '';
    var body = '';
    if (c.watch) {
      body += step(1, 'This colour sold <b class="v">' + pc(c.mix, 1) + '</b> of the group in the last 45 days' + (c.mixNote ? ' (' + esc(c.mixNote) + ')' : '') + '.');
      body += step(2, 'From step 1: that is under the colour minimum of <b class="v">' + (c.mixT || 2) + '%</b>, so the tool does not work out a quantity — sizes are probably broken or cut.');
      body += '<div class="xp-res"><b>Watch List.</b> A New PO request for this colour goes to the Category Head for approval, with the buyer\'s reason (“Why introduce this colour?”).</div>';
      X.panel({ title: c.colour + ' · delivery ' + monL(dm) + ' · Watch List', sub: esc(c.group || ''), body: body,
        buttons: c.allLines ? [{ label: 'Download Excel (all lines)', fn: c.allLines }] : [] });
      return;
    }
    var N = singleNumbers(c), s = 1;
    var pre = c.pre || [], PM = perMonthMix(c);
    var mixW = function (x) { return pc(mxOf(c, x), 1); };
    body += step(s++, 'Stock today <b class="v">' + n(c.stock) + '</b> — all stores, F1 file.');
    if (dm !== curKey) {
      var preTxt = pre.length ? pre.map(function (p) { return monL(p.mkey) + ': sale ' + n(p.sale) + (p.counted !== p.days ? ' (' + p.counted + ' days left)' : '') + (p.po ? ', POs +' + n(p.po) : ''); }).join('; ') : '';
      body += step(s++, 'From step 1, take off the plan sale until ' + lastDay(pre.length ? pre[pre.length - 1].mkey : curKey) + ' (F4 plan × colour mix ' + (PM ? 'of each month: ' + pre.map(function (p) { return MN[p.m - 1] + ' ' + mixW(p); }).join(', ') : pc(c.mix, 1)) + ', days left only this month) and add POs landing by then' +
        (preTxt ? ' — ' + esc(preTxt) : '') + ': stock on 1 ' + monL(dm) + ' <b class="v">' + n(ch0.openStock) + '</b>.');
    }
    var poTxt = 'add ' + monL(dm) + ' POs (F2) and quantities approved today (' + n(N.po0) + ')';
    if (c.req && c.req.f2ByMon) {   // v1.1.7: released POs, open MRAs from SAP, approved in the tool today
      var f2p = (c.req.f2ByMon || {})[dm] || 0, mrp_ = ((c.req.mraIn && c.req.mraIn.byMon) || {})[dm] || 0, ldp = (c.req.ledgerByMon || {})[dm] || 0;
      poTxt = 'add what lands in ' + monL(dm) + ': released POs ' + n(f2p) + ' + open MRAs from SAP ' + n(mrp_) + ' + approved in the tool today ' + n(ldp) + ' (' + n(N.po0) + ')';
    }
    if (c.request) {
      body += step(s, 'From step ' + (s - 1) + ', take off ' + monL(dm) + '\'s sale ' + n(ch0.colPlan) + (ch0.demandDays && ch0.totalDays && ch0.demandDays !== ch0.totalDays ? ' (' + ch0.demandDays + ' days left)' : '') + ' (plan × ' + mixW(ch0) + ') and ' + poTxt +
        ': stock on 1 ' + monL(nextKey) + ' without this request <b class="v">' + n(N.startNoProp) + '</b>' + (N.startNoProp < 0 ? ' (below 0: already short by this much)' : '') + ' — the window starts here.'); s++;
    } else {
      body += step(s, 'From step ' + (s - 1) + ', take off ' + monL(dm) + '\'s sale ' + n(ch0.colPlan) + (ch0.demandDays && ch0.totalDays && ch0.demandDays !== ch0.totalDays ? ' (' + ch0.demandDays + ' days left)' : '') + ' (plan × ' + mixW(ch0) + ') and ' + poTxt +
        ': stock on 1 ' + monL(nextKey) + ' <b class="v">' + n(N.wstart) + '</b> — the window starts here (never below 0).'); s++;
    }
    var wEnd = N.wrows.length ? N.wrows[N.wrows.length - 1] : null;
    body += step(s++, 'Window 1 ' + monL(nextKey) + ' – ' + (wEnd ? wEnd.take + ' ' + monL(wEnd.mkey) : '') + ' (' + c.D + ' days): ' +
      N.wrows.map(function (w) { return (w.take === w.days ? MN[+w.mkey.split('.')[0] - 1] : w.take + '/' + w.days + ' of ' + MN[+w.mkey.split('.')[0] - 1]); }).join(' + ') +
      (PM ? ' plan × the colour mix of each month (' + N.wrows.map(function (w) { return MN[+w.mkey.split('.')[0] - 1] + ' ' + mixW(ch[w.k]); }).join(', ') + ')' : ' plan × ' + pc(c.mix, 1)) + ' = <b class="v">' + n(N.wdem) + ' pcs</b> → <b class="v">' + d1(N.rate) + ' a day</b>.');
    var sN = s;
    body += step(s++, 'Limit ' + c.T1 + ' days + ' + c.buf + '% buffer = <b class="v">' + d2(N.eff) + ' days</b>. Stock needed on 1 ' + monL(nextKey) + ' = ' + d2(N.eff) + ' × ' + d1(N.rate) + ' = <b class="v">' + n(N.need) + '</b>.');
    var resTxt;
    if (c.request) {
      var q = +c.request.qty || 0;
      var cover = N.rate > 0 ? (Math.max(0, ch1.openStock || 0)) / (N.wdem) * c.D : null;
      body += step(s++, 'Max Permissible = step ' + sN + ' − step ' + (sN - 2) + ' = ' + n(N.need) + ' − ' + n(N.startNoProp) + ' = <b class="v">' + n(N.can) + '</b> (rounded down, never below 0).');
      resTxt = 'Asking <b>' + n(q) + '</b> gives about <b>' + (cover != null ? Math.round(cover) : '—') + ' days</b> of cover in the window (with the asked quantity added on ' + monL(dm) + '). ' +
        (q <= N.can ? 'That is within the limit, so it is allowed.' : 'That is above the ' + d2(N.eff) + '-day limit, so the tool allows up to <b>' + n(N.can) + '</b> (Confirm Final Quantity).');
    } else {
      body += step(s++, 'Can raise = step ' + sN + ' − step ' + (sN - 2) + ' = ' + n(N.need) + ' − ' + n(N.wstart) + ' = <b class="v">' + n(N.can) + '</b> (rounded down, never below 0).');
      resTxt = N.can > 0 ? '<b>Max Permissible ' + n(N.can) + '.</b> A New PO request up to this is allowed.' : '<b>Hold.</b> Stock and POs already cover ' + d2(N.eff) + ' days; nothing more can be raised for ' + monL(dm) + '.';
    }
    body += '<div class="xp-res">' + resTxt + '</div>';
    if (c.rec != null && Math.abs(c.rec - N.can) > 0) body += '<div class="xp-warn">The table shows ' + n(c.rec) + '; the steps give ' + n(N.can) + '. The data changed since the table was worked out — reopen the screen.</div>';
    body += '<details><summary>Where the ' + pc(c.mix, 1) + ' comes from</summary><div style="margin-top:6px;line-height:1.55">' + esc(c.colour) + ' sold ' + pc(c.s45share != null ? c.s45share : c.mix, 1) + ' of the group\'s last-45-day sale (F3). ' +
      esc(c.mixNote || 'The last 45 days are used for every month.') +
      (PM ? ' Colour mix switch ON: each month uses the rule of that month (Admin settings), so the share can differ month to month — see the Mix column of the step-by-step table.' : '') + '</div></details>';
    body += '<details><summary>Step-by-step table</summary>' + tbl(['Month', 'Open', 'POs in', 'Plan sale', 'Close'].concat(PM ? ['Mix'] : []),
      pre.map(function (p) { return [monL(p.mkey), n(p.open), n(p.po), n(p.sale), n(p.close)].concat(PM ? [mixW(p)] : []); }).concat(ch.slice(0, (N.wrows.length ? N.wrows[N.wrows.length - 1].k : 1) + 1).map(function (x) {
        return [monL(x.mkey), n(x.openStock), n(x.poIn != null ? x.poIn : x.ePO), n(x.colPlan), n(x.closeStock)].concat(PM ? [mixW(x)] : []);
      }))) + '</details>';
    X.panel({ title: c.colour + ' · delivery ' + monL(dm) + ' · ' + (c.request ? 'Max Permissible ' + n(N.can) : (N.can > 0 ? 'can raise ' + n(N.can) : 'Hold')),
      sub: esc(c.group || ''), body: body,
      buttons: [{ label: 'Download this calculation (Excel)', primary: true, fn: function () { X.singleExcel(c); } }].concat(c.allLines ? [{ label: 'Download Excel (all lines)', fn: c.allLines }] : [])
        .concat(c.req && window.MRA ? [{ label: 'Download working (JSON)', fn: function () { MRA.downloadWorking(c.req); } }] : []) });
  };

  // "this calculation" workbook, single concept
  X.singleBook = function (c) {
    var N = singleNumbers(c), pre = c.pre || [], ch = c.chain || [], PM = perMonthMix(c);
    var mixRef = function (id) { return PM ? '{' + id + '.mix}' : '{Inputs!mix.v}'; };
    var lastK = N.wrows.length ? N.wrows[N.wrows.length - 1].k : 1;
    var inputs = { name: 'Inputs', title: c.colour + ' · ' + (c.group || '') + ' · inputs', cols: [{ key: 'a', label: 'Input', width: 40 }, { key: 'v', label: 'Value', width: 14, fmt: 'd2' }, { key: 'w', label: 'Where it comes from', width: 60 }],
      rows: [{ id: 'stk', cells: { a: 'Stock today (all stores)', v: cIn(c.stock, 'int'), w: 'F1 file' } },
        { id: 'mix', cells: { a: PM ? 'Colour mix used (delivery month)' : 'Colour mix used', v: cIn(c.mix, 'pct'), w: (c.mixNote || 'Share of the last-45-day sale (F3)') + (PM ? ' — switch ON: each month has its own share, see the Mix column of Month by month' : '') } },
        { id: 't1', cells: { a: 'Limit T1 (days)', v: cIn(c.T1), w: 'Settings' } },
        { id: 'buf', cells: { a: 'Buffer %', v: cIn(c.buf), w: 'Settings' } },
        { id: 'D', cells: { a: 'Window D (days)', v: cIn(c.D), w: 'Settings: days from the 1st of the month after delivery' } }].concat(c.request ? [{ id: 'q', cells: { a: 'Asked quantity', v: cIn(+c.request.qty || 0, 'int'), w: 'New PO request' } }] : []) };
    var mm = { name: 'Month by month', title: 'Stock rolled month by month (plan sale = F4 × mix × days counted ÷ days in month)', cols: [
      { key: 'm', label: 'Month', width: 10 }, { key: 'fc', label: 'F4 plan (group)', width: 14, fmt: 'int' }, { key: 'days', label: 'Days in month', width: 9 }, { key: 'cnt', label: 'Days counted', width: 9 },
      { key: 'sale', label: 'Plan sale', width: 12, fmt: 'd1' }].concat(PM ? [{ key: 'mix', label: 'Mix', width: 8, fmt: 'pct' }] : []).concat([ { key: 'po', label: 'POs in (F2 + open MRAs + approved today)', width: 14, fmt: 'int' }, { key: 'ask', label: 'Asked qty', width: 10, fmt: 'int' },
      { key: 'open', label: 'Open stock', width: 12, fmt: 'd1' }, { key: 'close', label: 'Close stock', width: 12, fmt: 'd1' }, { key: 'take', label: 'Days in window', width: 9 }, { key: 'win', label: 'Window sale', width: 12, fmt: 'd1' }]), rows: [] };
    var prevId = null;
    pre.forEach(function (p, i) {
      var id = 'p' + i;
      mm.rows.push({ id: id, cells: { m: monL(p.mkey), fc: cIn(p.fc, 'int'), days: cIn(p.days), cnt: cIn(p.counted), sale: cF('{' + id + '.fc}*' + mixRef(id) + '*{' + id + '.cnt}/{' + id + '.days}', p.sale),
        po: cIn(p.po, 'int'), open: prevId ? cF('{' + prevId + '.close}', p.open) : cL('{Inputs!stk.v}', p.open), close: cF('MAX(0,{' + id + '.open}+{' + id + '.po}-{' + id + '.sale})', p.close) } });
      if (PM) mm.rows[mm.rows.length - 1].cells.mix = cIn(mxOf(c, p), 'pct');
      prevId = id;
    });
    for (var k = 0; k <= Math.min(lastK, ch.length - 1); k++) {
      var x = ch[k], id = 'c' + k, w = N.wrows.filter(function (r) { return r.k === k; })[0];
      var po = x.poIn != null ? x.poIn : (x.ePO || 0), ask = k === 0 && c.request ? (+c.request.qty || 0) : 0;
      var cells = { m: monL(x.mkey), fc: cIn(x.fc || 0, 'int'),
        days: cIn(x.totalDays || dim(x.y, x.m)), cnt: cIn(x.demandDays || dim(x.y, x.m)), sale: cF('{' + id + '.fc}*' + mixRef(id) + '*{' + id + '.cnt}/{' + id + '.days}', x.colPlan),
        po: cIn(po, 'int'), open: prevId ? cF('{' + prevId + '.close}', x.openStock) : cL('{Inputs!stk.v}', x.openStock),
        close: cF('MAX(0,{' + id + '.open}+{' + id + '.po}' + (ask ? '+{' + id + '.ask}' : '') + '-{' + id + '.sale})', x.closeStock) };
      if (ask) cells.ask = cL('{Inputs!q.v}', ask);
      if (PM) cells.mix = cIn(mxOf(c, x), 'pct');
      if (w) { cells.take = cIn(w.take); cells.win = cF('{' + id + '.sale}*{' + id + '.take}/{' + id + '.days}', w.sale); }
      mm.rows.push({ id: id, cells: cells });
      prevId = id;
    }
    var w0 = 'c1', wl = 'c' + Math.min(lastK, ch.length - 1);
    var st = { name: 'Steps', title: c.colour + ' · delivery ' + monL(c.month) + ' · ' + (c.request ? 'New PO request' : 'Recommendation'), subtitle: c.group || '',
      cols: [{ key: 'no', label: 'Step', width: 6 }, { key: 'what', label: 'What', width: 46 }, { key: 'how', label: 'How (in words)', width: 60 }, { key: 'v', label: 'Value', width: 14, fmt: 'int' }], rows: [] };
    var sr = function (id, no, what, how, cell) { st.rows.push({ id: id, cells: { no: no, what: what, how: how, v: cell } }); };
    sr('s1', 1, 'Stock today (F1)', 'From the Inputs sheet', cL('{Inputs!stk.v}', c.stock));
    if (c.request) {
      sr('s3', 2, 'Stock on the window start without this request', monL(c.month) + ': open + POs − plan sale (can be below 0)', cF('{Month by month!c0.open}+{Month by month!c0.po}-{Month by month!c0.sale}', N.startNoProp, 'd1'));
    } else {
      sr('s3', 2, 'Stock on the window start (1 ' + monL((ch[1] || {}).mkey) + ')', 'Close stock of ' + monL(c.month) + ' (Month by month sheet)', cL('{Month by month!c0.close}', N.wstart, 'd1'));
    }
    sr('s4', 3, 'Window sale (' + c.D + ' days)', 'Sum of the window sale column', cF('SUM({Month by month!' + w0 + '.win:Month by month!' + wl + '.win})', N.wdem, 'd1'));
    sr('s5', 4, 'Daily rate in the window', 'Window sale ÷ D', cF('{s4.v}/{Inputs!D.v}', N.rate, 'd2'));
    sr('s6', 5, 'Limit in days', 'T1 × (1 + buffer ÷ 100)', cF('{Inputs!t1.v}*(1+{Inputs!buf.v}/100)', N.eff, 'd2'));
    sr('s7', 6, 'Stock needed on the window start', 'Limit days × daily rate', cF('{s6.v}*{s5.v}', N.need, 'd1'));
    sr('s8', 7, c.request ? 'Max Permissible' : 'Can raise (Max Permissible)', 'Stock needed − stock on the window start, rounded down, never below 0', cF('MAX(0,ROUNDDOWN({s7.v}-{s3.v},0))', N.can, 'int', 'bold'));
    st.rows[st.rows.length - 1].style = 'total';
    return [st, inputs, mm];
  };
  X.singleExcel = function (c) {
    var sh = X.singleBook(c);
    X.save(X.book('calc', sh), 'Calculation_' + c.colour + '_' + c.month.replace('.', '-') + '.xlsx');
  };

  // "all lines": one row per colour x month with the formula columns
  X.singleAllBook = function (lines, meta) {
    var cols = [{ key: 'c', label: 'Colour', width: 9 }, { key: 'm', label: 'Delivery month', width: 10 }, { key: 'stk', label: 'Stock today', width: 11, fmt: 'int' },
      { key: 'mix', label: 'Colour mix used', width: 10, fmt: 'pct' }, { key: 'ws', label: 'Stock on window start', width: 13, fmt: 'd1' }, { key: 'wd', label: 'Window sale (D days)', width: 13, fmt: 'd1' },
      { key: 'D', label: 'D (days)', width: 7 }, { key: 'rate', label: 'Daily rate', width: 10, fmt: 'd2' }, { key: 'lim', label: 'Limit days (T1 + buffer)', width: 11, fmt: 'd2' },
      { key: 'need', label: 'Stock needed', width: 12, fmt: 'd1' }, { key: 'mp', label: 'Max Permissible', width: 12, fmt: 'int' }, { key: 'res', label: 'Result', width: 12 }];
    var rows = lines.map(function (L, i) {
      var id = 'r' + i;
      if (L.watch) return { id: id, cells: { c: L.colour, m: monL(L.month), stk: cIn(L.stock, 'int'), mix: cIn(L.mix, 'pct'), res: 'Watch List (mix under ' + (meta.mixT || 2) + '%)' } };
      var N = singleNumbers(L);
      return { id: id, cells: { c: L.colour, m: monL(L.month), stk: cIn(L.stock, 'int'), mix: cIn(L.mix, 'pct'), ws: cIn(N.wstart, 'd1'), wd: cIn(N.wdem, 'd1'), D: cIn(L.D),
        rate: cF('IF({' + id + '.D}>0,{' + id + '.wd}/{' + id + '.D},0)', N.rate), lim: cIn(N.eff, 'd2'), need: cF('{' + id + '.lim}*{' + id + '.rate}', N.need),
        mp: cF('MAX(0,ROUNDDOWN({' + id + '.need}-{' + id + '.ws},0))', N.can), res: cF('IF({' + id + '.mp}>0,"Raise","Hold")', N.can > 0 ? 'Raise' : 'Hold') } };
    });
    return [{ name: 'All lines', title: 'Colour Recommendation — Single Concept · ' + (meta.group || ''), subtitle: 'Window ' + meta.D + ' days from the 1st of the month after delivery · limit ' + meta.T1 + ' days + ' + meta.buf + '%', cols: cols, rows: rows,
      note: 'Stock on window start and window sale come from the month-by-month roll (download "this calculation" from a colour for that roll with formulas).' }];
  };

  // Hooks into the screens
  // ══════════════════════════════════════════════════════════════════
  function groupText(info) {
    info = info || {};
    return [info.brand, info.mcDesc || info.mcDes || info.mcCd, info.mrp ? '₹' + info.mrp : '', info.flag].filter(Boolean).join(' · ');
  }
  function singleParams() { var p = P(); return { T1: +p.T1 || 75, buf: +p.strBuffer || 0, D: +p.D || 75, mixT: +p.mix || 2 }; }
  function recoCtx(r, md, info) {
    var sp = singleParams();
    var cs = md.cs || {};
    var note = md.cm ? (md.cm.applied ? md.cm.note + (md.cm.colourNote ? ' · ' + md.cm.colourNote : '') : (md.cm.note || '')) : '';
    return { colour: String(r.col).toUpperCase(), month: md.mon, stock: r.stk, mix: md.mixUsed != null ? md.mixUsed : (md.cm ? md.cm.used : r.mixRatio),
      s45share: r.mixRatio, mixNote: note, T1: sp.T1, buf: sp.buf, D: cs.winDays || sp.D, mixT: sp.mixT, watch: md.action === 'Watch List',
      nodata: md.action === 'No data', rec: md.rec, pre: cs.pre || [], chain: cs.chain || [], group: groupText(info) };
  }
  X.recoCtx = recoCtx;
  function recoAll(res, info) {
    var lines = [];
    (res.rows || []).forEach(function (r) { (r.mdata || []).forEach(function (md) { if (md.action !== 'No data') lines.push(recoCtx(r, md, info)); }); });
    var sp = singleParams();
    var meta = { group: groupText(info), D: sp.D, T1: sp.T1, buf: sp.buf, mixT: sp.mixT };
    X.save(X.book('all', X.singleAllBook(lines, meta)), 'Recommendation_single_' + String((info || {}).mcCd || '') + '_all_lines.xlsx');
  }
  X.recoAll = recoAll;
  function hookPaint() {
    if (typeof window._paintRec !== 'function' || window._paintRec._xp) return;
    var orig = window._paintRec;
    var w = function (res, months) {
      var out = orig.apply(this, arguments);
      try {
        var box = document.getElementById('rec-out');
        if (box && res && res.rows && box.querySelector('[data-xs]')) {
          var info = (window.RS && RS.info) || {};
          var all = function () { recoAll(res, info); };
          box.querySelectorAll('[data-xs]').forEach(function (el) {
            el.onclick = function () {
              var p = el.getAttribute('data-xs').split('|'), r = res.rows[+p[0]], md = r && r.mdata[+p[1]];
              if (!r || !md) return;
              if (md.action === 'No data') { X.panel({ title: String(r.col).toUpperCase() + ' · ' + monL(md.mon), body: '<div class="xp-warn">No F4 plan for this group from ' + monL(md.mon) + ', so no quantity can be worked out.</div>' }); return; }
              var c = recoCtx(r, md, info); c.allLines = all; X.single(c);
            };
          });
          var b = document.getElementById('xs-all'); if (b) b.onclick = all;
        }
      } catch (e) { console.error(e); }
      return out;
    };
    Object.keys(orig).forEach(function (k) { w[k] = orig[k]; });
    w._xp = 1; window._paintRec = w;
  }

  // New PO request result card: "How was this number worked out?"
  function requestCtx(r) {
    var sp = singleParams();
    var cm = r.colourMix || null;
    return { colour: String(r.colour || '').toUpperCase(), month: r.month, stock: r.opStock, mix: r.effMix != null ? (r.effMix || 0) : (r.mixPct || 0),
      s45share: cm ? cm.s45 : r.mixPct, mixNote: cm ? (cm.applied ? cm.note : cm.note || '') : '', T1: sp.T1, buf: sp.buf, D: sp.D, mixT: sp.mixT,
      watch: r.mixFailed || (r.decision === 'Watch List' && !r.chain), rec: r.maxPermQty, pre: r.preRoll || [], chain: r.chain || [],
      request: { qty: r.qty }, group: groupText(r.info), req: r };
  }
  X.requestCtx = requestCtx;
  X.openForRequest = function (r) {
    if (!r) return;
    if (r.engine === 'B') {
      if (r.mixFailed) { var wc = requestCtx(r); wc.watch = true; X.single(wc); return; }
      if (r.decision === 'Watch List') { X.panel({ title: (r.gm || '') + ' ' + (r.colour || '') + ' · Watch List', body: '<div class="xp-warn">' + esc(r.decisionReason || '') + '</div><div class="xp-res"><b>Watch List.</b> A New PO request for this colour goes to the Category Head for approval, with the buyer\'s reason (“Why introduce this colour?”).</div>' }); return; }   // v1.1.6 D3: no sale -> no quantity steps
      if (!r.chain || !r.chain.length) { X.panel({ title: (r.gm || '') + ' ' + (r.colour || ''), body: '<div class="xp-warn">' + esc(r.decisionReason || 'No step-by-step for this result.') + '</div>' }); return; }
      X.single(requestCtx(r)); return;
    }
    X.panel({ title: (r.gm || '') + ' ' + (r.colour || ''), body: '<div class="xp-warn">' + esc(r.decisionReason || '') + '</div>' });
  };
  function hookResult() {
    if (typeof window.renderResult !== 'function' || window.renderResult._xp) return;
    var orig = window.renderResult;
    var w = function (r) {
      var out = orig.apply(this, arguments);
      try { addLink(r); addBobs(r); } catch (e) { console.error(e); }
      return out;
    };
    Object.keys(orig).forEach(function (k) { w[k] = orig[k]; });
    w._xp = 1; window.renderResult = w;
  }
  function addLink(r) {
    if (!r || r.engine !== 'B' || !((r.chain && r.chain.length) || r.mixFailed)) return;
    var cards = document.querySelectorAll('[id="res-' + r.id + '"]');
    var card = cards[0];
    var body = card && card.querySelector('.res-body');
    if (!body || body.querySelector('.xp-reslink')) return;
    var d = document.createElement('div');
    d.className = 'xp-reslink';
    d.style.cssText = 'margin:0 0 10px;font-size:12px';
    d.innerHTML = '<span class="xp-link" role="button" tabindex="0">How was this number worked out? →</span> <span style="color:#6B7280">(steps, Excel with formulas)</span>';
    var go = function () { X.openForRequest(r); };
    d.firstChild.onclick = go;
    d.firstChild.onkeydown = function (e) { if (e.key === 'Enter') go(); };
    body.insertBefore(d, body.firstChild);
  }
  // v1.2.5 change 4: BOBs sibling colours behind an expand button
  X.bobsHtml = function (list, open) {
    if (!list || !list.length) return '';
    var cnt = {}; list.forEach(function (s) { var k = s.q || 'not ranked'; cnt[k] = (cnt[k] || 0) + 1; });
    var sumTxt = ['Q1', 'Q2', 'Q3', 'Q4', 'not ranked'].filter(function (k) { return cnt[k]; }).map(function (k) { return cnt[k] + ' ' + k; }).join(', ');
    var has3 = list.some(function (s) { return s.first3st != null; });
    var ncol = Object.keys(list.reduce(function (a, s) { a[s.gm + '|' + s.colour] = 1; return a; }, {})).length;
    var qc = { Q1: '#0B6B2E', Q2: '#0B6B2E', Q3: '#8A5A00', Q4: '#B42318' };
    return '<details class="xp-bobs"' + (open ? ' open' : '') + ' style="margin:0 0 10px;border:1px solid #E6EAF2;border-radius:10px;padding:8px 12px;background:#FAFBFE">' +
      '<summary style="cursor:pointer;font-weight:700;color:#1A4FAF;font-size:12.5px">Show the sibling colours of this style: ' + ncol + ' colour' + (ncol === 1 ? '' : 's') +
        (list.length !== ncol ? ', ' + list.length + ' BSR rows (one per season)' : '') + ' (' + esc(sumTxt) + ')</summary>' +
      '<table style="border-collapse:collapse;width:100%;font-size:12px;margin-top:6px"><thead><tr style="background:#1F2A55;color:#fff">' +
      '<th style="padding:4px 8px;text-align:left">Generic Article · colour</th><th style="padding:4px 8px">BSR season</th><th style="padding:4px 8px;text-align:right">Weeks on floor</th><th style="padding:4px 8px">Rank</th>' +
      '<th style="padding:4px 8px;text-align:right" title="AVG PERDAY SELL THRU% in the BSR (the value the ranking uses)">Sell-through / day</th>' +
      (has3 ? '<th style="padding:4px 8px;text-align:right">First 3 weeks sell-through / day</th>' : '') +
      '<th style="padding:4px 8px;text-align:right">First 3 weeks sale (pcs)</th></tr></thead><tbody>' +
      list.map(function (s) {
        return '<tr style="border-bottom:1px solid #EEF1F7"><td style="padding:4px 8px">' + esc(s.gm) + ' · <b>' + esc(s.colour) + '</b></td><td style="padding:4px 8px;text-align:center">' + esc(s.season || '—') + '</td><td style="padding:4px 8px;text-align:right">' + esc(s.wof) + '</td>' +
          '<td style="padding:4px 8px;text-align:center;font-weight:700;color:' + (qc[s.q] || '#6B7280') + '">' + esc(s.q || 'not ranked') + '</td>' +
          '<td style="padding:4px 8px;text-align:right">' + (s.pdstr != null ? (s.pdstr * 100).toFixed(2) + '%' : '—') + '</td>' +
          (has3 ? '<td style="padding:4px 8px;text-align:right">' + (s.first3st != null ? (s.first3st <= 1 ? (s.first3st * 100).toFixed(2) + '%' : Number(s.first3st).toFixed(2) + '%') : '—') + '</td>' : '') +
          '<td style="padding:4px 8px;text-align:right">' + n(s.first3) + '</td></tr>';
      }).join('') + '</tbody></table><div style="font-size:11px;color:#6B7280;margin-top:4px">Ranked in the same MC / MRP peer group as today. “not ranked” = under the weeks-on-floor gate. The BOBs rule looks at every BSR row of the sibling colours, so a colour sold in two seasons shows twice.</div></details>';
  };
  function addBobs(r) {
    if (!r || r.optionType !== 'BOBs' || !r.bobsSiblings || !r.bobsSiblings.length) return;
    var cards = document.querySelectorAll('[id="res-' + r.id + '"]');
    var card = cards[0], body = card && card.querySelector('.res-body');
    if (!body || body.querySelector('.xp-bobs')) return;
    var d = document.createElement('div'); d.innerHTML = X.bobsHtml(r.bobsSiblings);
    var link = body.querySelector('.xp-reslink');
    if (link && link.nextSibling) body.insertBefore(d.firstChild, link.nextSibling); else body.insertBefore(d.firstChild, body.firstChild);
  }
  function install() { hookPaint(); hookResult(); }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', install); else install();
  window.addEventListener('load', install);
  setTimeout(install, 0);
  X.install = install;
})();
