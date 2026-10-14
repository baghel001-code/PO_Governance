#!/usr/bin/env node
// Builds the Tool 1 parity dataset: the recommendation parity dataset
// (tests/POGovernance.Tests/Recommendation/Fixtures/dataset.json, from
// tests/parity/recommendation/make-fixture.js) plus rows that exercise Tool 1-only paths:
//   1101NOMC   Universe Master GM without MC CD             → Cannot Validate
//   8800000001 Universe Master GM, bucket without F4        → Auto Allow "No forecast data" (debug reason)
//   6600000001 single concept, legacy monthly F3 + CY AVG, F4 with date-string / ISO headers
//   6600000002 single concept, F4 with Excel-serial headers only (numeric fallback)
//   7700000001 Article Master only (not in UM / BSR)        → New article with resolved info
//   B1 NVY     a very large pending PO                       → Hold, Max Permissible 0
// Output: tests/POGovernance.Tests/Tool1/Fixtures/dataset.json (deterministic).
'use strict';
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '../../..');
const src = JSON.parse(fs.readFileSync(path.join(ROOT, 'tests/POGovernance.Tests/Recommendation/Fixtures/dataset.json'), 'utf8'));
const d = JSON.parse(JSON.stringify(src));
const F = d.files;

F.um.push({ 'Generic Material': '1101NOMC', 'Brand Type': 'NON-PL', 'Division': 'MENS', 'MC CD': '', 'MC DESC': '', 'MRP': '399', 'Flag': 'NON-BOGO' });
F.um.push({ 'Generic Material': '8800000001', 'Brand Type': 'NON-PL', 'Division': 'MENS', 'MC CD': '8800', 'MC DESC': 'M-SOCKS', 'MRP': '299', 'Flag': 'NON-BOGO' });
F.f1.push({ 'Generic Material': '8800000001', 'Color': 'BLK', 'Brand Type': 'NON-PL', 'MC CD': '8800', 'MRP': '299', 'Flag': 'NON-BOGO', 'OP STK': '1,250' });

// B6: legacy F3 (monthly columns, one row with a pre-computed CY AVG), F4 with date-string headers
const b6 = { 'Brand Type': 'MAVIE', 'MC CD': '6600', 'MRP': 249, 'Flag': 'NON-BOGO' };
for (const gm of ['6600000001', '6600000002']) {
  F.um.push(Object.assign({ 'Generic Material': gm, 'Division': 'KIDS', 'MC DESC': 'K-SHORTS' }, b6));
  F.artmaster.push(Object.assign({ 'Generic Material': gm, 'Division': 'KIDS', 'MC DESC': 'K-SHORTS', 'Season': 'AW26', 'ART DESC': 'SHORTS ' + gm.slice(-1), 'VENDOR NAME': 'Kids Co' }, b6));
}
const b6cols = { RED: [900, 1100, 1300], BLU: [300, 280, 310], YEL: [20, 15, 10] };
for (const [c, v] of Object.entries(b6cols)) {
  F.f1.push(Object.assign({ 'Generic Material': '6600000001', 'Color': c, 'OP STK': String(v[0] * 3) }, b6));
  const r = Object.assign({ 'Generic Material': '6600000001', 'Color': c }, b6, { '07.2026': String(v[0]), '08.2026': String(v[1]), '09.2026': String(v[2]) });
  F.f3.push(r);
  F.f2.push(Object.assign({ 'PO Number': '4500009' + c.length, 'Generic Material': '6600000001', 'Color': c, 'Delivery Month': '11.2026', 'Pending Qty': String(v[1]) }, b6));
}
F.f3.push(Object.assign({ 'Generic Material': '6600000002', 'Color': 'GRN', 'CY APR & MAY AVG': '150' }, b6));
F.f1.push(Object.assign({ 'Generic Material': '6600000002', 'Color': 'GRN', 'OP STK': '400' }, b6));
F.f4.push(Object.assign({}, b6, { '10/1/2026': '4,000', '2026-11-01': '4,400', '12/1/2026': '5,000', '2027-01-01': '3,900', '2/1/2027': '3,500', '2027-03-01': '3,200', 'Total': 45000 }));
// B7: F4 with Excel-serial headers only (numeric fallback)
const b7 = { 'Brand Type': 'MAVIE', 'MC CD': '6601', 'MRP': '249', 'Flag': 'BOGO' };
F.um.push(Object.assign({ 'Generic Material': '6601000001', 'Division': 'KIDS', 'MC DESC': 'K-TEE' }, b7));
F.f1.push(Object.assign({ 'Generic Material': '6601000001', 'Color': 'WHT', 'OP STK': '2000' }, b7));
const f3b7 = Object.assign({ 'Generic Material': '6601000001', 'Color': 'WHT' }, b7);
const f3b7o = Object.assign({ 'Generic Material': '6601000001', 'Color': 'PNK' }, b7);
for (let i = 1; i <= 30; i++) { f3b7['D' + i] = String(10 + (i % 4)); f3b7o['DAY ' + i] = String(25); }
F.f3.push(f3b7, f3b7o);
F.f4.push(Object.assign({}, b7, { '46296': '6,000', '46327': '6,500', '46357': '7,000', '46388': '5,500', '46419': '5,000', '46447': '4,800' }));

// Article Master only
F.artmaster.push({ 'Generic Material': '7700000001', 'Brand Type': 'NON-PL', 'Division': 'LADIES', 'MC CD': '2203', 'MC DESC': 'L-JEANS', 'MRP': '599', 'Flag': 'BOGO', 'Season': 'AW26', 'ART DESC': 'NEW JEANS', 'VENDOR NAME': 'New Vendor' });
// B1 NVY: huge pending PO → no headroom
F.f2.push({ 'PO Number': '4500099999', 'Generic Material': '1101000103', 'Color': 'NVY', 'Brand Type': 'NON-PL', 'MC CD': '1101', 'MRP': '399', 'Flag': 'NON-BOGO', 'Delivery Month': '11.2026', 'Pending Qty': '250000', 'Vendor Name': 'Vendor X' });

const dest = path.join(ROOT, 'tests/POGovernance.Tests/Tool1/Fixtures/dataset.json');
fs.writeFileSync(dest, JSON.stringify(d, null, 1));
console.log('wrote', dest, Object.fromEntries(Object.entries(F).map(([k, v]) => [k, v.length])), 'requests', d.requests.length);
