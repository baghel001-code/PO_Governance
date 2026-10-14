// GRC Back Testing + Season Selection parity harness.
//
// Runs the ORIGINAL v1.1.4 functions (extracted verbatim from
// legacy/python-app/app/index.html) on a deterministic synthetic fixture and
// writes:
//   fixture.json  — the input dataset (S.files + S.params) and every scenario
//   expected.json — what the original code produced (data + parsed screen output)
// The xUnit tests in tests/POGovernance.Tests/Analytics/Grc assert that the C#
// port returns the same.
//
// Usage:  node tests/parity/grc/run.js
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const { loadSource, extractFunction, extractLine } = require('./extract');

const src = loadSource();

// ── 1. Original code, verbatim ──────────────────────────────────────────
const FUNCS = [
  'col', '_buildUMIndex', '_buildResolveIndex', 'getPDSTR', 'quartile',
  '_buildCatHeadMap', '_catHeadEmailFor', '_catHeadDisplayName', '_populateSelectPreserving', '$g',
  '_seasonSortKey', '_allSeasonsSorted', 'renderSeasonPills', '_toggleSeasonPill',
  '_isoWeekToDate', '_grcAsOfDate', '_weeksElapsedSincePeriod', '_weekPeriodToMonthLabel',
  '_bsrQHistorical', '_computeGRCBacktest', '_traceArticleHistory', 'renderArticleTrace',
  '_grcResetFilters', 'renderGRCImpact', 'exportGRCRowDetail',
];
const LINES = [
  'const num = ', 'const _colNorm = ', 'const _idx={};', 'function _n(v)', 'function _sf(v)',
  'let _bsrPoolCache = null;', 'const GRC_BUCKET_ORDER', 'const GRC_BUCKET_COLOR',
  'let _grcBacktestCache = null', 'const GRC_TRACE_BUCKET_COLOR',
];
let code = '';
for (const l of LINES) code += extractLine(src, l) + '\n';
for (const f of FUNCS) code += extractFunction(src, f) + '\n';

// ── 2. Minimal shims: S, document, toast, XLSX ──────────────────────────
const shim = `
const S = { files: {}, params: {} };
function __setState(files, params){
  S.files = files; S.params = params;
  _idx.um = null; _idx.cybsr = null; _idx.resolveKey = null; _idx.catHeadMapSrc = null; _idx.catHeadMapCache = null;
  _grcBacktestCache = null; _grcBacktestCacheKey = null;
}
globalThis.__api = {
  __setState, S: () => S,
  _seasonSortKey, _allSeasonsSorted, renderSeasonPills, _toggleSeasonPill,
  _isoWeekToDate, _grcAsOfDate, _weeksElapsedSincePeriod, _weekPeriodToMonthLabel,
  _computeGRCBacktest, _traceArticleHistory, renderArticleTrace, _grcResetFilters, renderGRCImpact,
  exportGRCRowDetail, _catHeadDisplayName,
};
`;

class FakeEl {
  constructor(id) { this.id = id; this.innerHTML = ''; this.style = {}; this.value = ''; }
}
class FakeSelect {
  constructor(id, html) { this.id = id; this.style = {}; this._opts = []; this._value = ''; this.innerHTML = html || ''; }
  get innerHTML() { return this._html; }
  set innerHTML(h) {
    this._html = h;
    this._opts = [];
    let sel = null;
    const re = /<option value="([^"]*)"([^>]*)>([\s\S]*?)<\/option>/g;
    let m;
    while ((m = re.exec(h))) {
      const v = m[1].replace(/&quot;/g, '"');
      this._opts.push({ value: v, label: m[3] });
      if (sel === null && /\bselected\b/.test(m[2])) sel = v;
    }
    this._value = sel !== null ? sel : (this._opts.length ? this._opts[0].value : '');
  }
  get value() { return this._value; }
  set value(v) { this._value = this._opts.some(o => o.value === v) ? v : ''; }
  get options() { return this._opts; }
}

let toasts = [], written = [];
const els = {};
function resetDom() {
  for (const k of Object.keys(els)) delete els[k];
  const sel = (id, html) => { els[id] = new FakeSelect(id, html); };
  sel('grc-as-of', '');
  sel('grc-view-mode', '<option value="native">Week (native)</option><option value="month">Month (rolled up)</option>');
  sel('grc-cutover-month', '');
  sel('grc-f-brand', '<option value="">All Brands</option>');
  sel('grc-f-division', '<option value="">All Divisions</option>');
  sel('grc-f-cathead', '<option value="">All Category Heads</option>');
  sel('grc-drill-month', '<option value="">All months combined</option>');
  for (const id of ['grc-impact-body', 'grc-viewmode-wrap', 'grc-trace-body', 'grc-trace-gm', 'season-pills-current',
    'season-pills-fallback', 'grc-impact-placeholder', 'grc-impact-controls']) els[id] = new FakeEl(id);
}
const sandbox = {
  document: { getElementById: id => els[id] || null },
  toast: m => toasts.push(m),
  XLSX: {
    utils: {
      book_new: () => ({ sheets: [] }),
      aoa_to_sheet: rows => ({ rows }),
      encode_range: r => ({ s: r.s, e: r.e }),
      book_append_sheet: (wb, ws, name) => wb.sheets.push({ name, ws }),
    },
    writeFile: (wb, name) => written.push({ name, wb }),
  },
  console,
};
vm.createContext(sandbox);
vm.runInContext(code + shim, sandbox, { filename: 'index.html-extract.js' });
const api = sandbox.__api;

// ── 3. Deterministic synthetic fixture ─────────────────────────────────
function mulberry32(a) { return function () { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
const rnd = mulberry32(20261014);
const ri = (a, b) => a + Math.floor(rnd() * (b - a + 1));
const pick = arr => arr[Math.floor(rnd() * arr.length)];

const BRANDS = ['PL', 'NON-PL', 'VMM PL'];
const DIVS = ['MENS', 'LADIES', 'KIDS', 'ACCESSORIES'];
const MCS = ['1001', '1002', '1003', '1004'];
const MRPS = [299, 399, 499, '1,299'];
const SEASONS = ['AW26', 'AW26', 'SS26', 'AW_26', 'AW25', 'SS26', 'ss-26', 'SS 2026', 'Core', 'SS25', '', 'AW26', 'aw 26'];
const COLOURS = ['BLACK', 'NAVY', 'RED', 'WHITE', 'GREEN'];

const artmaster = [];
const gmInfo = {};
for (let i = 0; i < 200; i++) {
  const gm = String(1110064000 + i);
  const brand = i % 17 === 0 ? '' : BRANDS[i % 3];
  const div = i % 13 === 0 ? '' : DIVS[(i >> 1) % 4];
  const mc = i % 29 === 5 ? '' : MCS[(i * 7) % 4];
  const mrp = MRPS[(i >> 2) % 4];
  const flag = i % 5 === 0 ? 'BOGO' : i % 11 === 0 ? '' : 'NON-BOGO';
  const season = SEASONS[i % SEASONS.length];
  gmInfo[gm] = { brand, div, mc, mrp, flag, season };
  const row = { 'Generic Material': i % 2 ? gm : Number(gm), 'Brand Type': brand, 'Division': div, 'MC CD': mc,
    'MC DES': mc ? 'MC DESC ' + mc : '', 'MRP': mrp, 'Flag (BOGO/NON-BOGO)': flag, 'Season': season, 'ART DESC': 'ART ' + i };
  artmaster.push(row);
}
const umGms = ['1110064003', '1110064010', '1110064022', '1110064041', '1110064057'];
const um = umGms.map(g => ({ 'Generic Material': g, 'MC CD': '9999', 'MRP': 199, 'Division': 'UMDIV' }));
for (let j = 0; j < 5; j++) um.push({ 'Generic Material': String(1110065000 + j), 'MC CD': MCS[j % 4], 'MRP': 399, 'Division': 'MENS', 'Brand Type': 'PL' });
const f2 = [];
for (let j = 0; j < 3; j++) f2.push({ 'Generic Material': String(1110066000 + j), 'MC CD': MCS[j], 'MRP': 499, 'Division': 'LADIES', 'Brand Type': 'NON-PL', 'Flag': 'NON-BOGO' });

const bsr = [];
for (let i = 0; i < 200; i++) {
  const gm = String(1110064000 + i);
  const inf = gmInfo[gm];
  const nCol = 2 + (i % 3);
  for (let c = 0; c < nCol; c++) {
    const r = {};
    r['Generic Material'] = i % 2 === 0 ? Number(gm) : gm;
    r[i % 9 === 0 ? 'Colour' : 'Color'] = COLOURS[c];
    r[i % 10 === 0 ? 'MC_CD' : 'MC CD'] = i % 23 === 0 ? '1004' : (inf.mc || '1001');
    r['MRP'] = inf.mrp;
    if (i % 7 === 0) r['BOGO/NON-BOGO'] = inf.flag; else r['Article Type'] = i % 6 === 0 ? '' : inf.flag;
    const wof = ri(0, 45);
    r[i % 8 === 0 ? 'WOF' : 'WEEK ON FLOOR'] = i % 4 === 0 ? String(wof) : wof;
    const k = rnd();
    r['AVG PERDAY SELL THRU%'] = k < 0.12 ? 0 : k < 0.4 ? (rnd() * 3).toFixed(2) + '%' : k < 0.45 ? '' : Math.round(rnd() * 300) / 10000;
    bsr.push(r);
    if (i % 31 === 0) bsr.push(Object.assign({}, r, { 'AVG PERDAY SELL THRU%': 0.5 })); // duplicate gm|colour — first wins
  }
}
const EMAILS = ['sukhdev.singh@vishalwholesale.co.in', 'anita_rao2@vishalwholesale.co.in', 'r.k.verma@vishalwholesale.co.in', 'priya@vishalwholesale.co.in', '123@vishalwholesale.co.in'];
const catheadmap = [];
for (const b of BRANDS) for (const m of MCS) for (const p of MRPS) {
  const r = rnd();
  if (r < 0.25) continue;
  const row = { 'Brand Type': b, 'MC CD': m, 'MRP': p };
  row[r < 0.35 ? 'Level 3' : 'Category_Email_ID'] = pick(EMAILS);
  catheadmap.push(row);
  if (r > 0.9) catheadmap.push(Object.assign({}, row, { 'Category_Email_ID': 'dup.person@x.com' }));
}
catheadmap.push({ 'Brand Type': 'NON-PL', 'MC CD': '1001', 'MRP': 299, 'Category_Email_ID': '' });

const weekLabels = [];
for (let w = 45; w <= 52; w++) weekLabels.push(String(w).padStart(2, '0') + '.2025');
for (let w = 1; w <= 14; w++) weekLabels.push(String(w).padStart(2, '0') + '.2026');
const monthLabels = ['09.2025', '10.2025', '11.2025', '12.2025', '01.2026', '02.2026', '03.2026'];

function makeGrc(periods, periodType) {
  const rows = [];
  const gms = [];
  for (let i = 0; i < 200; i += 2) gms.push(String(1110064000 + i));
  gms.push('1110064003', '1110064041', '1110065001', '1110065003', '1110066000', '1110066002', '9990000001', '9990000002');
  for (const gm of gms) {
    const nCol = 1 + (Number(gm) % 3);
    for (let c = 0; c <= nCol; c++) { // one extra colour that is NOT in BSR -> No Peer Data
      const colour = c === nCol ? 'MAROON' : COLOURS[c];
      for (const p of periods) {
        if (rnd() < 0.55) continue;
        const qty = rnd() < 0.1 ? ri(1, 40) + 0.5 : ri(1, 600);
        const brand = rnd() < 0.05 ? 'NON-PL' : (gmInfo[gm] ? (gmInfo[gm].brand || 'NON-PL') : 'PL');
        const value = periodType === 'week' ? (rnd() < 0.1 ? null : Math.round(qty * 180.37 * 100) / 100) : null;
        rows.push({ brand, gm, artdesc: rnd() < 0.2 ? '' : 'DESC ' + gm, colour, period: p, periodType, qty, value });
      }
    }
  }
  return rows;
}
const grcWeek = makeGrc(weekLabels, 'week');
const grcMonth = makeGrc(monthLabels, 'month');

const baseParams = { T1: 75, T2: 100, D: 75, mix: 2, N: 2, wof: 4, peer: 5, mixWindowDays: 45, borderTolerance: 10, strBuffer: 5, prevMonthPendingCutoffDay: 5 };
const datasets = {
  week: { files: { artmaster, um, f2, bsr, catheadmap, grc: grcWeek }, grcPeriodType: 'week' },
  month: { files: { artmaster, um, f2, bsr, catheadmap, grc: grcMonth }, grcPeriodType: 'month' },
};

// ── 4. Screen-output parsing (HTML → comparable structure) ─────────────
const strip = h => String(h).replace(/<[^>]*>/g, '').replace(/&quot;/g, '"').replace(/\s+/g, ' ').trim();
function cellColor(attr) { const m = /(?:^|[;"\s])color:(#[0-9A-Fa-f]{6})/.exec(attr); return m ? m[1] : null; }
function parseTables(html) {
  const out = [];
  const tre = /<table[\s\S]*?<\/table>/g;
  let t;
  while ((t = tre.exec(html))) {
    const rows = [];
    const rre = /<tr([^>]*)>([\s\S]*?)<\/tr>/g;
    let r;
    while ((r = rre.exec(t[0]))) {
      const cells = [];
      const cre = /<t[hd]([^>]*)>([\s\S]*?)<\/t[hd]>/g;
      let c;
      while ((c = cre.exec(r[2]))) {
        const span = /<span style="([^"]*)">/.exec(c[2]);
        cells.push({ text: strip(c[2]), color: cellColor(c[1]) || (span ? cellColor(span[1].replace(/background:[^;]*;/, '')) : null) });
      }
      rows.push(cells);
    }
    out.push(rows);
  }
  return out;
}
function parseImpact(html) {
  if (!/<table/.test(html)) return { message: strip(html) };
  const v = { message: null };
  const warn = /<div[^>]*>(⚠ Coverage[\s\S]*?)<\/div>/.exec(html);
  v.warning = warn ? strip(warn[1]) : null;
  const svg = /<svg[\s\S]*?<\/svg>/.exec(html)[0];
  v.svgRects = [...svg.matchAll(/<rect x="([^"]*)" y="([^"]*)" width="([^"]*)" height="([^"]*)" fill="([^"]*)"\/>/g)].map(m => ({ h: Number(m[4]), fill: m[5] }));
  v.svgTexts = [...svg.matchAll(/<text[^>]*>([^<]*)<\/text>/g)].map(m => m[1]);
  v.tables = parseTables(html);
  // coverage bars
  const covBlock = /Coverage — %[\s\S]*?<\/div>([\s\S]*?)<div style="padding:14px 16px;background:#F9FAFB;border-radius:10px;margin-bottom:20px">/.exec(html);
  v.coverageTitle = strip(/(Coverage — %[^<]*)/.exec(html)[1]);
  v.coverage = [...html.matchAll(/<div style="width:56px;[^"]*">([^<]*)<\/div>\s*<div[^>]*><div style="width:([^%]*)%;height:100%;background:([^;]*);[^"]*"><\/div><\/div>\s*<div[^>]*>([^<]*)<\/div>/g)]
    .map(m => ({ period: m[1], width: Number(m[2]), color: m[3], text: m[4] }));
  v.quartileTitle = strip(/(Quartile % by [^<]*)/.exec(html)[1]);
  const bh = /<div style="font-size:10\.5px;font-weight:700;color:var\(--ink3\);text-transform:uppercase;letter-spacing:\.05em;margin-bottom:12px">([\s\S]*?)<\/div>/.exec(html);
  v.breakdownHeading = bh ? strip(bh[1]) : null;
  v.breakdownTitles = [...html.matchAll(/<div style="font-size:11px;font-weight:700;color:var\(--ink3\);text-transform:uppercase;letter-spacing:\.05em;margin-bottom:8px">([^<]*)<\/div>/g)].map(m => m[1]);
  const grid = /<div style="display:grid;grid-template-columns:1fr 1fr;gap:14px;margin-bottom:20px">([\s\S]*?)<\/div>\s*<div style="overflow-x:auto">\s*<table/.exec(html)[1];
  // split the two cards: each starts with <div style="background:
  const cardStarts = [...grid.matchAll(/<div style="background:(#fff|#F9FAFB);border:1px solid/g)].map(m => m.index);
  v.cards = cardStarts.map((s, i) => strip(grid.slice(s, cardStarts[i + 1] || grid.length)));
  v.monthNote = /How weeks are mapped to months/.test(html);
  const foot = /<div style="margin-top:16px;font-size:11px;color:var\(--ink3\);line-height:1\.6">([\s\S]*?)<\/div>/.exec(html);
  v.footer = foot ? strip(foot[1]) : null;
  return v;
}
function selectState(id) { const e = els[id]; return { value: e.value, options: e.options.map(o => o.value) }; }

// ── 5. Scenarios ───────────────────────────────────────────────────────
function setup(ds, paramsOver) {
  const d = datasets[ds];
  const files = Object.assign({}, d.files, { grcPeriodType: d.grcPeriodType });
  const params = JSON.parse(JSON.stringify(Object.assign({}, baseParams, paramsOver || {})));
  api.__setState(files, params);
  resetDom();
  // Data Setup boot: artmaster loaded → Season Selection defaults (shared.js afterDataChange)
  api.renderSeasonPills();
  // GRC loaded → cutover picker gets the native periods, last one selected (_renderGRCMonthPicker)
  const periods = [...new Set(files.grc.map(r => r.period))].sort((a, b) => {
    const [an, ay] = a.split('.'), [bn, by] = b.split('.');
    return (parseInt(ay) * 100 + parseInt(an)) - (parseInt(by) * 100 + parseInt(bn));
  });
  els['grc-cutover-month'].innerHTML = periods.map((m, i) => `<option value="${m}" ${i === periods.length - 1 ? 'selected' : ''}>${m}</option>`).join('');
}
function setSelect(id, v) {
  if (v == null) return;
  const e = els[id];
  if (!e.options.some(o => o.value === v)) e.innerHTML = e.innerHTML + `<option value="${v}">${v}</option>`;
  e.value = v;
}

const impactScenarios = [
  { name: 'W1-default', ds: 'week' },
  { name: 'W2-asof-mid-cutover-drill', ds: 'week', req: { asOfPeriod: '08.2026', cutover: '02.2026', drill: '05.2026' } },
  { name: 'W3-monthview-cutover-miss', ds: 'week', req: { viewMode: 'month', cutover: '06.2026' } },
  { name: 'W4-monthview-cutover-coincide-drill', ds: 'week', req: { viewMode: 'month', cutover: '01.2026', drill: '02.2026' } },
  { name: 'W5-division', ds: 'week', req: { division: 'MENS' } },
  { name: 'W6-cathead-brand', ds: 'week', req: { catHead: 'Sukhdev Singh', brand: 'PL' } },
  { name: 'W7-brand-drill', ds: 'week', req: { brand: 'NON-PL', drill: '50.2025', cutover: '48.2025' } },
  { name: 'W8-filter-nothing', ds: 'week', req: { division: 'ZZZ' } },
  { name: 'W9-wof0-peer3', ds: 'week', params: { wof: 0, peer: 3 }, req: { cutover: '03.2026' } },
  { name: 'W10-asof-from-params', ds: 'week', params: { grcAsOfPeriod: '10.2026' }, req: { asOfPeriod: '99.2030' } },
  { name: 'W11-seasons-preset', ds: 'week', params: { seasonCurrent: ['AW26', 'XX99'], seasonFallback: ['SS26', 'AW25'] }, req: { cutover: '01.2026' } },
  { name: 'W12-unassigned-drill-first', ds: 'week', req: { catHead: 'Unassigned', drill: '45.2025', asOfPeriod: '02.2026' } },
  { name: 'W13-cutover-first', ds: 'week', req: { cutover: '45.2025' } },
  { name: 'M1-month-native', ds: 'month', req: { cutover: '12.2025', drill: '01.2026' } },
  { name: 'M2-month-viewmonth-asof', ds: 'month', req: { viewMode: 'month', asOfPeriod: '11.2025' } },
  { name: 'M3-month-div-drill-invalid', ds: 'month', req: { division: 'LADIES', drill: '45.2025' } },
];

const expected = { impact: [], trace: [], export: [], seasons: [], periodMath: {}, compute: [] };

for (const sc of impactScenarios) {
  setup(sc.ds, sc.params);
  const q = sc.req || {};
  if (q.asOfPeriod) { els['grc-as-of'].innerHTML = `<option value="${q.asOfPeriod}">${q.asOfPeriod}</option>`; }
  setSelect('grc-view-mode', q.viewMode || 'native');
  if (q.cutover) setSelect('grc-cutover-month', q.cutover);
  setSelect('grc-f-division', q.division || '');
  setSelect('grc-f-cathead', q.catHead || '');
  setSelect('grc-f-brand', q.brand || '');
  setSelect('grc-drill-month', q.drill || '');
  api.renderGRCImpact();
  const S = api.S();
  const view = parseImpact(els['grc-impact-body'].innerHTML);
  expected.impact.push({
    name: sc.name, ds: sc.ds, params: sc.params || null, req: q,
    seasonCurrent: S.params.seasonCurrent, seasonFallback: S.params.seasonFallback,
    asOf: S.params.grcAsOfPeriod,
    selects: {
      asOf: selectState('grc-as-of'), division: selectState('grc-f-division'), catHead: selectState('grc-f-cathead'),
      brand: selectState('grc-f-brand'), drill: selectState('grc-drill-month'), cutover: selectState('grc-cutover-month'),
    },
    drillAllLabel: els['grc-drill-month'].options.length ? els['grc-drill-month'].options[0].label : null,
    viewModeVisible: els['grc-viewmode-wrap'].style.display === 'flex',
    view,
  });

  // raw compute result for the same state (as the screen computed it)
  const bt = api._computeGRCBacktest(q.division || null, q.catHead || null, q.viewMode || 'native', q.brand || null);
  const btCopy = JSON.parse(JSON.stringify(bt));
  btCopy.flatRowCount = btCopy.flatRows.length; delete btCopy.flatRows;
  expected.compute.push({ name: sc.name, bt: btCopy });

  if (['W1-default', 'W2-asof-mid-cutover-drill', 'M1-month-native', 'W9-wof0-peer3'].includes(sc.name)) {
    // Article Trace for a set of GMs, at the same "as of"
    const gms = ['1110064000', '1110064002', '1110064008', '1110064016', '1110064003', '1110065001', '1110066000',
      '1110064199', '9990000001', ' 1110064004 ', '', '1110064034', '1110064060', '1110064120'];
    for (const gm of gms) {
      els['grc-trace-gm'].value = gm;
      api.renderArticleTrace();
      const html = els['grc-trace-body'].innerHTML;
      const t = gm.trim() ? JSON.parse(JSON.stringify(api._traceArticleHistory(gm.trim()))) : null;
      expected.trace.push({
        scenario: sc.name, gm, asOf: S.params.grcAsOfPeriod, result: t,
        html: html ? { text: strip(html), tables: parseTables(html) } : null,
        error: html && /#FEF2F2/.test(html) && !/<table/.test(html) ? strip(html) : null,
      });
    }
    // Row-detail export
    toasts = []; written = [];
    api.exportGRCRowDetail();
    const w = written[0];
    expected.export.push({
      scenario: sc.name, asOf: S.params.grcAsOfPeriod, toast: toasts[0],
      fileNamePrefix: w.name.slice(0, 15), sheet: w.wb.sheets[0].name, rows: w.wb.sheets[0].ws.rows,
      cols: w.wb.sheets[0].ws['!cols'], autofilter: w.wb.sheets[0].ws['!autofilter'].ref,
    });
  }
}

// _grcResetFilters: clears the four filter selects and re-renders
setup('week');
setSelect('grc-f-division', 'MENS'); setSelect('grc-drill-month', '02.2026');
api._grcResetFilters();
expected.resetFilters = { division: els['grc-f-division'].value, drill: els['grc-drill-month'].value, view: parseImpact(els['grc-impact-body'].innerHTML).tables.length };

// No GRC loaded
api.__setState({ artmaster, bsr }, JSON.parse(JSON.stringify(baseParams)));
resetDom();
expected.noGrc = { compute: api._computeGRCBacktest(null, null, 'native', null), trace: api._traceArticleHistory('1110064000') };
toasts = []; written = [];
api.exportGRCRowDetail();
expected.noGrc.exportToast = toasts[0];
api.renderGRCImpact();
expected.noGrc.body = els['grc-impact-body'].innerHTML;

// ── Season Selection ────────────────────────────────────────────────────
const seasonCases = [
  { name: 'defaults', art: artmaster, params: {} },
  { name: 'keep-valid', art: artmaster, params: { seasonCurrent: ['SS26', 'AW26'], seasonFallback: ['AW25'] } },
  { name: 'drop-invalid', art: artmaster, params: { seasonCurrent: ['ZZ1', 'SS25'], seasonFallback: ['QQ'] } },
  { name: 'all-invalid', art: artmaster, params: { seasonCurrent: ['ZZ1'], seasonFallback: ['QQ'] } },
  { name: 'no-artmaster', art: null, params: { seasonCurrent: ['ZZ1'], seasonFallback: [] } },
  { name: 'single-season', art: [{ Season: 'aw-2027' }, { SEASON: 'AW27' }, { season: 'junk' }, { Season: 'SS 99' }, { Season: 'AW026' }], params: {} },
  { name: 'no-season-col', art: [{ X: 1 }], params: { seasonCurrent: ['A'] } },
];
for (const c of seasonCases) {
  api.__setState(c.art ? { artmaster: c.art } : {}, JSON.parse(JSON.stringify(Object.assign({}, baseParams, c.params))));
  resetDom();
  const sorted = api._allSeasonsSorted();
  api.renderSeasonPills();
  const S = api.S();
  expected.seasons.push({ name: c.name, params: c.params, sorted, current: S.params.seasonCurrent || null, fallback: S.params.seasonFallback || null,
    message: /No Season column/.test(els['season-pills-current'].innerHTML) ? strip(els['season-pills-current'].innerHTML) : null });
}
const toggles = [['current', 'AW25'], ['current', 'AW26'], ['fallback', 'SS26'], ['fallback', 'SS26'], ['current', 'SS26']];
api.__setState({ artmaster }, JSON.parse(JSON.stringify(baseParams)));
resetDom();
api.renderSeasonPills();
expected.seasonToggles = [];
for (const [g, s] of toggles) {
  api._toggleSeasonPill(g, s);
  const S = api.S();
  expected.seasonToggles.push({ group: g, season: s, current: S.params.seasonCurrent.slice(), fallback: S.params.seasonFallback.slice() });
}
expected.seasonKeys = ['SS26', 'aw26', 'AW 2026', 'ss_25', 'AW-126', 'SS2', 'XX26', '', 'AW99', 'SS00'].map(s => ({ s, key: api._seasonSortKey(s) }));

// ── Period math ─────────────────────────────────────────────────────────
const iso = [], monthOf = [];
for (let y = 2018; y <= 2030; y++) for (let w = 1; w <= 53; w++) {
  iso.push({ w, y, ms: api._isoWeekToDate(w, y).getTime() });
  monthOf.push({ p: String(w).padStart(2, '0') + '.' + y, m: api._weekPeriodToMonthLabel(String(w).padStart(2, '0') + '.' + y) });
}
expected.periodMath.isoWeek = iso;
expected.periodMath.weekToMonth = monthOf;
const elapsed = [];
for (const [asOf, type] of [['10.2026', 'week'], ['03.2026', 'month'], ['52.2025', 'week']]) {
  api.__setState({ grcPeriodType: type }, Object.assign({}, baseParams, { grcAsOfPeriod: asOf }));
  const periods = type === 'week' ? weekLabels : monthLabels;
  for (const p of periods) elapsed.push({ asOf, type, p, weeks: api._weeksElapsedSincePeriod(p, type), asOfMs: api._grcAsOfDate().getTime() });
}
expected.periodMath.elapsed = elapsed;

// ── Write ───────────────────────────────────────────────────────────────
const fixture = { baseParams, datasets: { week: datasets.week, month: datasets.month } };
fs.writeFileSync(path.join(__dirname, 'fixture.json'), JSON.stringify(fixture));
fs.writeFileSync(path.join(__dirname, 'expected.json'), JSON.stringify(expected));
console.log('fixture: grc week rows', grcWeek.length, 'month rows', grcMonth.length, 'bsr', bsr.length);
console.log('scenarios: impact', expected.impact.length, 'trace', expected.trace.length, 'export', expected.export.length, 'seasons', expected.seasons.length);
