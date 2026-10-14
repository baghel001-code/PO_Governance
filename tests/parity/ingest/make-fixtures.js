'use strict';
// Writes the SheetJS-made fixtures (.xlsb, .xls, .csv) and the zip bundle into
// tests/POGovernance.Tests/Ingest/Fixtures. The .xlsx fixtures come from the ClosedXML generator
// (POGOV_REGEN_FIXTURES=1 dotnet test --filter FixtureGenerator) — run that first.
// Usage: node tests/parity/ingest/make-fixtures.js
const fs = require('fs');
const path = require('path');
const { createContext, ROOT } = require('./legacy');

const FIX = path.join(ROOT, 'tests', 'POGovernance.Tests', 'Ingest', 'Fixtures');
const { ctx } = createContext();
const XLSX = ctx.XLSX, JSZip = ctx.JSZip;

function writeBook(name, sheets, bookType) {
  const wb = XLSX.utils.book_new();
  for (const [sn, aoa, fmts] of sheets) {
    const ws = XLSX.utils.aoa_to_sheet(aoa);
    for (const [ref, z] of Object.entries(fmts || {})) if (ws[ref]) { ws[ref].z = z; delete ws[ref].w; }
    XLSX.utils.book_append_sheet(wb, ws, sn);
  }
  const out = XLSX.write(wb, { bookType, type: 'array' });
  fs.writeFileSync(path.join(FIX, name), Buffer.from(out));
}

// F4 forecast as .xlsb
writeBook('F4_FORECAST.xlsb', [['FORCAST', [
  ['Forecast plan', '', '', ''],
  ['KEY-2', 'Brand Type', '06.2026', '07.2026', '08.2026'],
  ['PL-1012-499-NON-BOGO', 'PL', 120, 130.5, 0],
  ['PL-1014-799-BOGO', 'PL', 1234567, 0.333333333333, 99999999999],
]]], 'xlsb');

// Universe master as .xls (BIFF8)
writeBook('Universe_Master.xls', [['Universe_Master', [
  ['Generic Material', 'Brand Type', 'Division', 'MC CD', 'MC DES', 'MRP', 'Flag'],
  [1234567890, 'PL', 'MENS', 1012, 'T-SHIRT', 499, 'NON-BOGO'],
  [1234567891, 'PL', 'MENS', 1012, 'T-SHIRT', 599.99, 'BOGO'],
  ['1234567892', 'NON-PL', 'LADIES', 1014, 'KURTA', 1299, true],
], { F3: '0.00', F4: '#,##0' }]], 'biff8');

// Colour master CSV (comma, quotes, tricky date-ish values) — UTF-8 with BOM
const csv1 = [
  'Colour Code,Colour Name,Family,Updated,Note',
  'C01,NAVY,"BLUE, DARK",6/1/2026,1-2',
  'C02,"RED ""HOT""",RED,2026-06-01,10-12',
  'C03,  WHITE ,WHITE,01.06.2026,"=""x"""',
  'C04,BLACK,,Jun 1 2026,=SUM(A1)',
  'C05,GREY,GREY,13/1/2026,"1,234"',
  '',
  'C06,Café,BROWN,1/2/3,TRUE',
  'C07,PINK,RED,2026,$12.50',
  'C08,ROSE,RED,Jun-26,(45)',
  'C09,PEACH,RED,6-2026,5%',
].join('\r\n') + '\r\n';
fs.writeFileSync(path.join(FIX, 'Colour_Master.csv'), Buffer.concat([Buffer.from([0xEF, 0xBB, 0xBF]), Buffer.from(csv1, 'utf8')]));

// SCR products / cat head mapping as semicolon text without BOM (read as Latin-1 like the browser)
const csv2 = [
  'Brand Type;Division;MC CD;MRP;Category Head;Buyer',
  'PL;MENS;1012;499;Anil Kumar;Ravi',
  'PL;LADIES;1014;799;Sunita;Meena',
].join('\n');
fs.writeFileSync(path.join(FIX, 'SCR_PRODUCTS.csv'), Buffer.from(csv2, 'latin1'));

// A broken workbook (zip signature, garbage)
fs.writeFileSync(path.join(FIX, 'corrupt.xlsx'), Buffer.from('PK\u0003\u0004this is not really a zip file at all', 'latin1'));

(async () => {
  const zip = new JSZip();
  const add = (inZip, file) => zip.file(inZip, fs.readFileSync(path.join(FIX, file)));
  add('bundle/F1_STOCK_portal.xlsx', 'F1_STOCK_portal.xlsx');
  add('bundle/PO_raw.xlsx', 'PO_raw.xlsx');
  add('bundle/F4_FORECAST.xlsb', 'F4_FORECAST.xlsb');
  add('bundle/Universe_Master.xls', 'Universe_Master.xls');
  add('bundle/BSR_Kids.xlsx', 'BSR_Kids.xlsx');
  add('bundle/BSR_Ladies.xlsx', 'BSR_Ladies.xlsx');
  add('bundle/masters/Colour_Master.csv', 'Colour_Master.csv');
  add('bundle/AttrRel_master.xlsx', 'AttrRel_master.xlsx');
  zip.file('bundle/readme.txt', 'not data');
  zip.file('bundle/Notes.csv', 'just,some,text\n1,2,3\n');
  const out = await zip.generateAsync({ type: 'uint8array', compression: 'DEFLATE' });
  fs.writeFileSync(path.join(FIX, 'bundle.zip'), Buffer.from(out));
  console.log('fixtures written to', FIX);
})();
