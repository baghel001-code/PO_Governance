'use strict';
// Runs the ORIGINAL v1.1.4 browser code (vendor SheetJS + functions extracted from index.html) on the
// fixtures and writes the expected results the xUnit parity tests compare against:
//   Expected/sheets.json     XLSX.read + sheet_to_json(header:1, defval:'', raw:false) of every fixture
//   Expected/scenarios.json  per upload scenario: datasets pushed (rows / meta / X-Label), status texts,
//                            toasts, alerts, report lines, GRC period picker
//   Expected/ssf.json        SSF.format(fmt, value) for a grid of number formats and values
// Usage (from the repo root): TZ=Asia/Kolkata node tests/parity/ingest/run-parity.js
process.env.TZ = process.env.TZ || 'Asia/Kolkata';
const fs = require('fs');
const path = require('path');
const { createContext, fileObj, ROOT } = require('./legacy');

const TESTS = path.join(ROOT, 'tests', 'POGovernance.Tests', 'Ingest');
const FIX = path.join(TESTS, 'Fixtures');
const OUT = path.join(TESTS, 'Expected');
fs.mkdirSync(OUT, { recursive: true });
const sleep = ms => new Promise(r => setTimeout(r, ms));
const clone = o => JSON.parse(JSON.stringify(o));

function htmlLines(html) {
  if (!html) return [];
  const parts = html.split('<br>').map(s => s.replace(/<[^>]*>/g, ''));
  if (parts.length && parts[parts.length - 1] === '') parts.pop();
  return parts;
}

async function sheets() {
  const { ctx } = createContext();
  const res = {};
  for (const f of fs.readdirSync(FIX).sort()) {
    if (f.endsWith('.zip')) continue;
    try {
      const buf = new Uint8Array(fs.readFileSync(path.join(FIX, f)));
      const wb = ctx.XLSX.read(buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength), { type: 'array', cellDates: false });
      const out = { sheetNames: wb.SheetNames.slice(), sheets: {} };
      for (const sn of wb.SheetNames) out.sheets[sn] = ctx.XLSX.utils.sheet_to_json(wb.Sheets[sn], { header: 1, defval: '', raw: false });
      res[f] = clone(out);
    } catch (e) { res[f] = { error: String(e.message) }; }
  }
  return res;
}

async function scenario(sc) {
  const { ctx, elements, log } = createContext();
  const files = names => names.map(n => fileObj(ctx, n, fs.readFileSync(path.join(FIX, n))));
  const ev = names => ({ target: { files: files(names) } });
  if (sc.before) {
    ctx.loadFile(ev(sc.before), sc.key);
    await sleep(100);
    log.toasts.length = 0; log.dirty.length = 0;
  }
  let stId, reportId = null;
  switch (sc.kind) {
    case 'file': ctx.loadFile(ev(sc.files), sc.key); await sleep(150); stId = 'st-' + sc.key; break;
    case 'zip': await ctx.loadZipBundle(ev(sc.files)); stId = 'st-zipbundle'; reportId = 'zip-mapping-report'; break;
    case 'raw': await ctx.handleRawImport(ev(sc.files)); stId = 'st-rawimport'; reportId = 'raw-import-report'; break;
    case 'grc': await ctx.loadGRCFile(ev(sc.files)); stId = 'st-grc'; break;
  }
  const S = ctx.S;
  const keys = log.dirty.filter(k => S.files[k] && S.files[k].length).map(k => {
    const payload = { rows: S.files[k] };
    if (k === 'grc') payload.meta = { grcPeriodType: S.files.grcPeriodType || null };
    const st = elements['st-' + k];
    return { key: k, label: (st ? st.textContent : '').slice(0, 200), payload: clone(payload) };
  });
  const slotStatus = {};
  for (const [id, el] of Object.entries(elements)) {
    if (!id.startsWith('st-') || ['st-zipbundle', 'st-rawimport'].includes(id)) continue;
    slotStatus[id.slice(3)] = el.textContent;
  }
  const st = elements[stId];
  return {
    keys,
    emptyKeys: log.dirty.filter(k => !(S.files[k] && S.files[k].length)),
    status: st ? st.textContent : null,
    statusIsError: st ? / er$/.test(st.className) : false,
    slotStatus,
    toasts: log.toasts,
    alerts: log.alerts,
    lines: reportId ? htmlLines(elements[reportId] && elements[reportId].innerHTML) : [],
    picker: log.picker || null
  };
}

function ssf(ctx) {
  const fmts = ['General', '0', '0.00', '#,##0', '#,##0.00', '0%', '0.00%', '0.00E+00', '# ?/?', '# ??/??', 'm/d/yy', 'd-mmm-yy',
    'd-mmm', 'mmm-yy', 'h:mm AM/PM', 'h:mm:ss AM/PM', 'h:mm', 'h:mm:ss', 'm/d/yy h:mm', '#,##0 ;(#,##0)', '#,##0 ;[Red](#,##0)',
    '#,##0.00;(#,##0.00)', '#,##0.00;[Red](#,##0.00)', 'mm:ss', '[h]:mm:ss', 'mmss.0', '##0.0E+0', '@',
    'dd-mm-yyyy', 'dd.mm.yyyy', 'dd/mm/yyyy', 'yyyy-mm-dd', 'mmmm yyyy', 'dddd, mmmm d, yyyy', 'mm.yyyy', 'dd-mmm-yyyy hh:mm:ss',
    '0.0', '0.000', '#,##0.000', '0.0%', '#,##0;-#,##0', '#,##0;[Red]-#,##0', '0;-0;;@', '"₹"#,##0.00', '[$₹-4009] #,##0.00',
    '_(* #,##0_);_(* \\(#,##0\\);_(* "-"_);_(@_)', '_(* #,##0.00_);_(* \\(#,##0.00\\);_(* "-"??_);_(@_)', '[>=100000]#,##0;0.00',
    '#,##,##0', '##,##,##0.00', '000000', '00000.00', '#', '#.##', '0.##', '#,###', '"Qty: "0', '0 "pcs"', '@" pcs"', '[h]:mm',
    '#,##0,', '#,##0,,"M"', '0.00E+0', '#?/?', '0 ?/?', 'yyyy', 'mmm', 'hh:mm:ss.000', 'General;-General', '[Red]0.00', '\\-0'];
  const vals = [0, 1, -1, 0.5, 0.1 + 0.2, 12.345, -12.345, 1234.5, -1234.5, 1234567.891, 46174, 46174.5, 45000.123456, 60, 0.000123456,
    1e-7, 99999999999, 123456789012, 1234567890123, 4000012345, 2147483648, -2147483649, 0.999999999, 1e21, 33.3333333333, 7, 100000, 0.75];
  const out = [];
  for (const f of fmts) for (const v of vals.concat(['text', '', true, false])) {
    let r; try { r = ctx.XLSX.SSF.format(f, v); } catch (e) { r = null; }
    out.push([f, v, r]);
  }
  for (let i = 0; i < 400; i++) {
    const v = (Math.sin(i * 12.9898) * 43758.5453 % 1) * Math.pow(10, (i % 30) - 12);
    let r; try { r = ctx.XLSX.SSF.format('General', v); } catch (e) { r = null; }
    out.push(['General', v, r]);
  }
  return out;
}

(async () => {
  fs.writeFileSync(path.join(OUT, 'sheets.json'), JSON.stringify(await sheets(), null, 1));
  const scenarios = JSON.parse(fs.readFileSync(path.join(__dirname, 'scenarios.json'), 'utf8'));
  const res = {};
  for (const sc of scenarios) res[sc.name] = await scenario(sc);
  fs.writeFileSync(path.join(OUT, 'scenarios.json'), JSON.stringify(res, null, 1));
  fs.writeFileSync(path.join(OUT, 'ssf.json'), JSON.stringify(ssf(createContext().ctx)));
  fs.copyFileSync(path.join(__dirname, 'scenarios.json'), path.join(OUT, 'scenario-list.json'));
  console.log('expected results written to', OUT);
})().catch(e => { console.error(e); process.exit(1); });
