#!/usr/bin/env node
// Recommendation-engine parity harness.
//
// Loads the ORIGINAL v1.1.4 functions from legacy/python-app/app/index.html (the whole second
// <script> — the recommendation engine — plus the first-script helpers it calls, extracted by
// name), runs them in a vm sandbox with minimal shims (S, Shared, document, XLSX, a fixed Date)
// on Fixtures/dataset.json for several scenarios, and writes the expected results to
// tests/POGovernance.Tests/Recommendation/Fixtures/expected.json. The xUnit parity tests run the
// C# port on the same dataset and compare field by field.
//
// Run with the en-IN default locale (the tool's numbers are formatted with toLocaleString()):
//   LANG=en_IN.UTF-8 node tests/parity/recommendation/run-parity.js
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const ROOT = path.resolve(__dirname, '../../..');
const HTML = fs.readFileSync(path.join(ROOT, 'legacy/python-app/app/index.html'), 'utf8');
const FIX = path.join(ROOT, 'tests/POGovernance.Tests/Recommendation/Fixtures');
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
function extractRecScript() {
  const marker = HTML.indexOf('// RECOMMENDATION ENGINE — built on top of detection engine');
  const s = HTML.lastIndexOf('<script>', marker) + '<script>'.length;
  const e = HTML.indexOf('</script>', marker);
  return HTML.slice(s, e);
}

const helperSrc = [
  extractLine('const num = v =>'),
  extractLine('const fmt = v =>'),
  extractLine('const _colNorm = s =>'),
  extractLine('const isMMYYYY = k =>'),
  'const _idx={};',
  ...['col', '_buildUMIndex', '_buildBSRIndex', '_buildResolveIndex', 'addMonths', 'daysInMonth', 'monLabel', 'mmyyyy',
    '_prevMonthCarry', '_prevMonthCarryNote', '_ePOWithCarry', '_todayStr', '_approvedQtyFor', 'ymKey',
    'getWOF', 'getPDSTR', 'quartile', 'pctRank', 'resolveGM', 'routeRequest'].map(extractFunction),
  'let _runACache = null;', 'let _runBCache = null;',
  extractFunction('_runACached'), extractFunction('_runBCached')
].join('\n\n');
const recSrc = extractRecScript();

// ── shims ──────────────────────────────────────────────────────────────────
function makeEl(id) {
  const el = {
    id, textContent: '', innerHTML: '', value: '', style: {}, children: [], _listeners: {},
    classList: { _s: new Set(), add(c) { this._s.add(c); }, remove(c) { this._s.delete(c); }, toggle(c) { this._s.has(c) ? this._s.delete(c) : this._s.add(c); }, contains(c) { return this._s.has(c); } },
    addEventListener(t, f) { this._listeners[t] = f; },
    appendChild(c) { this.children.push(c); return c; },
    focus() { }
  };
  return el;
}

const driverSrc = `
(function(){
  const sc = __scenario;
  const out = [];
  const strip = s => String(s).replace(/<br\\s*\\/?>/g, '\\n').replace(/<[^>]+>/g, '').replace(/&amp;/g, '&').replace(/&minus;/g, '\\u2212').replace(/&lt;/g,'<').replace(/&gt;/g,'>');
  const all = (re, s) => { const r = []; let m; while ((m = re.exec(s))) r.push(m); return r; };
  function htmlTexts(html) {
    const t = {};
    const sum = /💡 (.*?)<\\/div>/.exec(html); if (sum) t.summary = strip(sum[1]);
    t.tips = all(/title="(Based on threshold STR of [^"]*)"/g, html).map(m => m[1]);
    t.watchTips = all(/<span title="(Colour Mix % = [^"]*)"/g, html).map(m => m[1]);
    t.formulas = all(/<td style="font-family:var\\(--mono\\);font-size:9\\.5px;color:#9CA3AF">(.*?)<\\/td>/g, html).map(m => strip(m[1]));
    const empty = /<div class="rec-empty"[^>]*>(.*?)<\\/div>/.exec(html); if (empty) t.empty = strip(empty[1]);
    const h1 = /<span>(Step 1 — .*?)<\\/span>/.exec(html); if (h1) t.heading = strip(h1[1]);
    const hb = /<div class="rec-sec">(.*?)<\\/div>/.exec(html); if (hb) t.heading = strip(hb[1]);
    const h2 = /<div class="rec-sec" style="margin-top:18px">(.*?)<\\/div>/.exec(html); if (h2) t.step2Heading = strip(h2[1]);
    t.traceHeaders = all(/<div style="font-size:10\\.5px;color:#6B7280;margin-bottom:8px">(.*?)<\\/div>/g, html).map(m => strip(m[1]));
    t.qtyHeaders = all(/<div style="font-size:10\\.5px;color:#1E2A55;margin-bottom:8px">(.*?)<\\/div>/g, html).map(m => strip(m[1]));
    t.options = all(/<option value="([^"]*)"( selected)?>([^<]*)<\\/option>/g, html).map(m => ({ value: m[1], selected: !!m[2], label: m[3] }));
    t.sections = all(/letter-spacing:\\.02em">(Q[12][^<]*)<\\/td>/g, html).map(m => m[1]);
    return t;
  }
  function cautionTexts(sc2) {
    if (!sc2) return null;
    const html = _cautionBoxHtml(sc2, 'x');
    return {
      texts: all(/<span>(.*? above Tool 2 threshold of .*?)<\\/span>/g, html).map(m => strip(m[1])),
      footers: all(/<div style="font-size:10px;color:#92400E;margin-top:4px">(.*?)<\\/div>/g, html).map(m => strip(m[1]))
    };
  }
  for (const c of sc.cases) {
    let result;
    try {
      switch (c.kind) {
        case 'buildMonths': result = _buildMonths(c.mc, c.mrp, c.brand, c.flag); break;
        case 'runB': result = _runB(c.mc, c.mrp, c.brand, c.flag, c.months); break;
        case 'runA': result = _runA(c.mc, c.mrp, c.brand, c.flag, c.months, c.dim); break;
        case 'runBCached': _runBCache = new Map(); result = [_runBCached(c.mc, c.mrp, c.brand, c.flag, c.months), _runBCache.size]; _runBCache = null; break;
        case 'runACached': _runACache = new Map(); result = [_runACached(c.mc, c.mrp, c.brand, c.flag, c.months), _runACached(c.mc, c.mrp, c.brand, c.flag, c.months) === _runACache.get(_runACache.keys().next().value)]; _runACache = null; break;
        case 'strCaution': { const r = _strCaution(c.mc, c.mrp, c.brand, c.flag, c.colour, c.gm, c.month); result = { res: r, box: cautionTexts(r) }; break; }
        case 'bsrQ': result = _bsrQ(c.gm, c.colour, c.mc, c.mrp, c.flag); break;
        case 'gmsForColour': result = _getGMsForColour(c.mc, c.mrp, c.brand, c.flag, c.colour); break;
        case 'recQty': result = _recQty(c.stock, c.pending, c.f4Total, c.month, c.sales45, c.totalSales45); break;
        case 'f2ByCol': result = _f2ByCol(c.mc, c.mrp, c.brand, c.flag, c.month); break;
        case 'f4': result = _f4(c.mc, c.mrp, c.brand, c.flag, c.month); break;
        case 'cumPO': result = _cumPO(c.map, c.month); break;
        case 'carry': { const k = _prevMonthCarry(c.map, c.y, c.m, c.d); result = { carry: k, note: _prevMonthCarryNote(k), ePO: c.months.map(m => _ePOWithCarry(c.map, m, k)) }; break; }
        case 'openRec': {
          __alerts.length = 0;
          const info = resolveGM(c.gm);
          const route = routeRequest(c.gm, c.colour || '');
          RS.engine = route.engine;
          RS.info = { mcCd: (info && info.mcCd) || '', mrp: (info && info.mrp) || '', brand: (info && info.brand) || '', flag: (info && info.flag) || '', div: (info && info.div) || '', mcDesc: (info && info.mcDesc) || '' };
          RS._cacheKey = null; RS._cacheRes = null; RS.dataVersion = (RS.dataVersion || 0) + 1;
          _openRec();
          const r = { engine: route.engine, alert: __alerts[0] || null };
          if (!r.alert) {
            if (c.months) { RS.months = c.months.slice(); _render(); }
            if (c.dim) _setRecDimOverride(c.dim);
            r.allMonths = RS.allMonths.slice(); r.months = RS.months.slice();
            r.title = document.getElementById('rec-title').textContent; r.sub = document.getElementById('rec-sub').textContent;
            r.res = RS._cacheRes;
            r.html = htmlTexts(document.getElementById('rec-out').innerHTML);
            if (c.export) {
              __xlsx.sheets = []; __xlsx.file = null; __toasts.length = 0;
              _exportEngineARecs();
              r.export = { file: __xlsx.file, sheets: __xlsx.sheets, toast: __toasts[0] || null };
            }
          }
          result = r; break;
        }
        default: throw new Error('unknown case ' + c.kind);
      }
    } catch (e) { result = { __error: String(e && e.stack || e) }; }
    out.push({ name: c.name, kind: c.kind, args: c, result });
  }
  return out;
})()
`;

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
  const params = Object.assign({ T1: 75, T2: 100, D: 75, mix: 2, N: 2, wof: 4, peer: 5, mixWindowDays: 45, borderTolerance: 10, strBuffer: 5, prevMonthPendingCutoffDay: 5 }, sc.params || {});
  const S = { files, params, requests: JSON.parse(JSON.stringify(dataset.requests)), reqCount: 0, role: 'planner' };
  const els = {};
  const document = {
    getElementById(id) { return els[id] || (els[id] = makeEl(id)); },
    createElement(t) { return makeEl(t); },
    querySelectorAll() { return []; }
  };
  const alerts = [], toasts = [];
  const xlsx = { sheets: [], file: null };
  const XLSX = {
    utils: {
      book_new() { return { sheets: [] }; },
      aoa_to_sheet(aoa) { return { aoa }; },
      book_append_sheet(wb, ws, name) { xlsx.sheets.push({ name, widths: (ws['!cols'] || []).map(c => c.wch), rows: ws.aoa }); }
    },
    writeFile(wb, fname) { xlsx.file = fname; }
  };
  const ctx = vm.createContext({
    S, Shared: { serverToday: sc.today }, document, Date: FakeDate, Math, JSON, Object, Array, String, Number, Map, Set, parseFloat, parseInt, isFinite, isNaN,
    console, alert: m => alerts.push(m), toast: m => toasts.push(m), setTimeout: f => f(), XLSX,
    __alerts: alerts, __toasts: toasts, __xlsx: xlsx, __scenario: sc
  });
  ctx.window = ctx;
  const src = helperSrc + '\n' + recSrc + '\n' + driverSrc;
  return vm.runInContext(src, ctx, { filename: 'index-extract.js' });
}

// ── scenarios ──────────────────────────────────────────────────────────────
const B = {
  B1: { mc: '1101', mrp: '399', brand: 'NON-PL', flag: 'NON-BOGO' },
  B2: { mc: '2203', mrp: '599', brand: 'NON-PL', flag: 'BOGO' },
  B3: { mc: '3305', mrp: '799', brand: 'MAVIE', flag: 'NON-BOGO' },
  B4: { mc: '4407', mrp: '999', brand: 'NON-PL', flag: '' },
  B5: { mc: '5509', mrp: '499', brand: 'MAVIE', flag: 'BOGO' }
};
const lc = b => ({ mc: b.mc.toLowerCase(), mrp: b.mrp, brand: b.brand.toLowerCase(), flag: b.flag.toLowerCase() });

function casesFor(today) {
  const cs = [];
  const add = (name, kind, args) => cs.push(Object.assign({ name, kind }, args));
  for (const [k, b] of Object.entries(B)) add('months-' + k, 'buildMonths', b);
  const m4 = ['10.2026', '11.2026', '12.2026', '01.2027'];
  add('runB-B1-default', 'runB', Object.assign({ months: m4 }, lc(B.B1)));
  add('runB-B1-raw', 'runB', Object.assign({ months: ['09.2026', '11.2026', '02.2027'] }, B.B1));
  add('runB-B1-odd', 'runB', Object.assign({ months: ['13.2026', '07.2026', 'x'] }, B.B1));
  add('runB-B2', 'runB', Object.assign({ months: m4 }, lc(B.B2)));
  add('runB-B4', 'runB', Object.assign({ months: ['12.2026'] }, B.B4));
  add('runB-none', 'runB', { mc: '9999', mrp: '1', brand: 'X', flag: 'Y', months: m4 });
  add('runA-B2', 'runA', Object.assign({ months: m4 }, lc(B.B2)));
  add('runA-B2-raw-2m', 'runA', Object.assign({ months: ['11.2026', '01.2027'] }, B.B2));
  add('runA-B2-style', 'runA', Object.assign({ months: m4, dim: 'Style' }, lc(B.B2)));
  add('runA-B2-nomonths', 'runA', Object.assign({ months: [] }, lc(B.B2)));
  add('runA-B2-design', 'runA', Object.assign({ months: m4, dim: 'Design' }, lc(B.B2)));
  add('runA-B3', 'runA', Object.assign({ months: m4 }, lc(B.B3)));
  add('runA-B4', 'runA', Object.assign({ months: m4 }, lc(B.B4)));
  add('runA-B5', 'runA', Object.assign({ months: m4 }, lc(B.B5)));
  add('runA-B1-fit', 'runA', Object.assign({ months: m4, dim: 'Fit' }, lc(B.B1)));
  add('runBCached-B1', 'runBCached', Object.assign({ months: m4 }, B.B1));
  add('runACached-B2', 'runACached', Object.assign({ months: ['11.2026'] }, B.B2));
  for (const [gm, col, mon] of [['2203000201', 'BLK', '11.2026'], ['2203000202', 'OWH', '10.2026'], ['2203000203', 'blu', '12.2026'],
    ['2203000204', '204', '01.2027'], ['2203000205', 'GRY', '11.2026'], ['B2203X07', 'BEG', '11.2026'], ['B2203X08', 'BLK', '11.2026'],
    ['2203000201', 'BLK', '']])
    add('caution-B2-' + gm + '-' + col + '-' + mon, 'strCaution', Object.assign({ gm, colour: col, month: mon }, B.B2));
  for (const [gm, col, mon] of [['4407000401', 'BLK', '11.2026'], ['4407000402', 'TAN', '12.2026'], ['1101000101', 'WHT', '10.2026'], ['1101000102', 'RED', '01.2027']])
    add('caution-' + gm + '-' + col, 'strCaution', Object.assign({ gm, colour: col, month: mon }, gm.startsWith('44') ? B.B4 : B.B1));
  for (const r of dataset.files.bsr.slice(0, 40)) add('bsrQ-' + r['Generic Material'] + '-' + r['Color'], 'bsrQ', { gm: r['Generic Material'], colour: r['Color'], mc: r['MC CD'], mrp: r['MRP'], flag: B.B2.flag });
  add('bsrQ-B4', 'bsrQ', { gm: '4407000401', colour: 'BLK', mc: '4407', mrp: '999', flag: '' });
  add('bsrQ-missing', 'bsrQ', { gm: 'nope', colour: 'BLK', mc: '2203', mrp: '599', flag: 'BOGO' });
  for (const col of ['BLK', 'OWH', 'GRY', 'ZZZ']) add('gms-' + col, 'gmsForColour', Object.assign({ colour: col }, B.B2));
  add('recQty-1', 'recQty', { stock: 1200, pending: 300, f4Total: 15000, month: '11.2026', sales45: 900, totalSales45: 5000 });
  add('recQty-2', 'recQty', { stock: 9000, pending: 0, f4Total: 15000, month: '02.2027', sales45: 900, totalSales45: 5000 });
  add('recQty-3', 'recQty', { stock: 10, pending: 0, f4Total: 15000, month: '13.2026', sales45: 0, totalSales45: 5000 });
  add('f2ByCol-B1', 'f2ByCol', Object.assign({ month: '11.2026' }, B.B1));
  add('f4-B1', 'f4', Object.assign({ month: '11.2026' }, B.B1));
  add('cumPO', 'cumPO', { map: { '09.2026': 10, '10.2026': 20, '12.2026': 40, 'bad': 5 }, month: '11.2026' });
  add('carry-1', 'carry', { map: { '09.2026': 120, '10.2026': 30 }, y: 2026, m: 10, d: 3, months: ['09.2026', '10.2026', '11.2026'] });
  add('carry-2', 'carry', { map: { '12.2025': 77 }, y: 2026, m: 1, d: 5, months: ['01.2026'] });
  add('carry-3', 'carry', { map: null, y: 2026, m: 1, d: 1, months: ['01.2026'] });
  // the modal, as opened from a Tool 1 result card
  add('open-B1', 'openRec', { gm: '1101000101', colour: 'BLK' });
  add('open-B1-months', 'openRec', { gm: 'A1101X04', colour: 'RED', months: ['09.2026', '12.2026', '03.2027'] });
  add('open-B2', 'openRec', { gm: '2203000201', colour: 'BLK', export: true });
  add('open-B2-bobs', 'openRec', { gm: '2203000203', colour: 'ZZZ', months: ['11.2026', '12.2026'], export: true });
  add('open-B2-style', 'openRec', { gm: '2203000202', colour: 'OWH', dim: 'Style', export: true });
  add('open-B3', 'openRec', { gm: '3305000301', colour: 'PNK', export: true });
  add('open-B4', 'openRec', { gm: '4407000401', colour: 'BLK' });
  add('open-B5', 'openRec', { gm: '5509000501', colour: 'RED' });
  add('open-new', 'openRec', { gm: '2203009100', colour: 'PNK' });
  add('open-unknown', 'openRec', { gm: 'ZZ0000001', colour: 'BLK', months: ['11.2026'], export: true });
  return cs;
}

const scenarios = [
  { name: 'S1-early-month', today: '2026-10-03' },
  { name: 'S2-params', today: '2026-10-14',
    params: { T1: 60, T2: 0, strBuffer: 0, mix: 0, D: 0, prevMonthPendingCutoffDay: 15, wof: 0, peer: 3, seasonCurrent: ['SS26'], seasonFallback: ['AW26'] } },
  { name: 'S3-year-end', today: '2026-12-02', params: { seasonCurrent: ['AW26'], seasonFallback: ['SS26'], strBuffer: 12.5, T2: 40, D: 45 } },
  { name: 'S4-missing-files', today: '2026-10-14', params: { seasonCurrent: ['AW26'], seasonFallback: ['SS26'], mix: 8, T2: 30 }, drop: ['attrrel', 'artmaster'] },
  { name: 'S5-cutoff-zero', today: '2026-11-01', params: { prevMonthPendingCutoffDay: 0, seasonCurrent: ['AW26', 'SS26'], T1: 120 }, drop: ['bsr'] }
];

const expected = { generatedBy: 'tests/parity/recommendation/run-parity.js', source: 'legacy/python-app/app/index.html (v1.1.4)', scenarios: [] };
let errors = 0;
for (const sc of scenarios) {
  sc.cases = casesFor(sc.today);
  const results = runScenario(sc);
  results.forEach(r => { if (r.result && r.result.__error) { errors++; console.error(sc.name, r.name, r.result.__error); } });
  expected.scenarios.push({ name: sc.name, today: sc.today, params: sc.params || {}, drop: sc.drop || [], results });
}
fs.writeFileSync(path.join(FIX, 'expected.json'), JSON.stringify(expected));
console.log('wrote expected.json:', expected.scenarios.map(s => s.name + ' (' + s.results.length + ' cases)').join(', '), errors ? (' — ' + errors + ' JS errors') : '');
