// UAT certificate as a Word document (same content and look as PO-Governance-UAT-Certificate.pdf, PDFKit design):
// logo letterhead, pink section bars, pink table headers, page numbers. Tests come from tests.json (taken from Program.cs).
// Run from the repository root: node tools/UatCertificate/make-docx.js [output.docx]
const fs = require('fs');
const path = require('path');
const {
  Document, Packer, Paragraph, TextRun, ImageRun, Table, TableRow, TableCell, WidthType, BorderStyle, ShadingType,
  AlignmentType, Footer, PageNumber,
  VerticalAlign, HeightRule, Tab, TabStopType
} = require('docx');

const root = path.resolve(__dirname, '..', '..');
const out = path.resolve(process.argv[2] || path.join(root, 'docs', 'uat', 'PO-Governance-UAT-Certificate.docx'));
const modules = JSON.parse(fs.readFileSync(path.join(__dirname, 'tests.json'), 'utf8'));
const logo = fs.readFileSync(path.join(root, 'Admin', 'wwwroot', 'img', 'logo.png'));

const PINK = 'D81B60', BLUE = '215AA8', GREY = '808080', LINE = 'D9D9D9', FONT = 'Arial';
const W = 10206;                                   // A4 content width (11906 - 2 x 850 DXA)
const today = new Date().toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
const REF = 'UAT-POG-2026-01';

const none = { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' };
const noBorders = { top: none, bottom: none, left: none, right: none };
const rowLine = { top: none, left: none, right: none, bottom: { style: BorderStyle.SINGLE, size: 4, color: LINE } };

const run = (text, o = {}) => new TextRun({ text, font: FONT, size: o.size || 20, bold: o.bold, color: o.color, italics: o.italics });
const para = (text, o = {}) => new Paragraph({
  children: Array.isArray(text) ? text : [run(text, o)],
  alignment: o.align, spacing: { before: o.before || 0, after: o.after === undefined ? 80 : o.after, line: o.line || 264 },
  keepNext: o.keepNext
});

// pink section bar (PDFKit AddSectionBar)
const bar = (title) => new Paragraph({
  children: [run(title.toUpperCase(), { bold: true, color: 'FFFFFF', size: 17 })],
  shading: { type: ShadingType.CLEAR, color: 'auto', fill: PINK },
  border: { right: { style: BorderStyle.SINGLE, size: 48, color: BLUE, space: 0 } },
  spacing: { before: 200, after: 80 }, indent: { left: 0 }, keepNext: true
});

const cell = (text, width, o = {}) => new TableCell({
  width: { size: width, type: WidthType.DXA },
  borders: o.borders || rowLine,
  shading: o.fill ? { type: ShadingType.CLEAR, color: 'auto', fill: o.fill } : undefined,
  margins: { top: 60, bottom: 60, left: 90, right: 90 },
  verticalAlign: VerticalAlign.CENTER,
  children: (Array.isArray(text) ? text : [text]).map(t =>
    para(t, { bold: o.bold, color: o.color, size: o.size || 18, align: o.align, after: 0 }))
});

// label + value rows (PDFKit AddWrappedKeyValue)
const kv = (rows) => new Table({
  width: { size: W, type: WidthType.DXA }, columnWidths: [2600, W - 2600],
  rows: rows.map(([k, v]) => new TableRow({ cantSplit: true, children: [cell(k, 2600, { bold: true, size: 19 }), cell(v, W - 2600, { size: 19 })] }))
});

// table with a pink header row that repeats on every page (PDFKit AddTable)
const table = (cols, rows) => new Table({
  width: { size: W, type: WidthType.DXA }, columnWidths: cols.map(c => c.w),
  rows: [
    new TableRow({ tableHeader: true, cantSplit: true,
      children: cols.map(c => cell(c.h, c.w, { fill: PINK, color: 'FFFFFF', bold: true, align: c.align, borders: noBorders })) }),
    ...rows.map(r => new TableRow({ cantSplit: true, children: cols.map((c, i) => cell(r[i] || '', c.w, { align: c.align })) }))
  ]
});

const testCols = [
  { h: '#', w: 650 }, { h: 'Functionality', w: 2700 }, { h: 'Expected result', w: 4256 },
  { h: 'Result', w: 1050, align: AlignmentType.CENTER }, { h: 'Remarks', w: 1550 }
];

const body = [];
// letterhead (PDFKit AddLetterhead)
body.push(new Paragraph({ children: [new ImageRun({ type: 'png', data: logo, transformation: { width: 120, height: 32 } })], spacing: { after: 80 } }));
body.push(new Paragraph({
  children: [run('Vishal Mega Mart', { bold: true, color: PINK, size: 28 }),
    new TextRun({ children: [new Tab(), 'Date: ' + today], font: FONT, size: 20, bold: true, color: PINK })],
  tabStops: [{ type: TabStopType.RIGHT, position: W }], spacing: { after: 0 }
}));
body.push(new Paragraph({
  children: [run('PO Governance', { color: GREY, size: 16 }),
    new TextRun({ children: [new Tab(), REF], font: FONT, size: 20, bold: true })],
  tabStops: [{ type: TabStopType.RIGHT, position: W }], spacing: { after: 120 }
}));
body.push(new Paragraph({
  children: [run('User Acceptance Test (UAT) Certificate', { size: 20 })], alignment: AlignmentType.CENTER,
  border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: GREY, space: 6 } }, spacing: { after: 200 }
}));
body.push(para('VMM PO Governance Platform – UAT Certificate', { bold: true, size: 24, after: 40 }));
body.push(para('Release: PO Governance (.NET) with SAP interface v1.4, MRA module (Python v1.1.7 parity), schema v8', { size: 16 }));

body.push(bar('1. Project details'));
body.push(kv([
  ['Project', 'VMM PO Governance Platform – PO validation, PO assessment and SAP integration'],
  ['Application', 'Portal (Admin, Blazor) + API (POGovApi) + SAP interface /api/sap/v1'],
  ['Release / build', 'Branch claude/admiring-shannon-0eqqlg · database schema v8 (05_sap_interface.sql) · API master 04_api_master.sql'],
  ['UAT environment', 'Staging – portal http://192.168.0.175:8177 · API http://192.168.0.175:8178 · SQL Server'],
  ['Business owner', 'Planning / Merchandising'],
  ['UAT period', 'From ____________________   To ____________________'],
  ['Prepared by', '______________________________']
]));

body.push(bar('2. Scope and approach'));
body.push(para('Every function of the tool listed below was tested in the UAT environment with business data by the users of each ' +
  'role (Buyer, Category Head, Planner, Admin) and by the SAP team for the SAP interface. For each test the tester records ' +
  'the result (Pass / Fail) and remarks. A failed test is retested after the fix; open points are listed in section 4.', { size: 19 }));
body.push(para('Result: write PASS or FAIL. Remarks: defect reference, data used or observation.', { size: 16, color: GREY }));

body.push(bar('3. Functionality tested'));
let n = 0;
for (const m of modules) {
  body.push(bar(m.title));
  body.push(table(testCols, m.rows.map(r => [String(++n).padStart(2, '0'), r.f, r.e, '', ''])));
}

body.push(bar('4. Open points / observations'));
body.push(table([{ h: '#', w: 650 }, { h: 'Observation', w: 5856 }, { h: 'Owner', w: 1900 }, { h: 'Target date', w: 1800 }],
  [1, 2, 3, 4, 5].map(i => [String(i), '', '', ''])));

body.push(bar('5. Certification'));
body.push(para('We confirm that the functionalities listed in section 3 were tested in the UAT environment and meet the business ' +
  'requirements, except for the open points recorded in section 4. The release is accepted for deployment to production.', { size: 19 }));
body.push(kv([['UAT result', 'ACCEPTED  /  ACCEPTED WITH OPEN POINTS  /  NOT ACCEPTED   (strike out what does not apply)']]));
body.push(para('', { after: 160 }));

// sign-off blocks (PDFKit AddSignatureBlocks): pink header, name and date, space for signature and stamp
const half = (W - 200) / 2;
const signCell = (role) => new TableCell({
  width: { size: half, type: WidthType.DXA }, borders: { top: none, left: none, right: none, bottom: { style: BorderStyle.SINGLE, size: 4, color: LINE } },
  margins: { top: 0, bottom: 80, left: 0, right: 0 },
  children: [
    new Paragraph({ children: [run(role, { bold: true, color: 'FFFFFF', size: 17 })], shading: { type: ShadingType.CLEAR, color: 'auto', fill: PINK },
      border: { right: { style: BorderStyle.SINGLE, size: 48, color: BLUE, space: 0 } }, spacing: { after: 80 } }),
    para([run('Name: ', { bold: true }), run('______________________________')], { after: 80 }),
    para([run('Date: ', { bold: true }), run('______________')], { after: 80 }),
    para('', { after: 400 }), para('', { after: 200 }),
    new Paragraph({ tabStops: [{ type: TabStopType.RIGHT, position: half - 100 }], spacing: { after: 0 },
      children: [run('Signature', { color: GREY, size: 16 }), new TextRun({ children: [new Tab(), 'Stamp'], font: FONT, size: 16, color: GREY })] })
  ]
});
const gap = () => new TableCell({ width: { size: 200, type: WidthType.DXA }, borders: noBorders, children: [para('', { after: 0 })] });
const spacerRow = () => new TableRow({ height: { value: 300, rule: HeightRule.EXACT },
  children: [new TableCell({ columnSpan: 3, borders: noBorders, children: [para('', { after: 0 })] })] });
body.push(new Table({
  width: { size: W, type: WidthType.DXA }, columnWidths: [half, 200, half],
  rows: [
    new TableRow({ cantSplit: true, children: [signCell('Business Owner (Planning)'), gap(), signCell('Category / Merchandising')] }),
    spacerRow(),
    new TableRow({ cantSplit: true, children: [signCell('SAP Team'), gap(), signCell('IT / Application Owner')] })
  ]
}));

const doc = new Document({
  creator: 'VMM PO Governance', title: 'UAT Certificate – VMM PO Governance Platform', description: REF,
  styles: { default: { document: { run: { font: FONT, size: 20 } } } },
  sections: [{
    properties: { page: { size: { width: 11906, height: 16838 }, margin: { top: 850, bottom: 1000, left: 850, right: 850, footer: 400 } } },
    footers: {
      default: new Footer({ children: [
        para('VMM PO Governance Platform · UAT Certificate · ' + REF, { size: 15, align: AlignmentType.CENTER, after: 0 }),
        new Paragraph({ alignment: AlignmentType.CENTER, children: [
          new TextRun({ text: 'Page ', font: FONT, size: 15 }), new TextRun({ children: [PageNumber.CURRENT], font: FONT, size: 15 }),
          new TextRun({ text: ' of ', font: FONT, size: 15 }), new TextRun({ children: [PageNumber.TOTAL_PAGES], font: FONT, size: 15 })] })
      ] })
    },
    children: body
  }]
});

Packer.toBuffer(doc).then(b => { fs.mkdirSync(path.dirname(out), { recursive: true }); fs.writeFileSync(out, b); console.log(out + ' · ' + n + ' tests'); });
