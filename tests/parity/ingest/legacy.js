'use strict';
// Loads the ORIGINAL browser code of v1.1.4 into a vm sandbox that looks like the browser page:
//   - legacy/python-app/app/vendor/xlsx.min.js  (SheetJS / xlsx-js-style, the exact file the page used)
//   - legacy/python-app/app/vendor/jszip.min.js
//   - the Data Setup functions copied verbatim (extracted by name) from legacy/python-app/app/index.html
// with minimal shims for document / toast / alert / FileReader. Run with TZ=Asia/Kolkata.
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const ROOT = path.resolve(__dirname, '..', '..', '..');
const APP = path.join(ROOT, 'legacy', 'python-app', 'app');

// Functions / constants of index.html used by the upload flows.
const FUNCTIONS = ['_bestSheetName', 'loadFile', 'clearFileSlot', '_zipRules', '_parseWorkbookForKey', 'loadZipBundle',
  '_rawHdr', '_rawPick', '_rawPickAfter', '_classifyRawSheet', '_rawObjFromHeaders', '_transformPOPending',
  '_transformStock', '_transformSalesTrend', '_transformArticleMaster', '_transformBSRDetail', '_processRawWorkbook',
  'loadGRCFile', 'handleRawImport', 'dateToMMYYYY'];
const CONSTS = ['MULTI_KEYS', 'REQUIRED_KEYS', 'isMMYYYY', 'toYM', 'isDayCol', 'dayColNum'];

// Extract `function name(...) {...}` with a small scanner that skips strings, templates, comments, regexes.
function extractFunction(src, name) {
  const re = new RegExp('(?:async\\s+)?function\\s+' + name.replace(/\$/g, '\\$') + '\\s*\\(');
  const m = re.exec(src);
  if (!m) throw new Error('function not found: ' + name);
  let i = src.indexOf('{', m.index);
  return src.slice(m.index, matchBrace(src, i) + 1);
}
function extractConst(src, name) {
  const re = new RegExp('^const\\s+' + name + '\\s*=.*$', 'm');
  const m = re.exec(src);
  if (!m) throw new Error('const not found: ' + name);
  return m[0];
}
function matchBrace(src, start) {
  let depth = 0, i = start, prev = '';
  const stack = []; // template literal nesting
  while (i < src.length) {
    const c = src[i];
    if (c === '/' && src[i + 1] === '/') { i = src.indexOf('\n', i); continue; }
    if (c === '/' && src[i + 1] === '*') { i = src.indexOf('*/', i) + 2; continue; }
    if (c === '"' || c === "'") { i = skipString(src, i, c); prev = 'x'; continue; }
    if (c === '`') { i = skipTemplate(src, i); prev = 'x'; continue; }
    if (c === '/' && /[(,=:[!&|?{};+\-*%<>~^]/.test(prev || '(')) { i = skipRegex(src, i); prev = 'x'; continue; }
    if (c === '{') depth++;
    if (c === '}') { depth--; if (depth === 0) return i; }
    if (!/\s/.test(c)) prev = c;
    i++;
  }
  throw new Error('unbalanced');
}
function skipString(src, i, q) { i++; while (src[i] !== q) { if (src[i] === '\\') i++; i++; } return i + 1; }
function skipTemplate(src, i) {
  i++;
  while (src[i] !== '`') {
    if (src[i] === '\\') { i += 2; continue; }
    if (src[i] === '$' && src[i + 1] === '{') { i = matchBrace(src, i + 1) + 1; continue; }
    i++;
  }
  return i + 1;
}
function skipRegex(src, i) {
  i++; let inClass = false;
  while (true) {
    const c = src[i];
    if (c === '\\') { i += 2; continue; }
    if (c === '[') inClass = true; else if (c === ']') inClass = false;
    else if (c === '/' && !inClass) break;
    i++;
  }
  i++;
  while (/[a-z]/i.test(src[i])) i++;
  return i;
}

function makeElement(id) {
  const classes = new Set();
  return {
    id, textContent: '', className: '', innerHTML: '', style: {}, value: '',
    classList: { add: c => classes.add(c), remove: c => classes.delete(c), contains: c => classes.has(c) },
    get classes() { return [...classes]; }
  };
}

function createContext() {
  const elements = {};
  const log = { toasts: [], alerts: [], dirty: [] };
  const document = { getElementById: id => (elements[id] = elements[id] || makeElement(id)) };
  const sandbox = {
    console, setTimeout, clearTimeout, setInterval, clearInterval, setImmediate, queueMicrotask, Promise, TextDecoder, TextEncoder,
    Uint8Array, Uint16Array, Uint32Array, Int8Array, Int16Array, Int32Array, Float32Array, Float64Array, ArrayBuffer, DataView,
    document,
    toast: m => log.toasts.push(String(m)),
    alert: m => log.alerts.push(String(m)),
    updateDots: () => {}, renderSeasonPills: () => {}, renderGRCImpact: () => {},
    FileReader: class {
      readAsArrayBuffer(file) { setTimeout(() => this.onload({ target: { result: file._ab } }), 0); }
    }
  };
  sandbox.window = sandbox; sandbox.self = sandbox;
  const ctx = vm.createContext(sandbox);
  vm.runInContext(fs.readFileSync(path.join(APP, 'vendor', 'xlsx.min.js'), 'utf8'), ctx, { filename: 'xlsx.min.js' });
  vm.runInContext(fs.readFileSync(path.join(APP, 'vendor', 'jszip.min.js'), 'utf8'), ctx, { filename: 'jszip.min.js' });
  const html = fs.readFileSync(path.join(APP, 'index.html'), 'utf8');
  let code = 'var S = {files:{f1:null,f2:null,f3:null,f4:null,um:null,bsr:null,artmaster:null,colmaster:null,attrrel:null,catheadmap:null},params:{}};\n' +
    'var _idx = {}; var RS = {};\nfunction $g(id){ return document.getElementById(id); }\n';
  for (const c of CONSTS) code += extractConst(html, c).replace(/^const /, 'var ') + '\n';
  for (const f of FUNCTIONS) code += extractFunction(html, f) + '\n';
  // _renderGRCMonthPicker only fills a <select>; record what it was given.
  code += 'function _renderGRCMonthPicker(m){ __log.picker = m.slice(); }\n';
  sandbox.__log = log;
  vm.runInContext(code, ctx, { filename: 'index.html (extracted)' });
  // shared.js installWatchers(): record which S.files keys were set (= what pushFiles saves, in order)
  vm.runInContext(`S.files = new Proxy(S.files, { set: function(t,k,v){ t[k]=v; var key = k==='grcPeriodType'?'grc':k; if(__log.dirty.indexOf(key)<0) __log.dirty.push(key); return true; } });`, ctx);
  return { ctx, elements, log, sandbox };
}

function fileObj(ctx, name, buf) {
  const u8 = new Uint8Array(buf);
  const ab = u8.buffer.slice(u8.byteOffset, u8.byteOffset + u8.byteLength);
  // Browser File objects are Blobs; JSZip needs a binary type here, so hand it a Uint8Array carrying the File API bits.
  const f = new Uint8Array(ab);
  f.name = name; f._ab = ab; f.arrayBuffer = async () => ab;
  return f;
}

module.exports = { createContext, fileObj, ROOT, APP, extractFunction };
