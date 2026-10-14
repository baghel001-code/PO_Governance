#!/usr/bin/env node
// Tool 2 ("📋 PO Assessment") parity harness.
//
// Runs the ORIGINAL v1.1.4 code — the whole Tool 2 block of legacy/python-app/app/index.html
// (from "let batchResults" to the role/tab navigation, verbatim), the recommendation-engine
// <script>, the first-script helpers it calls (extracted by name) and packResults / runSummary
// from shared.js — in a vm sandbox with stubs for S, document, setTimeout (chunks run
// synchronously), Blob / URL (CSV) and XLSX (captures the workbook instead of writing it).
// For every scenario it runs runAssessment(), renders the dashboard, the detail / rollup views
// with several filter combinations and the CSV / Excel exports, and writes what the browser
// would show to tests/POGovernance.Tests/Tool2/Fixtures/expected.json.
//
//   node tests/parity/tool2/make-fixture.js            (dataset)
//   LANG=en_IN.UTF-8 node tests/parity/tool2/run-parity.js
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const ROOT = path.resolve(__dirname, '../../..');
const HTML = fs.readFileSync(path.join(ROOT, 'legacy/python-app/app/index.html'), 'utf8');
const SHARED = fs.readFileSync(path.join(ROOT, 'legacy/python-app/app/shared.js'), 'utf8');
const FIX = path.join(ROOT, 'tests/POGovernance.Tests/Tool2/Fixtures');
const dataset = JSON.parse(fs.readFileSync(path.join(FIX, 'dataset.json'), 'utf8'));

if ((1234567).toLocaleString() !== '12,34,567') {
  console.error('Run with LANG=en_IN.UTF-8 (toLocaleString() must use Indian grouping).');
  process.exit(1);
}

// ── extraction ─────────────────────────────────────────────────────────────
const lines = HTML.split('\n');
function extractFunction(name) {
  const re = new RegExp('^function ' + name.replace(/\$/g, '\\$') + '\\s*\\(');
  const start = lines.findIndex(l => re.test(l));
  if (start < 0) throw new Error('function not found: ' + name);
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
function extractTool2Block() {
  const s = lines.findIndex(l => l.startsWith('let batchResults = [];'));
  const e = lines.findIndex((l, i) => i > s && l.startsWith('function openRoleDialog()'));
  return lines.slice(s, e).join('\n');
}
function extractRecScript() {
  const marker = HTML.indexOf('// RECOMMENDATION ENGINE — built on top of detection engine');
  const s = HTML.lastIndexOf('<script>', marker) + '<script>'.length;
  const e = HTML.indexOf('</script>', marker);
  return HTML.slice(s, e);
}
function extractShared() {
  const s = SHARED.indexOf('  var POOL_KEYS');
  const e = SHARED.indexOf('  Shared.saveRun = async function');
  return SHARED.slice(s, e);
}

const helperSrc = [
  extractLine('const num = v =>'),
  extractLine('const fmt = v =>'),
  'const fmtCr = v => {\n  if(v>=1e7) return (v/1e7).toFixed(2)+\'Cr\';\n  if(v>=1e5) return (v/1e5).toFixed(2)+\'L\';\n  return fmt(v);\n};',
  extractLine('const fmtD = v =>'),
  extractLine('const _colNorm = s =>'),
  extractLine('const isMMYYYY = k =>'),
  extractLine('const toYM = k =>'),
  extractLine('const isDayCol = k =>'),
  'const _idx={};',
  ...['col', '_buildUMIndex', '_buildBSRIndex', '_buildResolveIndex', 'dateToMMYYYY', 'addMonths', 'daysInMonth', 'monLabel', 'mmyyyy',
    '_prevMonthCarry', '_prevMonthCarryNote', '_ePOWithCarry', '_todayStr', '_approvedQtyFor', 'ymKey',
    'getWOF', 'getPDSTR', 'quartile', 'pctRank', 'getBSRRows', 'absBlock', 'runEngineA', 'resolveGM', '_aiDriverBucketT2', 'toggleAssReason'].map(extractFunction),
  'function renderPlannerDashboard(){}',
  'function $g(id){ return document.getElementById(id); }'
].join('\n\n');
const tool2Src = extractTool2Block();
const recSrc = extractRecScript();
const sharedSrc = extractShared();
// The workbook is captured instead of being zipped and downloaded.
const overrideSrc = `
_writeXlsxNoGridlines = async function(wb, fname){ __xlsx.wb = wb; __xlsx.file = fname; };
`;

// ── shims ──────────────────────────────────────────────────────────────────
function makeEl(id) {
  return {
    id, textContent: '', innerHTML: '', value: '', style: {}, children: [], disabled: false,
    classList: { add() { }, remove() { }, toggle() { }, contains() { return false; } },
    addEventListener() { }, appendChild(c) { this.children.push(c); return c; }, removeChild() { },
    focus() { }, click() { }, scrollIntoView() { }, getBoundingClientRect() { return { right: 0, bottom: 0 }; },
    contains() { return false; }, closest() { return null; }, offsetWidth: 200
  };
}

function encodeCol(c) { let s = ''; c++; while (c > 0) { const m = (c - 1) % 26; s = String.fromCharCode(65 + m) + s; c = Math.floor((c - 1) / 26); } return s; }
function encodeCell(rc) { return encodeCol(rc.c) + (rc.r + 1); }
function makeXLSX() {
  return {
    utils: {
      book_new() { return { SheetNames: [], Sheets: {} }; },
      encode_cell: encodeCell,
      encode_range(r) { return encodeCell(r.s) + ':' + encodeCell(r.e); },
      aoa_to_sheet(aoa) {
        const ws = { __aoa: aoa };
        let maxR = 0, maxC = 0;
        aoa.forEach((row, r) => {
          if (!row) return;
          for (let c = 0; c < row.length; c++) {
            const v = row[c];
            if (v === undefined || v === null) continue; // holes / null: no cell (SheetJS default)
            ws[encodeCell({ r, c })] = { v, t: typeof v === 'number' ? 'n' : typeof v === 'boolean' ? 'b' : 's' };
            maxR = Math.max(maxR, r); maxC = Math.max(maxC, c);
          }
        });
        ws['!ref'] = 'A1:' + encodeCell({ r: maxR, c: maxC });
        return ws;
      },
      book_append_sheet(wb, ws, name) { wb.SheetNames.push(name); wb.Sheets[name] = ws; }
    }
  };
}
function dumpWorkbook(wb) {
  return wb.SheetNames.map(name => {
    const ws = wb.Sheets[name];
    const cells = {};
    Object.keys(ws).filter(k => /^[A-Z]+\d+$/.test(k)).forEach(k => {
      const c = ws[k];
      const o = {};
      if (c.v !== undefined) o.v = c.v;
      if (c.t) o.t = c.t;
      if (c.s) o.s = c.s;
      if (c.z) o.z = c.z;
      if (c.l) o.l = c.l;
      cells[k] = o;
    });
    const rows = (ws.__aoa || []).map(r => (r ? Array.from(r, v => (v === undefined ? null : v)) : []));
    return { name, rows, cols: ws['!cols'] || null, merges: ws['!merges'] || null, autofilter: ws['!autofilter'] || null, cells };
  });
}

const driverSrc = `
(async function(){
  const sc = __scenario;
  const out = {};
  const ent = s => s.replace(/&nbsp;/g,' ').replace(/&le;/g,'≤').replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&times;/g,'×').replace(/&quot;/g,'"').replace(/&#9656;/g,'▸').replace(/&#9662;/g,'▾').replace(/&#9658;/g,'►').replace(/&#9660;/g,'▼').replace(/&minus;/g,'−');
  const tokens = html => String(html).split(/<\\/?[a-zA-Z][^>]*>/).map(s => ent(s).trim()).filter(Boolean);
  const el = id => document.getElementById(id);
  const all = (re, s) => { const r = []; let m; while ((m = re.exec(s))) r.push(m); return r; };
  const widths = html => all(/width:([-0-9.e]+)%/g, html).map(m => parseFloat(m[1]));

  runAssessment();
  out.status = el('ass-status').textContent;
  out.toasts = __toasts.slice();
  out.saved = __saved.payload;
  out.summary = __saved.summary;

  // per-line display texts
  out.lines = batchResults.map(r => {
    const opts4 = _rankedOptionsAny(r, 4);
    const rcOpts = _reduceCancelOptions(r, 3);
    return {
      tooltip: buildDecisionTooltip(r),
      reason: tokens(buildDetailedReason(r)),
      recommendedActionA: _recommendedActionA(r),
      rankedOptions: opts4,
      rcOptions: rcOpts,
      rcRecommendation: _reduceCancelRecommendation(r, rcOpts),
      poStatus: _poStatusFlag(r),
      retention: _poRetentionInfo(r),
      q1PeerAvg: _q1PeerAvgPDSTR(r),
      allSiblings: _allSiblingsWithPDSTR(r),
      bobsQ1: _bobsQ1OptionsArray(r, 3),
      trueRepeatRec: _trueRepeatRecommendation(r),
      bucket: _bucketFor(r),
      colourOptionsText: _formatColourOptions(_rankedColoursB(r, 3), 'These colours in this MC currently have healthy sell-through: '),
      driver: _aiDriverBucketT2(r.decisionReason),
      catHead: _catHeadDisplayName(_catHeadEmailFor(_buildCatHeadMap(), (r.info&&r.info.brand)||'NON-PL', r.info&&r.info.mcCd, r.mrp))
    };
  });

  // dashboard (renderAssessmentDashboard ran at the end of the batch)
  out.kpi = tokens(el('ass-kpi').innerHTML);
  out.divChart = { tokens: tokens(el('ass-div-chart').innerHTML), widths: widths(el('ass-div-chart').innerHTML) };
  out.monthChart = { tokens: tokens(el('ass-month-chart').innerHTML), widths: widths(el('ass-month-chart').innerHTML) };
  out.engineSplit = { tokens: tokens(el('ass-engine-split').innerHTML), widths: widths(el('ass-engine-split').innerHTML) };
  const opts = id => all(/<option value="([^"]*)">/g, el(id).innerHTML).map(m => m[1]).slice(1); // drop the "All …" placeholder
  out.filterOptions = {};
  for (const id of ['ass-filter-brand','ass-filter-div','ass-filter-mc','ass-filter-mrp','ass-filter-mon','rollup-filter-div','rollup-filter-mc','rollup-filter-mrp','rollup-filter-mon'])
    out.filterOptions[id] = opts(id);
  const idxOf = new Map(batchResults.map((r,i)=>[r,i]));
  const node = n => ({ total:n.total, retain:n.retain, reduce:n.reduce, cancel:n.cancel, watch:n.watch, atRisk:n.atRisk });
  const ord = o => Object.entries(o).sort((a,b)=>b[1].atRisk-a[1].atRisk);
  out.tree = ord(window._brandTree).map(([b,bn]) => Object.assign({ label:b }, node(bn), { divs: ord(bn.divs).map(([d,dn]) => Object.assign({ label:d }, node(dn), {
    mcs: ord(dn.mcs).map(([m,mn]) => Object.assign({ label:m }, node(mn), { mrps: ord(mn.mrps).map(([p,pn]) => Object.assign({ label:p }, node(pn), {
      rows: [...pn.rows].sort((a,b)=>b.relQty-a.relQty).map(r=>idxOf.get(r)) })) })) })) }));
  out.treeTokens = tokens(el('ass-tree-body').innerHTML).length;
  out.catHeads = _allCatHeadNamesForExport();
  out.groupsB = buildEngineB_Groups(batchResults.filter(r=>r.engine==='B')).map(g => Object.assign({}, g, { info: undefined, chain: undefined }));

  // detail view (filterAssTable) for several filter combinations
  out.detail = [];
  for (const v of sc.detail) {
    for (const id of ['ass-filter-brand','ass-filter-div','ass-filter-mc','ass-filter-mrp','ass-filter-mon','ass-filter-dec']) el(id).value = '';
    Object.entries(v.f || {}).forEach(([k,val]) => el('ass-filter-' + k).value = val);
    setEngineTab(v.tab);
    out.detail.push({ name: v.name, header: tokens(el('ass-table-header').innerHTML), count: el('detail-row-count').textContent,
      noAction: el('ass-no-action').style.display, body: tokens(el('ass-table-body').innerHTML), banner: tokens(el('eng-context-banner').innerHTML) });
  }
  // rollup tables (renderRollupTables)
  out.rollup = [];
  for (const v of sc.rollup) {
    for (const id of ['div','mc','mrp','mon','dec']) el('rollup-filter-' + id).value = '';
    Object.entries(v.f || {}).forEach(([k,val]) => el('rollup-filter-' + k).value = val);
    renderRollupTables();
    out.rollup.push({ name: v.name, vendor: tokens(el('ass-vendor-body').innerHTML), divmc: tokens(el('ass-divmc-body').innerHTML) });
  }

  // CSV
  __blobs.length = 0; __anchors.length = 0;
  exportAssCSV();
  out.csv = { text: __blobs[0], file: __anchors[0] };

  // Excel
  out.xlsx = [];
  for (const sel of sc.xlsx) {
    _exportCatHeadSelection.clear(); sel.forEach(n => _exportCatHeadSelection.add(n));
    _catHeadColorCache.clear();
    __xlsx.wb = null; __xlsx.file = null; __toasts.length = 0;
    await exportAssXLSX();
    out.xlsx.push({ selection: sel, file: __xlsx.file, toast: __toasts[0] || null, sheets: __xlsx.wb ? __dumpWorkbook(__xlsx.wb) : null });
  }
  return out;
})()
`;

async function runScenario(sc) {
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
  const params = Object.assign({ T1: 75, T2: 100, D: 75, mix: 2, N: 2, wof: 4, peer: 5, mixWindowDays: 45, borderTolerance: 10, strBuffer: 5, prevMonthPendingCutoffDay: 5 }, sc.params || {});
  const S = { files, params, requests: JSON.parse(JSON.stringify(dataset.requests)), reqCount: 0, role: 'planner' };
  const els = {};
  const anchors = [], blobs = [];
  const document = {
    getElementById(id) { return els[id] || (els[id] = makeEl(id)); },
    createElement(t) { const e = makeEl(t); e.click = function () { anchors.push(this.download); }; return e; },
    querySelectorAll() { return []; },
    addEventListener() { },
    body: { appendChild() { }, removeChild() { } }
  };
  const toasts = [];
  const saved = {};
  const xlsx = { wb: null, file: null };
  const ctx = vm.createContext({
    S, document, Date: FakeDate, Math, JSON, Object, Array, String, Number, Map, Set, Promise, RegExp, parseFloat, parseInt, isFinite, isNaN,
    console, alert: () => { }, toast: m => toasts.push(m), setTimeout: f => f(),
    Blob: function (parts) { blobs.push(parts.join('')); }, URL: { createObjectURL: () => 'blob:x', revokeObjectURL() { } },
    XLSX: makeXLSX(), innerWidth: 1400,
    __toasts: toasts, __blobs: blobs, __anchors: anchors, __xlsx: xlsx, __saved: saved, __scenario: sc, __dumpWorkbook: dumpWorkbook
  });
  ctx.window = ctx;
  ctx.Shared = {
    serverToday: sc.today,
    saveRun() { saved.payload = { params: JSON.parse(JSON.stringify(S.params)), packed: ctx.__packResults(ctx.batchResultsRef()) }; saved.summary = ctx.__runSummary(ctx.batchResultsRef()); }
  };
  const src = helperSrc + '\n' + tool2Src + '\n' + overrideSrc + '\n' + sharedSrc +
    '\nvar __packResults = packResults, __runSummary = runSummary; function batchResultsRef(){ return batchResults; }\n' +
    recSrc + '\n' + driverSrc;
  return await vm.runInContext(src, ctx, { filename: 'index-extract.js' });
}

// ── scenarios ──────────────────────────────────────────────────────────────
const detailCommon = [
  { name: 'B-default', tab: 'B' },
  { name: 'A-default', tab: 'A' },
  { name: 'B-cancel', tab: 'B', f: { dec: 'Cancel' } },
  { name: 'B-reduce', tab: 'B', f: { dec: 'Reduce Qty' } },
  { name: 'B-retain', tab: 'B', f: { dec: 'Retain' } },
  { name: 'B-watch', tab: 'B', f: { dec: 'Watch List' } },
  { name: 'A-watch', tab: 'A', f: { dec: 'Watch List' } },
  { name: 'A-cancel', tab: 'A', f: { dec: 'Cancel' } },
  { name: 'A-retain', tab: 'A', f: { dec: 'Retain' } },
  { name: 'B-month', tab: 'B', f: { mon: '08.2026' } },
  { name: 'A-month-brand', tab: 'A', f: { mon: '09.2026', brand: 'NON-PL' } },
  { name: 'B-mrp-div', tab: 'B', f: { mrp: '₹399', div: 'MENS' } },
  { name: 'A-mc', tab: 'A', f: { mc: 'L-JEANS' } },
  { name: 'B-none', tab: 'B', f: { brand: 'NOPE' } }
];
const rollupCommon = [
  { name: 'all' },
  { name: 'div', f: { div: 'MENS' } },
  { name: 'dec-cancel', f: { dec: 'Cancel' } },
  { name: 'mrp-mon', f: { mrp: '₹599', mon: '12.2026' } },
  { name: 'mc', f: { mc: 'K-SHORTS' } },
  { name: 'none', f: { div: 'NOPE' } }
];
const scenarios = [
  { name: 'S1-early-month', today: '2026-10-03', params: { seasonCurrent: ['AW26'], seasonFallback: ['SS26'] },
    detail: detailCommon, rollup: rollupCommon, xlsx: [[], ['Sukhdev Singh'], ['Anita Rao', 'Unassigned'], ['Nobody']] },
  { name: 'S2-params', today: '2026-10-14',
    params: { T2: 40, strBuffer: 0, D: 45, mix: 5, borderTolerance: 25, wof: 3, peer: 3, N: 1, seasonCurrent: ['SS26'], seasonFallback: ['AW26'] },
    detail: detailCommon, rollup: rollupCommon, xlsx: [[], ['Priya K']] },
  { name: 'S3-no-mapping', today: '2026-12-02', params: { T2: 60, strBuffer: 12.5, seasonCurrent: ['AW26', 'SS26'], seasonFallback: [], prevMonthPendingCutoffDay: 0 },
    drop: ['catheadmap', 'attrrel'], detail: detailCommon, rollup: rollupCommon, xlsx: [[]] }
];

(async () => {
  const expected = { generatedBy: 'tests/parity/tool2/run-parity.js', source: 'legacy/python-app/app/index.html + shared.js (v1.1.4)', scenarios: [] };
  for (const sc of scenarios) {
    const res = await runScenario(sc);
    expected.scenarios.push(Object.assign({ name: sc.name, today: sc.today, params: sc.params || {}, drop: sc.drop || [],
      detailCases: sc.detail, rollupCases: sc.rollup, xlsxCases: sc.xlsx }, { result: res }));
    const counts = {};
    res.saved.packed.results.forEach(r => { counts[r.engine + ':' + r.decision] = (counts[r.engine + ':' + r.decision] || 0) + 1; });
    console.log(sc.name, res.status, JSON.stringify(counts));
  }
  fs.writeFileSync(path.join(FIX, 'expected.json'), JSON.stringify(expected));
  console.log('wrote expected.json');
})().catch(e => { console.error(e); process.exit(1); });
