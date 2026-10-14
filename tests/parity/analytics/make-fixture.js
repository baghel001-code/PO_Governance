#!/usr/bin/env node
// Builds tests/parity/analytics/fixture.json for the analytics parity tests:
//   files      — the recommendation fixture dataset (tests/parity/recommendation/make-fixture.js output),
//                augmented for Attribute Analysis (MC DESC on F1/F3, blank brand / flag / MC rows, an
//                extra Article Master attribute column, a padded header that never resolves)
//   requests   — a request history (Tool 1) covering every decision, engine, approval state,
//                cleared requests and every classifyReasonBucket driver
//   tool2Rows  — Tool 2 batchResults rows covering every decision / driver / null-field combination
// Deterministic (seeded PRNG).
'use strict';
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '../../..');
const base = JSON.parse(fs.readFileSync(path.join(ROOT, 'tests/POGovernance.Tests/Recommendation/Fixtures/dataset.json'), 'utf8'));
let seed = 7751;
function rnd() { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; }
function ri(a, b) { return Math.floor(a + rnd() * (b - a + 1)); }
function pick(a) { return a[ri(0, a.length - 1)]; }

const files = JSON.parse(JSON.stringify(base.files));
const mcDesc = { '1101': 'M-TSHIRT RN', '2203': 'L-JEANS', '3305': 'K-TOP', '4407': 'M-JACKET', '5509': 'L-KURTA' };
// MC DESC on most F1 / F3 rows (a few rows without → a second MC node "mc|||")
for (const k of ['f1', 'f3']) files[k].forEach((r, i) => { if (i % 9 !== 4) r['MC DESC'] = mcDesc[r['MC CD']] || ''; });
// rows with no brand (→ NON-PL), BOGO/NON-BOGO column instead of Flag, no MC (skipped), lower-case colour
files.f1.push({ 'Generic Material': '6601000001', 'Color': 'BLK', 'MC CD': '6601', 'MC DESC': 'U-CAP', 'MRP': '199', 'OP STK': '1,250' });
files.f1.push({ 'Generic Material': '6601000002', 'Color': 'blk', 'MC CD': '6601', 'MC DESC': 'U-CAP', 'MRP': '199', 'BOGO/NON-BOGO': 'BOGO', 'OP STK': '300' });
files.f1.push({ 'Generic Material': '7701000001', 'Color': 'RED', 'MC CD': '', 'MRP': '99', 'OP STK': '5000' });
files.f1.push({ 'Generic Material': '6601000003', 'Colour': 'WHT', 'MC CODE': '6601', 'MC DESCRIPTION': 'U-CAP', 'MRP': '249', 'Op Stk': '42' });
const f3row = (gm, col, mc, desc, mrp, extra, daily) => {
  const r = Object.assign({ 'Generic Material': gm, 'Color': col, 'MC CD': mc, 'MC DESC': desc, 'MRP': mrp }, extra);
  for (let d = 1; d <= 45; d++) r['Day ' + d] = String(daily);
  return r;
};
files.f3.push(f3row('6601000001', 'BLK', '6601', 'U-CAP', '199', {}, 3));
files.f3.push(f3row('6601000004', 'NVY', '6601', 'U-CAP', '199', {}, 1));
files.f3.push(f3row('6601000002', 'blk', '6601', 'U-CAP', '199', { 'BOGO/NON-BOGO': 'BOGO' }, 0));
// Article Master: extra attribute column "Pattern" on some rows, padded header, numeric-like header
files.artmaster.forEach((r, i) => {
  if (i % 3 === 0) r['Pattern'] = pick(['Solid', 'Stripe', 'Check']);
  if (i === 2) r[' Sleeve '] = 'Half';
  if (i === 5) r['2024'] = 'Y';
});
files.artmaster.push({ 'Generic Material': '6601000001', 'Brand Type': '', 'MC CD': '6601', 'MRP': '199', 'Pattern': 'Solid', 'Fit': 'Free' });

// ── Tool 1 request history ────────────────────────────────────────────────
const reasons = {
  absolute: 'Absolute Block override: GM has AVG PERDAY SELL THRU% = 0.6% < 1% threshold with 6w on floor (≥ 4w gate).',
  mix: 'Colour Mix % = 1.2% < 2% threshold. Broken/cut size likely. STR not computed.',
  zero: 'STR = 140 days > 79d (T=75d + 5% buffer). Max Permissible Qty = 0 units — stock already covers.',
  strEx: 'Colour STR exceeds T=75d. Reduce.',
  bobs: 'BOBs fallthrough: no valid peer pool after IQR clean. Auto Allow.',
  repeat: 'True Repeat: PDSTR 0.8% is 35% below peer median.',
  wof: 'WOF = 2w < 4w gate. Insufficient selling history — Auto Allow.',
  nopeer: 'No peer pool available. Auto Allow.',
  newArt: 'Not found in Universe Master or CY BSR. Treated as new article.',
  safe: 'Colour STR = 40 days ≤ T=75d + 5% buffer (79d). Safe to raise PO at full requested qty of 500 units.',
  other: 'Invalid delivery month.'
};
const decisions = ['Auto Allow', 'Allow', 'Watch List', 'Recommend Reduced Qty', 'Block', "Hold — Don't Raise", 'Cannot Validate'];
const brands = [['NON-PL', '1101', '399', 'NON-BOGO', 'MENS', 'M-TSHIRT RN'], ['NON-PL', '2203', '599', 'BOGO', 'LADIES', 'L-JEANS'],
  ['MAVIE', '3305', '799', 'NON-BOGO', 'KIDS', 'K-TOP'], ['MAVIE', '5509', '499', 'BOGO', 'LADIES', 'L-KURTA'], ['ZED', '8801', '1299', '', '', '']];
const months = ['10.2026', '11.2026', '12.2026', '01.2027'];
const requests = [];
let seq = 0;
for (let i = 0; i < 70; i++) {
  const b = brands[i % brands.length];
  const dec = decisions[(i * 3 + Math.floor(i / 7)) % decisions.length];
  const eng = i % 11 === 0 ? 'NEW' : (i % 3 === 0 ? 'A' : 'B');
  const rk = Object.keys(reasons)[(i * 5) % Object.keys(reasons).length];
  const r = { id: i + 1, seq: ++seq, reqId: 'REQ-20261014-' + String(seq).padStart(4, '0'), gm: (b[1] + '0000' + (i % 6)), colour: pick(['BLK', 'WHT', 'NVY', 'RED', '101']),
    month: months[i % 4], qty: ri(1, 40) * 50, engine: eng, decision: dec, decisionReason: reasons[rk], reqDate: '2026-10-14',
    requestedBy: pick(['Asha', 'Ravi', 'Meena']), requestedByEmail: pick(['asha@x', 'ravi@x', 'meena@x']),
    optionType: eng === 'B' ? 'Single Concept' : 'Repeat', colourKey: 'k' + i };
  if (i % 13 !== 7) r.info = { brand: b[0], mcCd: b[1], mrp: b[2], flag: b[3], div: b[4], mcDesc: b[5] };
  if (i % 17 === 3) r.info = { mcCd: b[1] }; // info without brand / div / mcDesc
  if (i % 19 === 5) delete r.month;
  if (i % 23 === 9) delete r.decisionReason;
  if (i % 29 === 11) r.qty = 0;
  if (eng === 'B' && i % 4 === 1) r.finalApprovedQty = Math.round(r.qty / 2);
  if (dec === 'Watch List') {
    const st = ['pending', 'approved', 'rejected', 'cleared'][i % 4];
    r.approval = { status: st, approvers: ['ch@x'], approverNames: ['Cat Head'] };
    if (st === 'approved') { r.approval.qty = Math.round(r.qty * 0.8); r.approval.by = 'ch@x'; r.approval.byName = 'Cat Head'; r.finalApprovedQty = r.approval.qty; }
    if (st === 'cleared') r.cleared = true;
  }
  requests.push(r);
}
// repeat offender (same gm·colour flagged twice) and a numeric-looking MC desc
requests.push(Object.assign({}, requests[2], { id: 71, seq: ++seq, reqId: 'REQ-20261014-0071', decision: 'Block', decisionReason: reasons.absolute }));
requests.push({ id: 72, seq: ++seq, gm: '9900000001', colour: 'BLK', month: '11.2026', qty: 120, engine: 'A', decision: 'Watch List', decisionReason: reasons.repeat,
  info: { brand: 'NON-PL', mcCd: '9900', mrp: '299', flag: 'BOGO', div: 'MENS', mcDesc: '204' }, reqDate: '2026-10-14' });

// ── Tool 2 batchResults ────────────────────────────────────────────────────
const t2dec = ['Retain', 'Watch List', 'Reduce Qty', 'Cancel', 'Cannot Validate'];
const t2reasons = [reasons.absolute, reasons.mix, 'Max Permissible Qty = 0 units', 'STR = 150 days exceeds T=100d', reasons.bobs, reasons.repeat,
  reasons.wof, reasons.nopeer, 'GM not found in Universe Master', 'Colour STR = 40 days ≤ T=100d. PO within cover limit.'];
const tool2Rows = [];
for (let i = 0; i < 90; i++) {
  const b = brands[i % 4];
  const dec = t2dec[(i * 7 + Math.floor(i / 5)) % t2dec.length];
  const relQty = ri(2, 60) * 25;
  const r = { idx: i + 1, gm: b[1] + '0000' + (i % 7), colour: pick(['BLK', 'WHT', 'NVY', 'RED', '101', 'OWH']), dm: months[i % 4], relQty,
    poNum: String(4500002000 + i), engine: i % 3 ? 'B' : 'A', decision: dec, decisionReason: t2reasons[(i * 3) % t2reasons.length],
    artSTR: i % 3 ? ri(10, 200) : null, pdstr: i % 3 ? null : (i % 2 ? Number((rnd() * 2).toFixed(2)) : null), quartile_: i % 3 ? null : pick(['Q1', 'Q2', 'Q3', 'Q4', null]),
    recQty: ri(0, relQty), excessQty: dec === 'Retain' ? 0 : ri(0, relQty), mixPct: 0.05, opStock: ri(0, 9000),
    div: b[4], mcDesc: b[5], mrp: i % 10 === 4 ? Number(b[2]) : b[2], vendor: pick(['V100200', 'V100201', 'V100202', 'V100203', 'V100204', 'V100205', 'V100206']),
    info: { brand: b[0], mcCd: b[1], mrp: b[2], flag: b[3], div: b[4], mcDesc: b[5] }, poRate: i % 4 === 0 ? null : ri(100, 600), poValue: null };
  if (i % 11 === 6) r.excessQty = null;
  if (i % 13 === 2) { r.mcDesc = ''; r.div = undefined; }
  if (i % 17 === 8) { r.info = null; r.mrp = '—'; r.vendor = ''; }
  if (i % 19 === 1) r.dm = '';
  tool2Rows.push(r);
}
tool2Rows.push({ idx: 91, gm: 'ERR1', colour: 'BLK', dm: '11.2026', relQty: 500, poNum: '4500009999', engine: '?', decision: 'Cannot Validate', decisionReason: 'Error: boom',
  artSTR: null, recQty: 0, excessQty: 0, mixPct: 0, opStock: 0, div: '—', mcDesc: '—', mrp: '—', vendor: '—', info: null, poValue: null, poRate: null });

const out = { files, requests, tool2Rows };
fs.writeFileSync(path.join(__dirname, 'fixture.json'), JSON.stringify(out));
console.log('wrote fixture.json', Object.fromEntries(Object.entries(files).map(([k, v]) => [k, v.length])), 'requests', requests.length, 'tool2Rows', tool2Rows.length);
