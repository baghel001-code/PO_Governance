#!/usr/bin/env node
// Tool 1 ("✏️ New Request") parity harness.
//
// Runs the ORIGINAL v1.1.4 code — the Category form functions of legacy/python-app/app/index.html
// (formGMInput, formPopulateColours, populateMonths, formSubmit, runEngineB, runEngineA, renderResult,
// renderCatHistory, _confirmFinalQty / qcConfirm / qcDecline … extracted verbatim by name, plus the
// recommendation script for _strCaution / _cautionBoxHtml) together with the REAL shared.js (refreshRequests,
// saveRequest with its 409 re-check, approvalBadge, visibleRequests). The browser is shimmed: a fake DOM,
// a fixed Date and a fake fetch that behaves like server.py's /api/requests (seq, 409 conflict, reqId,
// Watch List approval). Results go to tests/POGovernance.Tests/Tool1/Fixtures/expected.json; the xUnit
// tests run the C# service on the same data and compare field by field.
//
//   node tests/parity/tool1/make-fixture.js
//   LANG=en_IN.UTF-8 node tests/parity/tool1/run-parity.js
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const ROOT = path.resolve(__dirname, '../../..');
const HTML = fs.readFileSync(path.join(ROOT, 'legacy/python-app/app/index.html'), 'utf8');
const SHARED = fs.readFileSync(path.join(ROOT, 'legacy/python-app/app/shared.js'), 'utf8');
const FIX = path.join(ROOT, 'tests/POGovernance.Tests/Tool1/Fixtures');
const dataset = JSON.parse(fs.readFileSync(path.join(FIX, 'dataset.json'), 'utf8'));

if ((1234567).toLocaleString() !== '12,34,567') {
  console.error('Run with LANG=en_IN.UTF-8 (toLocaleString() must use Indian grouping).');
  process.exit(1);
}

// ── extraction ─────────────────────────────────────────────────────────────
const lines = HTML.split('\n');
function extractFunction(name) {
  const re = new RegExp('^(async )?function ' + name.replace(/\$/g, '\\$') + '\\s*\\(');
  const idx = [];
  lines.forEach((l, i) => { if (re.test(l)) idx.push(i); });
  if (!idx.length) throw new Error('function not found: ' + name);
  const start = idx[idx.length - 1];               // the later definition wins in JS
  const first = lines[start];
  const open = (first.match(/{/g) || []).length, close = (first.match(/}/g) || []).length;
  if (open > 0 && open === close) return first;
  let end = start + 1;
  while (end < lines.length && !/^}\s*$/.test(lines[end])) end++;
  return lines.slice(start, end + 1).join('\n');
}
function extractLine(prefix) {
  const l = lines.find(x => x.startsWith(prefix));
  if (!l) throw new Error('line not found: ' + prefix);
  return l;
}
function extractBlock(prefix, endRe) {
  const s = lines.findIndex(x => x.startsWith(prefix));
  let e = s + 1;
  while (!endRe.test(lines[e])) e++;
  return lines.slice(s, e + 1).join('\n');
}
function extractRecScript() {
  const marker = HTML.indexOf('// RECOMMENDATION ENGINE — built on top of detection engine');
  const s = HTML.lastIndexOf('<script>', marker) + '<script>'.length;
  const e = HTML.indexOf('</script>', marker);
  return HTML.slice(s, e);
}

const pageSrc = [
  extractLine('const REQUIRED_KEYS ='),
  extractLine('const num = v =>'),
  extractLine('const fmt = v =>'),
  extractLine('const _colNorm = s =>'),
  extractLine('const isMMYYYY = k =>'),
  extractLine('const toYM = k =>'),
  extractLine('const isDayCol = k =>'),
  'const _idx={};',
  extractLine('let _qtyConfirmResolve='),
  extractBlock('var FMAP = {', /^};\s*$/),
  ...['col', '_buildUMIndex', '_buildBSRIndex', '_buildResolveIndex', 'dateToMMYYYY', 'addMonths', 'daysInMonth', 'monLabel', 'mmyyyy',
    '_prevMonthCarry', '_prevMonthCarryNote', '_ePOWithCarry', '_todayStr', '_confirmFinalQty', 'qcClamp', 'qcConfirm', 'qcDecline',
    '_approvedQtyFor', 'ymKey', 'resolveGM', 'buildKey2', 'buildKey3', 'routeRequest', 'runEngineB', 'getWOF', 'getPDSTR', 'quartile',
    'pctRank', 'getBSRRows', 'absBlock', 'runEngineA', 'dpill', 'renderResult', '$g', 'formGMInput', 'formPopulateColours',
    'formColChange', 'formGetColour', 'formClear', 'formSubmit', 'renderCatHistory', 'populateMonths'].map(extractFunction),
  'function renderCatInsights(){}',
  'let _runACache = null;', 'let _runBCache = null;'
].join('\n\n');
const recSrc = extractRecScript();

// ── fake DOM ───────────────────────────────────────────────────────────────
function makeEl(id) {
  return {
    id, textContent: '', innerHTML: '', value: '', style: {}, children: [], disabled: false, max: '', min: '',
    classList: { _s: new Set(), add(c) { this._s.add(c); }, remove(c) { this._s.delete(c); }, toggle(c) { this._s.has(c) ? this._s.delete(c) : this._s.add(c); }, contains(c) { return this._s.has(c); } },
    addEventListener() { }, appendChild(c) { this.children.push(c); return c; }, insertBefore(c) { this.children.unshift(c); return c; },
    querySelector() { return null; }, focus() { }
  };
}

// ── text extraction from the painted HTML ───────────────────────────────────
const driverSrc = String.raw`
(async function(){
  const sc = __scenario;
  const strip = s => String(s).replace(/<br\s*\/?>/g, '\n').replace(/<[^>]+>/g, '').replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"').replace(/&#39;/g,"'");
  const norm = s => strip(s).replace(/\s+/g, ' ').trim();
  const all = (re, s) => { const r = []; let m; while ((m = re.exec(s))) r.push(m); return r; };
  const seg = (html, a, b) => { const i = html.indexOf(a); if (i < 0) return ''; const j = b ? html.indexOf(b, i) : -1; return j < 0 ? html.slice(i) : html.slice(i, j); };
  const rows = h => all(/<tr style="background:([^;"]*)[^>]*>([\s\S]*?)<\/tr>/g, h).map(m => ({ bg: m[1], cells: all(/<td[^>]*>([\s\S]*?)<\/td>/g, m[2]).map(c => norm(c[1])) }));
  function card(html) {
    const c = {};
    c.decision = norm((/white-space:nowrap">(.*?)<\/div>\s*<\/div>\s*<div class="res-body">/s.exec(html) || [])[1] || '');
    c.num = norm((/<div class="rh-num">(.*?)<\/div>/.exec(html) || [])[1] || '');
    const title = /<div class="rh-title"[^>]*>([\s\S]*?)<\/div>\s*<div class="rh-sub">/.exec(html);
    c.titleSpans = title ? all(/<span[^>]*>(.*?)<\/span>/g, title[1]).map(m => norm(m[1])) : [];
    c.sub = norm((/<div class="rh-sub">(.*?)<\/div>/.exec(html) || [])[1] || '');
    c.chips = all(/<span style="font-size:9px;font-weight:700;color:var\(--ink3\);text-transform:uppercase;letter-spacing:\.06em">(.*?)<\/span>\s*<span style="font-size:12px;font-weight:600;color:([^"]*)">(.*?)<\/span>/g, html).map(m => [m[1], norm(m[3])]);
    const mix = /⚠ Colour Mix %[^<]*/.exec(html); if (mix) c.mixBanner = norm(mix[0]);
    c.kpis = all(/<div style="font-size:18px;font-weight:700;color:([^;]+);font-family:var\(--mono\);line-height:1">(.*?)<\/div>\s*<div style="font-size:9px;color:var\(--ink3\);margin-top:5px;text-transform:uppercase;letter-spacing:\.05em">(.*?)<\/div>/g, html).map(m => [m[3], norm(m[2]), m[1]]);
    if (html.indexOf('<!-- STEP 1') >= 0) {
      const s1 = seg(html, '<!-- STEP 1', '<!-- STEP 2');
      c.step1 = all(/<div style="font-size:(?:16px|10px);font-weight:(?:700|600);color:#1A4FAF;font-family:var\(--mono\)">(.*?)<\/div>/g, s1).map(m => norm(m[1]));
      const s2 = seg(html, '<!-- STEP 2', '<!-- STEP 3');
      c.step2 = all(/<div style="font-size:14px;font-weight:700;color:([^;]+);font-family:var\(--mono\)">(.*?)<\/div>/g, s2).map(m => norm(m[2]));
      c.step2Labels = all(/letter-spacing:\.05em">(Colour Avg Sales\/\w+|MC Total Avg Sales\/\w+)<\/div>/g, s2).map(m => m[1]);
      c.step2Basis = norm((/Mix % = <strong>Colour total sales ÷ MC total sales<\/strong> ([\s\S]*?)\.\s*This tells us/.exec(s2) || [])[1] || '');
      const s3 = seg(html, '<!-- STEP 3', '<!-- STEP 4');
      c.step3Proposed = norm((/Proposed PO of <strong>(.*?) units<\/strong>/.exec(s3) || [])[1] || '');
      c.step3 = rows(s3).map(r => [r.bg].concat(r.cells));
      c.step3Empty = s3.indexOf('No chain data available') >= 0;
      const s4 = seg(html, '<!-- STEP 4', '<!-- STEP 5');
      c.step4 = rows(s4).map(r => [r.bg].concat(r.cells));
      c.step4Total = all(/<tr style="background:#7C3AED11;font-weight:700">([\s\S]*?)<\/tr>/g, s4).map(m => all(/<td[^>]*>([\s\S]*?)<\/td>/g, m[1]).map(x => norm(x[1])));
      const f = /STR Formula<\/div>\s*<div[^>]*>([\s\S]*?)<\/div>/.exec(s4);
      c.step4Formula = f ? strip(f[1]).split('\n').map(x => x.replace(/\s+/g, ' ').trim()).filter(Boolean) : [];
      const s5 = seg(html, '<!-- STEP 5', 'end detail panel');
      const f5 = /line-height:2;margin-bottom:10px[^>]*>([\s\S]*?)<\/div>/.exec(s5);
      c.step5Formula = f5 ? strip(f5[1]).split('\n').map(x => x.replace(/\s+/g, ' ').trim()).filter(Boolean) : [];
      c.step5Verdict = norm((/<div style="font-size:12px;color:var\(--ink\);line-height:1\.8">([\s\S]*?)<\/div>/.exec(s5) || [])[1] || '');
      c.step5Header = (/<div style="background:([^;]+);padding:8px 14px;display:flex;align-items:center;gap:8px">\s*<span[^>]*>STEP 5/.exec(s5) || [])[1] || '';
    }
    const a1 = /Step 1 — Article & Peer Pool<\/div>([\s\S]*?)Step 2 —|Step 1 — Article & Peer Pool<\/div>([\s\S]*?)— Reason/.exec(html);
    if (a1) c.aStep1 = all(/<div style="font-size:14px;font-weight:700;color:([^;]+);font-family:var\(--mono\);line-height:1\.2;word-break:break-all">(.*?)<\/div>\s*<div[^>]*>([\s\S]*?)<\/div>/g, html).map(m => [m[3], norm(m[2]), m[1]]);
    c.aStep2 = all(/<div style="font-size:15px;font-weight:700;color:([^;]+);font-family:var\(--mono\)">(.*?)<\/div>\s*<div[^>]*>(.*?)<\/div>/g, html).map(m => [m[3], norm(m[2]), m[1]]);
    const q3 = /(Step 3 — Quartile Ranking \([^)]*\))/.exec(html); if (q3) {
      c.aStep3 = [norm(q3[1]), norm((/(Rank within peer group of [^<]*)<\/span>/.exec(html) || [])[1] || ''), norm((/<span style="font-size:12px;font-weight:700;color:[^"]*">(\d+th percentile — [^<]*)<\/span>/.exec(html) || [])[1] || ''),
        (/position:absolute;left:([\d.]+)%;top:50%/.exec(html) || [])[1] || ''];
    }
    const b4 = /Step 4 — Border Zone Check \(PDSTR Gap\)<\/div>\s*<div[^>]*>([\s\S]*?)<\/div>\s*<div[^>]*>([\s\S]*?)<\/div>/.exec(html);
    if (b4) c.aStep4 = strip(b4[1]).split('\n').map(x => x.replace(/\s+/g, ' ').trim()).filter(Boolean).concat([norm(b4[2])]);
    const ab = /(⚡ Absolute Block Override:[^<]*(?:&lt;[^<]*)*)/.exec(html); if (ab) c.absNote = norm(ab[1]);
    c.reasonTitle = norm((/letter-spacing:\.06em;opacity:\.8">(.*?)<\/strong>/.exec(html) || [])[1] || '');
    c.reason = norm((/— Reason<\/strong><br>\s*<span style="color:var\(--ink2\)">([\s\S]*?)<\/span>\s*<\/div>/.exec(html) || [])[1] || '');
    c.footer = norm((/<span style="font-size:10px;color:var\(--ink3\)">(.*?)<\/span>\s*<button class="rec-btn"/.exec(html) || [])[1] || '');
    c.caution = all(/<span>(.*? above Tool 2 threshold of .*?)<\/span>/g, html).map(m => norm(m[1]));
    return c;
  }
  function historyRows() {
    const tb = document.getElementById('cat-hist-tbody').innerHTML;
    return all(/<tr>([\s\S]*?)<\/tr>/g, tb).map(m => all(/<td[^>]*>([\s\S]*?)<\/td>/g, m[1]).map(c => norm(c[1])));
  }
  const idOf = r => r ? String(r['Generic Material']) + '|' + String(r['Color']) : null;
  const out = [];
  populateMonths();
  out.push({ name: 'months', kind: 'months', args: {}, result: (document.getElementById('c-mon').innerHTML.match(/<option value="([^"]*)">([^<]*)<\/option>/g) || []).map(o => { const m = /value="([^"]*)">([^<]*)</.exec(o); return [m[1], m[2]]; }) });
  for (const c of sc.cases) {
    let result;
    try {
      if (c.kind === 'options') {
        const g = document.getElementById('c-gm'); g.value = c.gm;
        const strip0 = document.getElementById('c-strip'); strip0.classList._s.clear();
        formGMInput('c');
        result = {
          show: document.getElementById('c-strip').classList.contains('show'),
          strip: ['c-brand', 'c-div', 'c-mcd', 'c-mcdesc', 'c-mrp', 'c-flag'].map(i => document.getElementById(i).textContent),
          options: all(/<option value="([^"]*)">([^<]*)<\/option>/g, document.getElementById('c-col').innerHTML).map(m => [m[1], m[2]]),
          manual: document.getElementById('c-col-txt').style.display || ''
        };
      } else {
        __toasts.length = 0; __pending.length = 0; __modal.asked = null; __modal.c = c;
        __server.posts = [];
        document.getElementById('c-gm').value = c.gm;
        document.getElementById('c-col').value = c.colour || '';
        document.getElementById('c-col-txt').value = c.colourText || '';
        document.getElementById('c-mon').value = c.month;
        document.getElementById('c-qty').value = c.qty;
        document.getElementById('c-results').children.length = 0;
        const seqBefore = __server.seq;
        formSubmit('c');
        await Promise.all(__pending);
        const area = document.getElementById('c-results');
        const last = S.requests[S.requests.length - 1];
        const html = area.children[0] ? area.children[0].innerHTML : null;
        result = {
          toasts: __toasts.slice(), asked: __modal.asked, parallel: __modal.parallel || null,
          seqBefore,
          posts: __server.posts,
          saved: __server.lastSaved ? JSON.parse(JSON.stringify(__server.lastSaved)) : null,
          card: html ? card(html) : null,
          history: html ? historyRows() : null,
          pools: last && last.engine === 'A' && html ? { peerPool: (last.peerPool || []).map(idOf), cleanPool: (last.cleanPool || []).map(idOf), siblings: (last.siblings || []).map(idOf),
            qualSibs: (last.qualSibs || []).map(idOf), sibRanked: (last.sibRanked || []).map(s => ({ id: idOf(s.row), q: s.q, pdstr: s.pdstr })), repSibling: idOf(last.repSibling) } : null
        };
        __server.lastSaved = null;
        // the case's request stays in the server's ledger for the next cases only when it was saved
      }
    } catch (e) { result = { __error: String(e && e.stack || e) }; }
    out.push({ name: c.name, kind: c.kind, args: c, result });
  }
  return { out, ledger: __server.reqs, initialSeq: __server.initialSeq };
})()
`;

const ME = { name: 'Test Buyer', email: 'buyer@vmm.test', roles: ['planner'], roleLabels: ['Planner'] };
const SAVED_AT = '2026-10-14T10:00:00+05:30';
function approversFor(r) {
  const n = v => String(v || '').trim().replace(/\.0$/, '').toLowerCase();
  const i = r.info || {};
  return (n(i.brand) === 'non-pl' && n(i.mcCd) === '2203' && n(i.mrp) === '599') ? { emails: ['ch@vmm.test'], names: ['Cat Head'] } : { emails: [], names: [] };
}

function runScenario(sc) {
  const [Y, M, D] = sc.today.split('-').map(Number);
  const RealDate = Date;
  const FIXED = new RealDate(Y, M - 1, D, 12, 0, 0).getTime();
  class FakeDate extends RealDate {
    constructor(...a) { if (a.length === 0) super(FIXED); else super(...a); }
    static now() { return FIXED; }
  }
  const files = {};
  for (const k of ['f1', 'f2', 'f3', 'f4', 'um', 'bsr', 'artmaster', 'colmaster', 'attrrel', 'catheadmap'])
    files[k] = sc.drop && sc.drop.includes(k) ? null : JSON.parse(JSON.stringify(dataset.files[k]));
  const params = Object.assign({ T1: 75, T2: 100, D: 75, mix: 2, N: 2, wof: 4, peer: 5, mixWindowDays: 45, borderTolerance: 10, strBuffer: 5, prevMonthPendingCutoffDay: 5, seasonCurrent: ['AW26'], seasonFallback: ['SS26'] }, sc.params || {});
  const S = { files, params, requests: [], reqCount: 0, role: 'category' };
  // server-side ledger: today's requests of the fixture, numbered like server.py
  const server = { reqs: [], seq: 0, posts: [], lastSaved: null };
  for (const r of dataset.requests.filter(x => x.reqDate === sc.today)) { const c = JSON.parse(JSON.stringify(r)); c.seq = ++server.seq; c.id = server.reqs.length + 1; c.requestedBy = 'Earlier User'; c.requestedByEmail = 'earlier@vmm.test'; server.reqs.push(c); }
  server.initialSeq = server.seq;
  function serverSave(r, me) {
    server.seq += 1;
    r.seq = server.seq; r.id = server.reqs.length + 1;
    r.reqId = 'REQ-' + sc.today.replace(/-/g, '') + '-' + String(server.seq).padStart(4, '0');
    r.requestedBy = me.name; r.requestedByEmail = me.email; r.savedAt = SAVED_AT;
    delete r.approval;
    if (r.decision === 'Watch List') { const a = approversFor(r); r.approval = { status: 'pending', approvers: a.emails, approverNames: a.names }; }
    server.reqs.push(r);
    return r;
  }
  const els = {};
  const document = {
    getElementById(id) { return els[id] || (els[id] = makeEl(id)); },
    createElement(t) { return makeEl(t); },
    querySelectorAll() { return []; }, querySelector() { return null; },
    addEventListener() { }
  };
  const toasts = [], pending = [];
  const modal = { asked: null, c: null };
  async function fetchStub(url, opts) {
    opts = opts || {};
    const reply = (status, data) => ({ ok: status >= 200 && status < 300, status, headers: { get: () => 'application/json' }, json: async () => JSON.parse(JSON.stringify(data)) });
    const p = String(url).replace(/^api\//, '');
    if ((opts.method || 'GET') === 'GET' && p.startsWith('requests')) return reply(200, { requests: server.reqs, seq: server.seq });
    if (opts.method === 'POST' && p === 'requests') {
      const body = JSON.parse(opts.body);
      const r = body.request;
      server.posts.push({ seenSeq: body.seenSeq, force: !!body.force });
      r.reqDate = sc.today;
      const seen = parseInt(body.seenSeq || 0);
      if (!body.force && r.colourKey) {
        const newer = server.reqs.filter(x => (x.seq || 0) > seen && x.colourKey === r.colourKey);
        if (newer.length) return reply(409, { conflict: true, byOthers: [...new Set(newer.map(x => x.requestedBy || '?'))].sort().join(', '), seq: server.seq });
      }
      const saved = serverSave(r, ME);
      server.lastSaved = saved;
      return reply(200, { ok: true, id: saved.id, reqId: saved.reqId, seq: saved.seq, approval: saved.approval || null });
    }
    return reply(404, { error: 'not found' });
  }
  const ctx = vm.createContext({
    S, document, Date: FakeDate, Math, JSON, Object, Array, String, Number, Map, Set, Promise, Proxy, parseFloat, parseInt, isFinite, isNaN, encodeURIComponent,
    console, toast: m => toasts.push(m), alert: m => toasts.push('alert: ' + m),
    setTimeout: (f) => { const r = f(); if (r && r.then) pending.push(r); return 0; }, clearTimeout() { }, setInterval() { return 0; },
    fetch: fetchStub, localStorage: { getItem() { return null; }, setItem() { } }, sessionStorage: { getItem() { return null; }, setItem() { } },
    location: { protocol: 'http:', host: 'test', pathname: '/', replace() { } }, Blob: function () { }, Response: function () { },
    __scenario: sc, __toasts: toasts, __pending: pending, __modal: modal, __server: server
  });
  ctx.window = ctx;
  vm.runInContext(pageSrc + '\n' + recSrc, ctx, { filename: 'index-extract.js' });
  vm.runInContext(SHARED, ctx, { filename: 'shared.js' });
  // the modal: run the real _confirmFinalQty, then answer it like the planner would
  vm.runInContext(`
    const __origConfirm = _confirmFinalQty;
    _confirmFinalQty = function(p, m){
      const pr = __origConfirm(p, m);
      __modal.asked = { proposed: p, max: m };
      const c = __modal.c;
      if (c.parallel) {             // another user saves a request for this colour while the pop-up is open
        const x = JSON.parse(JSON.stringify(c.parallel));
        x.reqDate = Shared.serverToday;
        __modal.parallel = __serverSaveOther(x);
      } else __modal.parallel = null;
      const inp = document.getElementById('qc-input');
      if (c.confirm === 'decline') qcDecline();
      else { if (c.confirm !== 'max') inp.value = c.confirm; qcConfirm(); }
      return pr;
    };`, ctx);
  ctx.__serverSaveOther = x => JSON.parse(JSON.stringify(serverSave(x, { name: 'Other Buyer', email: 'other@vmm.test' })));
  ctx.Shared.online = true;
  ctx.Shared.user = ME;
  ctx.Shared.serverToday = sc.today;
  return vm.runInContext(driverSrc, ctx, { filename: 'driver.js' });
}

// ── scenarios ──────────────────────────────────────────────────────────────
const info = (brand, mc, mrp, flag) => ({ brand, mcCd: mc, mrp, flag });
function casesFor(sc) {
  const cs = [];
  const add = (name, kind, args) => cs.push(Object.assign({ name, kind }, args));
  // form options
  for (const gm of ['', '11', '110', '1101000101', ' 2203000201 ', 'A1101X04', '6600000001', '7700000001', 'ZZ0000001', '1101NOMC', '8800000001'])
    add('opt-' + gm.trim(), 'options', { gm });
  if (sc.only === 'options') return cs;
  const m = sc.months;   // [current, +1, +2, +3]
  const sub = (name, gm, colour, month, qty, extra) => add(name, 'submit', Object.assign({ gm, colour, month, qty: String(qty) }, extra || {}));
  // input checks
  sub('empty-qty', '1101000101', 'BLK', m[1], '');
  sub('neg-qty', '1101000101', 'BLK', m[1], -5);
  sub('no-colour', '1101000101', '', m[1], 100);
  // Single Concept
  // parallel approval while the pop-up is open → 409 → re-check → capped
  const par = (gm, col, mon, q) => ({ gm, colour: col, month: mon, qty: q, engine: 'B', optionType: '—', decision: 'Auto Allow', finalApprovedQty: q,
    info: { brand: 'NON-PL', div: 'MENS', mcCd: '1101', mcDesc: 'M-TSHIRT RN', mrp: '399', flag: 'NON-BOGO', season: 'AW26', artDesc: '', vendorName: '' },
    colourKey: 'non-pl|1101|399|non-bogo|' + col.toLowerCase(), requestedBy: 'Other Buyer' });
  sub('B1-conflict-cap', '1101000101', 'WHT', m[1], 900, { confirm: 'max', parallel: par('1101000103', 'WHT', m[1], 200000) });
  sub('B1-conflict-small', '1101000101', 'OWH', m[2], 50, { confirm: 'max', parallel: par('1101000103', 'OWH', m[2], 10) });
  sub('B1-conflict-other-colour', '1101000101', 'RED', m[3], 900, { confirm: 'max', parallel: par('1101000103', 'BLK', m[1], 500) });
  sub('B1-RED-over', 'A1101X04', 'RED', m[1], 800, { confirm: '99999999' });
  sub('B1-07-junk', 'A1101X04', '07', m[1], 800, { confirm: 'abc' });
  sub('B1-BLK-neg', 'A1101X04', 'BLK', m[1], 800, { confirm: '-7' });
  sub('B1-07-decline', 'A1101X04', '07', m[2], 100, { confirm: 'decline' });
  sub('B1-101-frac', 'A1101X04', '101', m[2], 800.9, { confirm: '133.8' });
  sub('B1-manual-colour', '1101000101', '__new__', m[1], 700, { colourText: ' owh ', confirm: 'max' });
  sub('B1-past-month', '1101000101', 'BLK', sc.past, 900, { confirm: 'max' });
  sub('B1-bad-month', '1101000101', 'BLK', '2026-11', 900, { confirm: 'max' });
  sub('B1-far', '1101000101', 'RED', m[3], 4500, { confirm: 'max' });
  // the same colours again (headroom now used by the cases above)
  for (const col of ['BLK', 'WHT', 'NVY', 'RED', 'OWH', '101', '07', 'GRY'])
    for (const [mi, qty, conf] of [[1, 300, 'max'], [2, 5000, '250'], [0, 120000, 'max'], [3, 40, 'decline']])
      sub('B1-' + col + '-' + mi + '-' + qty + '-' + conf, '1101000102', col, m[mi], qty, { confirm: conf });
  // other single concept paths
  sub('B-nomc', '1101NOMC', 'BLK', m[1], 100);
  sub('B-noforecast', '8800000001', 'BLK', m[1], 100);
  sub('B6-legacy-RED', '6600000001', 'RED', m[1], 1000, { confirm: 'max' });
  sub('B6-legacy-YEL', '6600000001', 'YEL', m[2], 1000, { confirm: '10' });
  sub('B6-cyavg-GRN', '6600000002', 'GRN', m[1], 10, { confirm: 'max' });
  sub('B7-serial-WHT', '6601000001', 'WHT', m[1], 500, { confirm: '300' });
  sub('B7-serial-PNK', '6601000001', 'PNK', m[2], 2500, { confirm: 'max' });
  // Multi Concept: every BSR article of B2 / B4 as True Repeat, plus BOBs
  const bsr = dataset.files.bsr;
  bsr.filter(r => ['2203', '4407', '3305', '5509'].includes(String(r['MC CD'])) && !String(r['Generic Material']).startsWith('22030091'))
     .forEach((r, i) => sub('A-TR-' + r['Generic Material'] + '-' + r['Color'], r['Generic Material'], r['Color'], m[1 + (i % 3)], 600));
  for (const [gm, col] of [['2203000201', 'ZZZ'], ['2203000202', 'PNK'], ['2203000205', 'NEWC'], ['4407000401', 'RED'], ['3305000301', 'BLU'], ['5509000501', 'TEAL'], ['2203009105', 'YEL'], ['2203009100', 'GRN']])
    sub('A-BOBs-' + gm + '-' + col, gm, col, m[2], 400);
  // New article
  sub('NEW-unknown', 'ZZ0000001', 'BLK', m[1], 250);
  sub('NEW-artmaster', '7700000001', 'BLK', m[1], 250);
  return cs;
}

const monthsFrom = (today, n) => { const [y, mo] = today.split('-').map(Number); return Array.from({ length: n }, (_, i) => { let mm = mo + i, yy = y; while (mm > 12) { mm -= 12; yy++; } return String(mm).padStart(2, '0') + '.' + yy; }); };
const scenarios = [
  { name: 'S1-early-month', today: '2026-10-03' },
  { name: 'S2-params', today: '2026-10-14', params: { T1: 60, strBuffer: 0, mix: 0.5, D: 45, peer: 3, wof: 3, borderTolerance: 25, seasonCurrent: ['SS26'], seasonFallback: ['AW26'], prevMonthPendingCutoffDay: 15, N: 3 } },
  { name: 'S3-year-end', today: '2026-12-02', params: { strBuffer: 12.5, seasonCurrent: ['AW26', 'SS26'], seasonFallback: [], wof: 6, mixWindowDays: 30, borderTolerance: 0 } },
  { name: 'S4-no-artmaster', today: '2026-10-14', params: { mix: 8, peer: 20, seasonCurrent: ['XX'], seasonFallback: ['SS26'] }, drop: ['artmaster', 'attrrel'] },
  { name: 'S5-no-colmaster', today: '2026-11-01', params: { prevMonthPendingCutoffDay: 0, T1: 120, D: 30 }, drop: ['colmaster'] },
  { name: 'S6-missing-bsr', today: '2026-10-14', drop: ['bsr'], small: true }
];

const expected = { generatedBy: 'tests/parity/tool1/run-parity.js', source: 'legacy/python-app/app/index.html + shared.js (v1.1.4)', me: ME, savedAt: SAVED_AT, scenarios: [] };
(async () => {
  let errors = 0;
  for (const sc of scenarios) {
    sc.months = monthsFrom(sc.today, 4);
    const [y, mo] = sc.today.split('-').map(Number);
    sc.past = String(mo === 1 ? 12 : mo - 1).padStart(2, '0') + '.' + (mo === 1 ? y - 1 : y);
    sc.cases = casesFor(sc);
    if (sc.small) sc.cases = sc.cases.filter(c => c.kind === 'options' || /^(B1-BLK-1|NEW-unknown|A-TR-2203000201)/.test(c.name));
    const { out: results, ledger, initialSeq } = await runScenario(sc);
    results.forEach(r => { if (r.result && r.result.__error) { errors++; console.error(sc.name, r.name, r.result.__error); } });
    expected.scenarios.push({ name: sc.name, today: sc.today, params: sc.params || {}, drop: sc.drop || [], ledger: JSON.parse(JSON.stringify(ledger)), initialSeq, results });
  }
  fs.writeFileSync(path.join(FIX, 'expected.json'), JSON.stringify(expected));
  const stats = {};
  for (const s of expected.scenarios) for (const r of s.results) if (r.kind === 'submit' && r.result.saved) stats[r.result.saved.decision] = (stats[r.result.saved.decision] || 0) + 1;
  console.log('wrote expected.json:', expected.scenarios.map(s => s.name + ' (' + s.results.length + ')').join(', '), errors ? (' — ' + errors + ' JS errors') : '', JSON.stringify(stats));
})();
