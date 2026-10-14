#!/usr/bin/env node
// Tool 2 (PO Assessment) parity dataset — extends the recommendation fixture
// (tests/POGovernance.Tests/Recommendation/Fixtures/dataset.json, from
// tests/parity/recommendation/make-fixture.js) with what the batch assessment needs:
//   • Cat Head Mapping rows with Category_Email_ID (B1+B3 → one person, B2 → another, B4 unmapped)
//   • F2 lines: PO Value, vendor names (incl. the swapped Vendor Name / Pending Qty quirk),
//     duplicate POs of one colour × month (FIFO allocation), a NEW article, a GM whose
//     Universe Master row has no MC CD (Cannot Validate), an invalid delivery month,
//     zero-qty / Released-Qty-only rows (filtered out)
//   • B6: a Single Concept bucket with MONTHLY F3 columns + "CY APR & MAY AVG" and an
//     ISO-date F4 header (exercises the non-daily mix path and dateToMMYYYY)
// Deterministic. Writes tests/POGovernance.Tests/Tool2/Fixtures/dataset.json.
'use strict';
const fs = require('fs');
const path = require('path');
const ROOT = path.resolve(__dirname, '../../..');
const src = JSON.parse(fs.readFileSync(path.join(ROOT, 'tests/POGovernance.Tests/Recommendation/Fixtures/dataset.json'), 'utf8'));
const files = JSON.parse(JSON.stringify(src.files));

let seed = 7741;
function rnd() { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; }
function ri(a, b) { return Math.floor(a + rnd() * (b - a + 1)); }

// ── Cat Head Mapping ──
files.catheadmap = [
  { 'Brand Type': 'NON-PL', 'MC CD': '1101', 'MRP': '399', 'Category_Email_ID': 'sukhdev.singh@vishalwholesale.co.in' },
  { 'Brand Type': 'NON-PL', 'MC CD': '1101', 'MRP': '399', 'Category_Email_ID': 'someone.else@vishalwholesale.co.in' }, // duplicate: first wins
  { 'Brand Type': 'NON-PL', 'MC CD': '2203', 'MRP': '599', 'Category Email ID': 'anita_rao2@vishalwholesale.co.in' },
  { 'Brand Type': 'MAVIE', 'MC CD': '3305', 'MRP': '799', 'Category_Email_ID': 'sukhdev.singh@vishalwholesale.co.in' },
  { 'Brand': 'MAVIE', 'MC': '5509', 'MRP': '499', 'Level 3': 'priya.k@vishalwholesale.co.in' },
  { 'Brand Type': 'MAVIE', 'MC CD': '6611', 'MRP': '299', 'Category_Email_ID': 'anita_rao2@vishalwholesale.co.in' },
  { 'Brand Type': 'NON-PL', 'MC CD': '4407', 'MRP': '999', 'Category_Email_ID': '' }
];

// ── F2 enrichment ──
const vendors = ['Shree Textiles', 'Arvind Mills', 'Raymond Ltd', 'Vendor X', 'Kewal Kiran'];
files.f2.forEach((r, i) => {
  r['Vendor Name'] = vendors[i % vendors.length];
  if (i % 3 === 0) r['PO Value'] = String(Math.round(Number(r['Pending Qty']) * (150 + (i % 7) * 20)));
});
// swapped Vendor Name / Pending Qty quirk (number in Vendor Name)
files.f2.push({ 'PO Number': '4500009001', 'Generic Material': '1101000102', 'Color': 'WHT', 'Brand Type': 'NON-PL', 'MC CD': '1101',
  'MRP': '399', 'Flag': 'NON-BOGO', 'Delivery Month': '12.2026', 'Pending Qty': 'Arvind Mills', 'Vendor Name': '1500' });
// duplicate POs of one colour × month (FIFO)
for (const [po, q] of [['4500009002', '2200'], ['4500009003', '900'], ['4500009004', '1400']])
  files.f2.push({ 'PO Number': po, 'Generic Material': '1101000103', 'Color': 'BLK', 'Brand Type': 'NON-PL', 'MC CD': '1101',
    'MRP': '399', 'Flag': 'NON-BOGO', 'Delivery Month': '11.2026', 'Pending Qty': q, 'Vendor Name': 'Raymond Ltd', 'PO Value': String(Number(q) * 210) });
files.f2.push({ 'PO Number': '4500009005', 'Generic Material': '1101000101', 'Color': 'NVY', 'Brand Type': 'NON-PL', 'MC CD': '1101',
  'MRP': '399', 'Flag': 'NON-BOGO', 'Delivery Month': '11.2026', 'Pending Qty': '1,800', 'Vendor Name': 'Raymond Ltd' });
// NEW article (not in UM / BSR / Article Master)
files.f2.push({ 'PO Number': '4500009006', 'Generic Material': '2203009999', 'Color': 'PNK', 'Brand Type': 'NON-PL', 'MC CD': '2203',
  'MRP': '599', 'Division': 'LADIES', 'MC DES': 'L-JEANS', 'Flag': 'BOGO', 'Delivery Month': '12.2026', 'Pending Qty': '640', 'Vendor Name': 'Kewal Kiran' });
// UM GM without MC CD → Cannot Validate
files.um.push({ 'Generic Material': 'C9999X01', 'Brand Type': 'NON-PL', 'Division': 'MENS', 'MC DESC': 'M-UNKNOWN', 'MRP': '199', 'Flag': 'NON-BOGO' });
files.f2.push({ 'PO Number': '', 'Generic Material': 'C9999X01', 'Color': 'BLK', 'Brand Type': 'NON-PL', 'MRP': '199', 'Flag': 'NON-BOGO',
  'Delivery Month': '11.2026', 'Pending Qty': '300', 'Vendor Name': '' });
// invalid delivery month on a Single Concept GM
files.f2.push({ 'PO Number': '4500009008', 'Generic Material': '1101000102', 'Color': 'RED', 'Brand Type': 'NON-PL', 'MC CD': '1101',
  'MRP': '399', 'Flag': 'NON-BOGO', 'Delivery Month': 'Nov-2026', 'Pending Qty': '450', 'Vendor Name': 'Shree Textiles' });
// rows filtered out by runAssessment
files.f2.push({ 'PO Number': '4500009009', 'Generic Material': '1101000102', 'Color': 'RED', 'Brand Type': 'NON-PL', 'MC CD': '1101',
  'MRP': '399', 'Flag': 'NON-BOGO', 'Delivery Month': '12.2026', 'Pending Qty': '0', 'Vendor Name': 'Shree Textiles' });
files.f2.push({ 'PO Number': '4500009010', 'Generic Material': '1101000102', 'Color': 'RED', 'Brand Type': 'NON-PL', 'MC CD': '1101',
  'MRP': '399', 'Flag': 'NON-BOGO', 'Delivery Month': '12.2026', 'Released Qty': '700', 'Vendor Name': 'Shree Textiles' });
// extra Multi Concept lines: BOBs colourways of B2 GMs not in BSR in that colour, True Repeats
for (const [gm, c, dm, q] of [['2203000201', 'PNK', '11.2026', '520'], ['2203000202', 'BLK', '12.2026', '880'], ['2203000203', 'OWH', '01.2027', '1300'],
  ['2203000205', 'BEG', '11.2026', '410'], ['B2203X07', 'GRY', '12.2026', '760'], ['3305000302', 'NVY', '11.2026', '600'], ['5509000501', 'RED', '12.2026', '350'],
  ['4407000402', 'OLV', '11.2026', '275']])
  files.f2.push({ 'PO Number': '45000091' + String(10 + ri(0, 89)), 'Generic Material': gm, 'Color': c, 'Brand Type': gm.startsWith('33') || gm.startsWith('55') ? 'MAVIE' : 'NON-PL',
    'MC CD': gm.startsWith('B') ? '2203' : gm.slice(0, 4), 'MRP': { '2203': '599', '3305': '799', '5509': '499', '4407': '999' }[gm.startsWith('B') ? '2203' : gm.slice(0, 4)],
    'Flag': '', 'Delivery Month': dm, 'Pending Qty': q, 'Vendor Name': 'Vendor X', 'PO Value': String(Number(q) * 333) });

// ── B6: Single Concept with monthly F3 ──
const B6 = { brand: 'MAVIE', mc: '6611', mrp: '299', flag: 'NON-BOGO', div: 'KIDS', mcDesc: 'K-SHORTS' };
const b6gms = ['6611000601', '6611000602'], b6cols = ['BLU', 'YEL', 'RED', 'WHT'];
for (const gm of b6gms) {
  files.um.push({ 'Generic Material': gm, 'Brand Type': B6.brand, 'Division': B6.div, 'MC CD': B6.mc, 'MC DESC': B6.mcDesc, 'MRP': B6.mrp, 'Flag': B6.flag });
  files.artmaster.push({ 'Generic Material': gm, 'Brand Type': B6.brand, 'Division': B6.div, 'MC CD': B6.mc, 'MC DESC': B6.mcDesc, 'MRP': B6.mrp,
    'Flag': B6.flag, 'Season': 'AW26', 'ART DESC': 'SHORTS ' + gm.slice(-2), 'Vendor': 'V6', 'VENDOR NAME': 'Kids Wear Co' });
  for (const c of b6cols) {
    const base = { 'Generic Material': gm, 'Color': c, 'Brand Type': B6.brand, 'MC CD': B6.mc, 'MRP': B6.mrp, 'Flag': B6.flag };
    files.f1.push(Object.assign({}, base, { 'OP STK': String(c === 'YEL' ? ri(20, 60) : ri(800, 5000)) }));
    const f3 = Object.assign({}, base, { '07.2026': String(ri(200, 900)), '08.2026': String(ri(200, 900)), '09.2026': c === 'RED' ? '' : String(ri(200, 900)) });
    if (c === 'WHT') f3['CY APR & MAY AVG'] = String(ri(300, 700));
    if (c === 'YEL') { f3['07.2026'] = '3'; f3['08.2026'] = '2'; f3['09.2026'] = '1'; }
    files.f3.push(f3);
    files.f2.push(Object.assign({}, base, { 'PO Number': '46000' + gm.slice(-2) + c, 'Delivery Month': c === 'BLU' ? '10.2026' : '12.2026',
      'Pending Qty': String(ri(300, 3000)), 'Vendor Name': 'Kids Wear Co', 'PO Value': String(ri(60000, 400000)) }));
  }
}
const f4b6 = { 'Brand Type': B6.brand, 'MC CD': B6.mc, 'MRP': B6.mrp, 'Flag': B6.flag };
for (const m of ['10.2026', '11.2026', '12.2026', '01.2027', '02.2027']) f4b6[m] = String(ri(3000, 9000));
f4b6['2027-03-01'] = String(ri(3000, 9000));
files.f4.push(f4b6);

const out = { files, requests: src.requests };
const dest = path.join(ROOT, 'tests/POGovernance.Tests/Tool2/Fixtures/dataset.json');
fs.writeFileSync(dest, JSON.stringify(out, null, 1));
console.log('wrote', dest, Object.fromEntries(Object.entries(files).map(([k, v]) => [k, v.length])));
