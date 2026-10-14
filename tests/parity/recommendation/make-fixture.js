#!/usr/bin/env node
// Generates the synthetic dataset used by the recommendation parity tests:
//   tests/POGovernance.Tests/Recommendation/Fixtures/dataset.json
// Deterministic (seeded PRNG) — re-running produces the same file.
//
// Buckets (Brand × MC × MRP × Flag):
//   B1 NON-PL 1101 399 NON-BOGO  single concept (Universe Master)        — colour table
//   B2 NON-PL 2203 599 BOGO      multi concept, Assessment Dimension Fit — attribute → colour → GM
//   B3 MAVIE  3305 799 NON-BOGO  multi concept, "Colour-only"            — re-run as single concept
//   B4 NON-PL 4407 999 ''        multi concept, no Attribute Relevance row → error
//   B5 MAVIE  5509 499 BOGO      multi concept, Style without Article Master values → error
'use strict';
const fs = require('fs');
const path = require('path');

let seed = 20261014;
function rnd() { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; }
function ri(a, b) { return Math.floor(a + rnd() * (b - a + 1)); }
function pick(arr) { return arr[ri(0, arr.length - 1)]; }
const fmtComma = n => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ',');

const buckets = [
  { id: 'B1', brand: 'NON-PL', mc: '1101', mrp: '399', flag: 'NON-BOGO', div: 'MENS', mcDesc: 'M-TSHIRT RN', single: true,
    gms: ['1101000101', '1101000102', '1101000103', 'A1101X04'], colours: ['BLK', 'WHT', 'NVY', 'RED', 'OWH', '101', '07', 'GRY'], season: 'AW26' },
  { id: 'B2', brand: 'NON-PL', mc: '2203', mrp: '599', flag: 'BOGO', div: 'LADIES', mcDesc: 'L-JEANS', single: false,
    gms: ['2203000201', '2203000202', '2203000203', '2203000204', '2203000205', '2203000206', 'B2203X07', 'B2203X08'],
    colours: ['BLK', 'OWH', 'BLU', 'GRY', '204', 'BEG'], fits: ['Regular', 'Slim', 'Relaxed'], season: 'AW26' },
  { id: 'B3', brand: 'MAVIE', mc: '3305', mrp: '799', flag: 'NON-BOGO', div: 'KIDS', mcDesc: 'K-TOP', single: false,
    gms: ['3305000301', '3305000302', '3305000303'], colours: ['PNK', 'YEL', 'WHT', 'NVY'], season: 'SS 26' },
  { id: 'B4', brand: 'NON-PL', mc: '4407', mrp: '999', flag: '', div: 'MENS', mcDesc: 'M-JACKET', single: false,
    gms: ['4407000401', '4407000402'], colours: ['BLK', 'OLV', 'TAN'], season: 'AW-26' },
  { id: 'B5', brand: 'MAVIE', mc: '5509', mrp: '499', flag: 'BOGO', div: 'LADIES', mcDesc: 'L-KURTA', single: false,
    gms: ['5509000501', '5509000502'], colours: ['RED', 'MRN'], season: 'AW_25' }
];

const f1 = [], f2 = [], f3 = [], f4 = [], um = [], bsr = [], artmaster = [], attrrel = [], colmaster = [], catheadmap = [];
const months = ['08.2026', '09.2026', '10.2026', '11.2026', '12.2026', '01.2027', '02.2027', '03.2027', '04.2027', '05.2027', '06.2027'];
const poMonths = ['08.2026', '09.2026', '10.2026', '11.2026', '12.2026', '01.2027', '02.2027'];
let poNo = 4500001000;

for (const b of buckets) {
  // a few rows use "599.0"-style MRP / blank flag to exercise _n() and the blank-flag wildcard
  const mrpVar = () => (rnd() < 0.15 ? b.mrp + '.0' : b.mrp);
  const flagVar = () => (rnd() < 0.1 ? '' : b.flag);
  const fitOf = {};
  b.gms.forEach((gm, gi) => {
    if (b.fits) fitOf[gm] = gi === b.gms.length - 1 ? '' : b.fits[gi % b.fits.length]; // last GM: no Fit → skipped
    // Article Master
    const am = { 'Generic Material': gm, 'Brand Type': b.brand, 'Division': b.div, 'MC CD': b.mc, 'MC DESC': b.mcDesc,
      'MRP': b.mrp, 'Flag': b.flag, 'Season': gi % 3 === 2 ? 'SS-26' : b.season, 'ART DESC': b.mcDesc + ' ART ' + (gi + 1),
      'Vendor': 'V' + (100200 + gi), 'VENDOR NAME': 'Vendor ' + String.fromCharCode(65 + gi) + ' Pvt Ltd' };
    if (b.fits) am['Fit'] = fitOf[gm];
    if (b.id === 'B2') am['Style'] = gi < 3 ? 'Straight' : 'Bootcut';
    if (b.id === 'B5') am['Style'] = '';
    artmaster.push(am);
    if (b.single) um.push({ 'Generic Material': gm, 'Brand Type': b.brand, 'Division': b.div, 'MC CD': b.mc, 'MC DESC': b.mcDesc, 'MRP': b.mrp, 'Flag': b.flag });
  });

  // F1 / F3 per GM × colour (not every combination)
  for (const gm of b.gms) {
    for (const c of b.colours) {
      if (rnd() < 0.35) continue;
      const ci = b.colours.indexOf(c);
      const stock = c === 'GRY' && b.id === 'B1' ? ri(400, 900) : (ci % 3 === 0 ? ri(6000, 16000) : ri(50, 3200));
      const daily = c === 'GRY' && b.id === 'B1' ? 0.2 : ri(0, 30);
      const base = { 'Generic Material': gm, 'Color': c, 'Brand Type': b.brand, 'MC CD': b.mc, 'MRP': mrpVar(), 'Flag': flagVar() };
      if (rnd() < 0.9) f1.push(Object.assign({}, base, { 'OP STK': rnd() < 0.2 ? fmtComma(stock) : String(stock) }));
      const r3 = Object.assign({}, base);
      for (let d = 1; d <= 45; d++) r3['Day ' + d] = String(Math.max(0, Math.round(daily + (rnd() - 0.5) * daily)));
      if (rnd() < 0.92) f3.push(r3);
      // F2 pending POs
      const nPO = ri(0, 2);
      for (let k = 0; k < nPO; k++) {
        f2.push({ 'PO Number': String(poNo++), 'Generic Material': gm, 'Color': c, 'Brand Type': b.brand, 'MC CD': b.mc,
          'MRP': mrpVar(), 'Flag': b.flag, 'Delivery Month': pick(poMonths), 'Pending Qty': String(ri(100, 2500)), 'Vendor Name': 'Vendor X' });
      }
      // BSR (multi concept buckets)
      if (!b.single) {
        const pd = (0.004 + rnd() * 0.05);
        const pdCell = rnd() < 0.3 ? (pd * 100).toFixed(2) + '%' : pd.toFixed(4);
        const row = { 'Generic Material': gm, 'Color': c, 'Brand Type': b.brand, 'MC CD': b.mc, 'MRP': b.mrp,
          'Article Type': rnd() < 0.15 ? '' : b.flag, 'AVG PERDAY SELL THRU%': pdCell };
        row[rnd() < 0.3 ? 'WEEK\n ON\nFLOOR' : 'WEEK ON FLOOR'] = String(ri(2, 14));
        bsr.push(row);
      }
    }
  }
  // F2 junk row (no delivery month) and one without matching flag
  f2.push({ 'PO Number': String(poNo++), 'Generic Material': b.gms[0], 'Color': b.colours[0], 'Brand Type': b.brand, 'MC CD': b.mc,
    'MRP': b.mrp, 'Flag': b.flag, 'Delivery Month': '', 'Pending Qty': '999', 'Vendor Name': 'Vendor X' });
  // F4 forecast rows (two rows for B1 to exercise summing; values with commas)
  const nF4 = b.id === 'B1' ? 2 : 1;
  for (let k = 0; k < nF4; k++) {
    const r = { 'Brand Type': b.brand, 'MC CD': b.mc, 'MRP': b.mrp, 'Flag': b.flag };
    for (const m of months) r[m] = rnd() < 0.08 ? '' : fmtComma(ri(4000, 26000));
    if (b.id === 'B2') r[' 07.2027 '] = '5,000'; // padded header: counted by _buildF4Map (trimmed), not by _buildMonths
    f4.push(r);
  }
  // Attribute Relevance Master
  if (b.id === 'B2') attrrel.push({ 'Brand Type': b.brand, 'MC CD': b.mc, 'MRP': b.mrp, 'Flag': b.flag, 'Assessment Dimension': 'Fit' });
  if (b.id === 'B3') attrrel.push({ 'Brand Type': b.brand, 'MC CD': b.mc, 'MRP': b.mrp, 'Flag': b.flag, 'Assessment\nDimension': 'Colour-only' });
  if (b.id === 'B5') attrrel.push({ 'Brand Type': b.brand, 'MC CD': b.mc, 'MRP': b.mrp, 'Flag': b.flag, 'Assessment Dimension': 'Style' });
  catheadmap.push({ 'Brand Type': b.brand, 'MC CD': b.mc, 'MRP': b.mrp, 'Level 1 Buyer': 'buyer@x', 'Category Head': 'ch@x' });
}

// BSR peers: other GMs in B2's MC×MRP (seasons via Article Master), some below the WOF gate
for (let i = 0; i < 14; i++) {
  const gm = '2203009' + String(100 + i);
  artmaster.push({ 'Generic Material': gm, 'Brand Type': 'NON-PL', 'Division': 'LADIES', 'MC CD': '2203', 'MC DESC': 'L-JEANS',
    'MRP': '599', 'Flag': 'BOGO', 'Season': i % 2 ? 'AW 26' : 'SS_26', 'ART DESC': 'PEER ' + i, 'Vendor': 'V9' + i, 'VENDOR NAME': 'Peer Vendor' });
  bsr.push({ 'Generic Material': gm, 'Color': pick(['BLK', 'OWH', 'BLU']), 'Brand Type': 'NON-PL', 'MC CD': '2203', 'MRP': '599',
    'Article Type': i % 5 === 0 ? 'NON-BOGO' : 'BOGO', 'WEEK ON FLOOR': String(ri(2, 12)),
    'AVG PERDAY SELL THRU%': (0.004 + rnd() * 0.05).toFixed(4) });
}
for (const c of ['BLK', 'WHT', 'NVY', 'RED', 'OWH', 'BLU', 'GRY', 'BEG', 'PNK', 'YEL', 'OLV', 'TAN', 'MRN', '101', '07', '204'])
  colmaster.push({ 'Colour': c, 'Colour Desc': c + ' DESC' });

// Same-day requests (v1.1.4 request objects). Dates cover every scenario's today.
const reqs = [];
const info = (b) => ({ brand: b.brand, mcCd: b.mc, mrp: b.mrp, flag: b.flag, div: b.div, mcDesc: b.mcDesc });
const B1 = buckets[0], B2 = buckets[1];
for (const date of ['2026-10-03', '2026-10-14', '2026-12-02', '2026-09-30']) {
  reqs.push({ gm: B1.gms[0], colour: 'BLK', month: date.startsWith('2026-12') ? '12.2026' : '10.2026', qty: 500, engine: 'B', decision: 'Auto Allow', info: info(B1), reqDate: date });
  reqs.push({ gm: B1.gms[1], colour: 'wht', month: '11.2026', qty: 800, engine: 'B', decision: "Hold — Don't Raise", finalApprovedQty: 300, info: info(B1), reqDate: date });
  reqs.push({ gm: B1.gms[2], colour: 'NVY', month: '12.2026', qty: 400, engine: 'B', decision: 'Auto Allow', finalApprovedQty: 0, info: info(B1), reqDate: date });
  reqs.push({ gm: B1.gms[0], colour: 'RED', month: '01.2027', qty: 250, engine: 'B', decision: 'Auto Allow', info: Object.assign(info(B1), { mcCd: '1101.0', mrp: 399 }), reqDate: date });
  reqs.push({ gm: B1.gms[0], colour: 'GRY', month: '10.2026', qty: 999, engine: 'B', decision: 'Watch List', info: info(B1), reqDate: date });
  reqs.push({ gm: B2.gms[0], colour: 'BLK', month: '11.2026', qty: 700, engine: 'A', decision: 'Allow', info: info(B2), reqDate: date });
  // engine-B ledger rows against the multi-concept bucket (exercise the attribute-scoped merge)
  reqs.push({ gm: B2.gms[1], colour: 'OWH', month: '11.2026', qty: 600, engine: 'B', decision: 'Auto Allow', info: info(B2), reqDate: date });
  reqs.push({ gm: B2.gms[2], colour: 'BLU', month: '12.2026', qty: 450, engine: 'B', decision: 'Auto Allow', finalApprovedQty: 450, info: info(B2), reqDate: date });
}

const out = {
  files: { f1, f2, f3, f4, um, bsr, artmaster, colmaster, attrrel, catheadmap },
  requests: reqs
};
const dest = path.resolve(__dirname, '../../POGovernance.Tests/Recommendation/Fixtures/dataset.json');
fs.writeFileSync(dest, JSON.stringify(out, null, 1));
console.log('wrote', dest, Object.fromEntries(Object.entries(out.files).map(([k, v]) => [k, v.length])), 'requests', reqs.length);
