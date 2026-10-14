// Extracts top-level function / const declarations VERBATIM from the legacy
// index.html (legacy/python-app/app/index.html) so the parity harness runs the
// original v1.1.4 code, not a re-implementation.
'use strict';
const fs = require('fs');
const path = require('path');

const INDEX = path.resolve(__dirname, '../../../legacy/python-app/app/index.html');

function loadSource() { return fs.readFileSync(INDEX, 'utf8'); }

// Skips a JS body starting at `start` (an opening "{"), honouring strings,
// template literals (with nested ${}), comments and regex literals.
function matchBrace(src, start) {
  let depth = 0, i = start;
  const tmplStack = []; // depth at which each open template literal's ${ began
  let lastSig = '';
  while (i < src.length) {
    const c = src[i], n = src[i + 1];
    if (c === '/' && n === '/') { i = src.indexOf('\n', i); if (i < 0) return -1; continue; }
    if (c === '/' && n === '*') { i = src.indexOf('*/', i + 2) + 2; continue; }
    if (c === '"' || c === "'") {
      i++;
      while (src[i] !== c) { if (src[i] === '\\') i++; i++; }
      i++; lastSig = 'x'; continue;
    }
    if (c === '`') { i = skipTemplate(src, i + 1); lastSig = 'x'; continue; }
    if (c === '/' && (lastSig === '' || '(,=:[!&|?{};+-*%<>~^'.includes(lastSig) || /return$|typeof$/.test(src.slice(Math.max(0, i - 8), i).trim()))) {
      // regex literal
      i++;
      let inClass = false;
      while (true) {
        const ch = src[i];
        if (ch === '\\') { i += 2; continue; }
        if (ch === '[') inClass = true;
        else if (ch === ']') inClass = false;
        else if (ch === '/' && !inClass) break;
        i++;
      }
      i++;
      while (/[a-z]/i.test(src[i])) i++;
      lastSig = 'x'; continue;
    }
    if (c === '{') depth++;
    else if (c === '}') { depth--; if (depth === 0) return i; }
    if (!/\s/.test(c)) lastSig = c;
    i++;
  }
  return -1;

  function skipTemplate(s, j) {
    while (j < s.length) {
      const ch = s[j];
      if (ch === '\\') { j += 2; continue; }
      if (ch === '`') return j + 1;
      if (ch === '$' && s[j + 1] === '{') {
        // find matching } of the expression
        let d = 0, k = j + 1;
        // reuse matchBrace on the expression
        const end = matchBrace(s, k);
        j = end + 1;
        continue;
      }
      j++;
    }
    return j;
  }
}

function extractFunction(src, name) {
  const re = new RegExp('\\n(?:async\\s+)?function\\s+' + name.replace(/\$/g, '\\$') + '\\s*\\(', 'g');
  let m, last = null;
  while ((m = re.exec(src))) last = m; // later definition wins (JS override rule)
  if (!last) throw new Error('function not found: ' + name);
  const start = last.index + 1;
  const open = src.indexOf('{', src.indexOf(')', start));
  const end = matchBrace(src, open);
  if (end < 0) throw new Error('unbalanced: ' + name);
  return src.slice(start, end + 1);
}

// Single-line top-level declaration: "const NAME = ...;" / "let NAME ..." (first match)
function extractLine(src, prefix) {
  const idx = src.indexOf('\n' + prefix);
  if (idx < 0) throw new Error('declaration not found: ' + prefix);
  const end = src.indexOf('\n', idx + 1);
  return src.slice(idx + 1, end);
}

module.exports = { loadSource, extractFunction, extractLine };
