#!/usr/bin/env node
// Analytics parity harness (Attribute Analysis, Category Insights, Planner dashboard, AI chat context /
// AI Insights, How-it-works live values).
//
// Runs the ORIGINAL v1.1.4 functions, extracted verbatim from legacy/python-app/app/index.html, in a vm
// sandbox (S, document, Shared and fetch stubbed) on tests/parity/analytics/fixture.json and writes
// tests/parity/analytics/expected.json. The rendered HTML is reduced to plain structures (table cells,
// colours, texts) that the xUnit tests rebuild from the C# DTOs and compare.
//
//   node tests/parity/analytics/make-fixture.js   (once — fixture.json is committed)
//   LANG=en_IN.UTF-8 node tests/parity/analytics/run.js
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const ROOT = path.resolve(__dirname, '../../..');
const HTML = fs.readFileSync(path.join(ROOT, 'legacy/python-app/app/index.html'), 'utf8');
const FIX = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixture.json'), 'utf8'));
if ((1234567).toLocaleString('en-IN') !== '12,34,567') { console.error('Node needs full ICU (en-IN).'); process.exit(1); }

// ── extraction ─────────────────────────────────────────────────────────────
const lines = HTML.split('\n');
function extractFunction(name) {
  const re = new RegExp('^(async )?function ' + name.replace(/\$/g, '\\$') + '\\s*\\(');
  const start = lines.findIndex(l => re.test(l));
  if (start < 0) throw new Error('function not found: ' + name);
  const first = lines[start];
  const open = (first.match(/{/g) || []).length, close = (first.match(/}/g) || []).length;
  if (open > 0 && open === close) return first;
  let end = start + 1;
  while (end < lines.length && !/^}\s*$/.test(lines[end])) end++;
  return lines.slice(start, end + 1).join('\n');
}
function extractLine(prefix) { const l = lines.find(x => x.startsWith(prefix)); if (!l) throw new Error('line not found: ' + prefix); return l; }
function extractRange(startPrefix, endPrefix) {
  const s = lines.findIndex(l => l.startsWith(startPrefix));
  const e = lines.findIndex((l, i) => i > s && l.startsWith(endPrefix));
  if (s < 0 || e < 0) throw new Error('range not found: ' + startPrefix);
  return lines.slice(s, e).join('\n');
}
function extractAiInsightsScript() {
  const marker = HTML.indexOf('<!-- ═══ AI INSIGHTS (BYOK');
  const s = HTML.indexOf('<script>', marker) + '<script>'.length;
  return HTML.slice(s, HTML.indexOf('</script>', s));
}
const SRC = [
  extractLine('const fmt = v =>'),
  extractFunction('_approvedQtyFor'), extractFunction('dpill'), extractFunction('_kv'), extractFunction('$g'),
  extractFunction('_n'), extractFunction('_sf'), extractFunction('_match'), extractFunction('_f1ByCol'), extractFunction('_f3ByCol'),
  extractFunction('_amAttrMap'), extractFunction('_f1f3ByAttr'),
  extractRange('var _aaTree = null;', '// Reads the Assessment Dimension'),
  extractRange('const DECISION_COLORS', 'async function clearAllRequests'),
  extractAiInsightsScript(),
  extractFunction('fmtNumIN'), extractFunction('renderEngineBHelp')
].join('\n\n');

// ── DOM stub ───────────────────────────────────────────────────────────────
function makeEl(id) {
  const el = {
    id, textContent: '', style: {}, children: [], disabled: false, scrollTop: 0, scrollHeight: 0, _html: '', _value: '',
    get innerHTML() { return this._html; },
    set innerHTML(v) { this._html = v; if (/^(cd-f-|pd-f-|aa-filter-brand)/.test(id || '')) this._value = ''; },
    get value() { return this._value; }, set value(v) { this._value = v; },
    classList: { add() { }, remove() { }, toggle() { } },
    appendChild(c) { c.parentElement = this; this.children.push(c); return c; },
    remove() { }, focus() { }
  };
  return el;
}
const decode = s => String(s).replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&amp;/g, '&');
const strip = s => decode(String(s).replace(/<[^>]+>/g, '')).replace(/\s+/g, ' ').trim();
const all = (re, s) => { const r = []; let m; while ((m = re.exec(s))) r.push(m); return r; };
function attr(tag, name) { const m = new RegExp(name + '="([^"]*)"').exec(tag); return m ? decode(m[1]) : null; }
function tables(html) {
  return all(/<table[^>]*>([\s\S]*?)<\/table>/g, html).map(t => ({
    headers: all(/<th[^>]*>([\s\S]*?)<\/th>/g, t[1]).map(m => strip(m[1])),
    rows: all(/<tr([^>]*)>([\s\S]*?)<\/tr>/g, t[1].replace(/<thead>[\s\S]*?<\/thead>/, '')).map(r => ({
      style: attr(r[1], 'style'), title: attr(r[1], 'title'),
      cells: all(/<td([^>]*)>([\s\S]*?)<\/td>/g, r[2]).map(c => strip(c[2])),
      cellStyles: all(/<td([^>]*)>/g, r[2]).map(c => attr(c[1], 'style'))
    }))
  }));
}
function options(el) { return all(/<option value="([^"]*)">([^<]*)<\/option>/g, el.innerHTML).map(m => decode(m[1])); }

function parseAA(html) {
  const o = {};
  const cr = /^<div style="font-size:11px;color:var\(--ink3\);margin-bottom:10px">([\s\S]*?)<\/div>/.exec(html);
  o.crumb = cr ? strip(cr[1]) : null;
  o.empty = html.startsWith('<div class="card" style="padding:40px') ? [strip(html)] : all(/<div class="card" style="padding:(?:24px);text-align:center;color:var\(--ink3\)">([\s\S]*?)<\/div>(?=<|$)/g, html).map(m => strip(m[1]));
  o.notes = all(/<div class="card" style="padding:12px 16px;margin-bottom:10px;[^"]*">([^<]*)<\/div>/g, html).map(m => m[1]);
  o.kv = all(/<div style="font-size:8\.5px;[^"]*">([^<]*)<\/div><div style="font-size:11\.5px;[^"]*">([^<]*)<\/div>/g, html).map(m => [m[1], decode(m[2])]);
  o.titles = all(/<div style="font-size:12px;font-weight:700;color:var\(--vmm-navy\);margin:[^"]*">([^<]*)<\/div>/g, html).map(m => decode(m[1]));
  o.bands = all(/border-left:4px solid (#\w+);border-radius:8px;padding:8px 10px"><div [^>]*>(.*?)<\/div><div style="font-size:16px;font-weight:700;color:#\w+">(\d+)<\/div><div [^>]*>([^<]*)<\/div>/g, html).map(m => [m[1], m[2], m[3], m[4]]);
  o.hasLegend = html.includes('Fast — selling ahead of stock');
  o.hasDevLegend = html.includes('Selling ahead of stock share (chase risk)');
  o.tables = tables(html);
  return o;
}
function parseDash(html, kpiFont) {
  const o = {};
  const et = /<div class="empty-title">([^<]*)<\/div>/.exec(html);
  if (et) { o.emptyTitle = et[1]; o.emptySub = strip(/<div class="empty-sub">([\s\S]*?)<\/div>/.exec(html)[1]); return o; }
  o.kpis = all(new RegExp('<div style="background:(#\\w+);border:1px solid (#\\w+)33;[^"]*">\\s*<div style="font-size:' + kpiFont + 'px;[^"]*">([^<]*)</div>\\s*<div [^>]*>([^<]*)</div>', 'g'), html).map(m => ({ bg: m[1], c: m[2], val: decode(m[3]), label: m[4] }));
  o.mixTitle = strip(/<div class="rc-hdr">(📊 Decision Mix[^<]*)<\/div>/.exec(html)[1]);
  o.arcs = all(/stroke="(#\w+)" stroke-width="6" stroke-dasharray="(\S+) (\S+)" stroke-dashoffset="(\S+)"/g, html).map(m => [m[1], m[2], m[3], m[4]]);
  o.legend = all(/background:(#\w+);display:inline-block;flex-shrink:0"><\/span>\s*([\s\S]*?) <strong style="font-family:var\(--mono\)">(\d+)<\/strong>\s*<span style="color:var\(--ink3\)">\((\d+)%\)<\/span>/g, html).map(m => [m[1], decode(m[2]), m[3], m[4]]);
  o.driverTitle = strip(/<div class="rc-hdr">(🔎[^<]*)<\/div>/.exec(html)[1]);
  o.drivers = all(/<span>(.*?)<\/span><span style="font-family:var\(--mono\);font-weight:700">(\d+) · (\d+)%<\/span>/g, html).map(m => [decode(m[1]), m[2], m[3]]);
  const de = /<div style="font-size:12px;color:var\(--ink3\);padding:8px 2px">([^<]*)<\/div>/.exec(html);
  o.driverEmpty = de ? de[1] : null;
  o.tableTitles = all(/<div class="sum-hdr-t">([^<]*)<\/div>/g, html).map(m => decode(m[1]));
  o.tables = tables(html);
  o.recTitle = strip(/<div class="rc-hdr">(💡[^<]*)<\/div>/.exec(html)[1]);
  o.recs = all(/border-left:3px solid var\(--vmm-pink\)">([\s\S]*?)<\/div>/g, html).map(m => m[1]);
  return o;
}

// ── sandbox ────────────────────────────────────────────────────────────────
function sandbox(opts) {
  const els = {};
  const sel = {};
  const document = {
    getElementById(id) { return els[id] || (els[id] = makeEl(id)); },
    createElement(t) { return makeEl(null); },
    querySelectorAll(s) { return s.split(',').map(c => { const k = c.trim().slice(1); return { set textContent(v) { sel[k] = v; } }; }); }
  };
  const toasts = [];
  const captured = [];
  const S = { files: JSON.parse(JSON.stringify(FIX.files)), params: Object.assign({ T1: 75, T2: 100, D: 75, mix: 2, N: 2, mixWindowDays: 45, strBuffer: 5 }, opts.params || {}),
    requests: JSON.parse(JSON.stringify(opts.requests || [])), role: opts.role || 'category' };
  for (const k of opts.drop || []) S.files[k] = null;
  const Shared = {
    ai: { chat: true, insights: true },
    visibleRequests(list) { return list; },
    aiChat(system, messages) { captured.push({ system, messages: JSON.parse(JSON.stringify(messages)) }); return Promise.resolve(opts.chatResponse(captured.length)); },
    aiInsights(prompt) { captured.push({ prompt }); return Promise.resolve(opts.insightsResponse); }
  };
  const ctx = vm.createContext({ S, document, Math, JSON, Object, Array, String, Number, Set, Map, Promise, parseFloat, parseInt, isFinite, isNaN, Infinity,
    console, toast: m => toasts.push(m), __rows: JSON.parse(JSON.stringify(opts.rows || [])), Shared: opts.noShared ? undefined : Shared });
  ctx.window = ctx; // window.Shared → the stub (visibleRequests = identity: the service receives already-visible requests)
  vm.runInContext('let batchResults = __rows;\n' + SRC + '\n;globalThis.__setRows=function(r){batchResults=r;};', ctx, { filename: 'index-extract.js' });
  return { ctx, els, sel, toasts, captured, S, run: code => vm.runInContext(code, ctx) };
}

const expected = { generatedBy: 'tests/parity/analytics/run.js', source: 'legacy/python-app/app/index.html (v1.1.4)' };

// ── 1. Attribute Analysis ─────────────────────────────────────────────────
function attrAnalysis(name, drop, filters) {
  const sb = sandbox({ drop });
  const res = { name, drop, filters, levels: [] };
  sb.run('renderAttrAnalysis()');
  res.brands = options(sb.els['aa-filter-brand']);
  sb.els['aa-filter-brand'].value = filters.brand || ''; sb.els['aa-filter-mc'] = sb.els['aa-filter-mc'] || makeEl('aa-filter-mc');
  sb.els['aa-filter-mc'].value = filters.mc || ''; sb.els['aa-filter-minstk'] = sb.els['aa-filter-minstk'] || makeEl('aa-filter-minstk');
  sb.els['aa-filter-minstk'].value = filters.minStk || '';
  sb.run('_aaRenderLevel1()');
  res.level1 = parseAA(sb.els['aa-content'].innerHTML);
  if (!filters.deep) return res;
  const n1 = sb.run('_aaLevel1Rows.length');
  for (let i = 0; i < n1; i++) {
    sb.run('_aaGoLevel2(' + i + ')');
    const l2 = { index: i, brand: sb.run('_aaState.brand'), mcKey: sb.run('_aaState.mcKey'), level2: parseAA(sb.els['aa-content'].innerHTML), level3: [] };
    const n2 = sb.run('_aaLevel2Rows.length');
    for (let j = 0; j < n2; j++) {
      sb.run('_aaGoLevel3(' + j + ')');
      l2.level3.push({ index: j, mrp: sb.run('_aaState.mrp'), flag: sb.run('_aaState.flag'), view: parseAA(sb.els['aa-content'].innerHTML) });
    }
    res.levels.push(l2);
  }
  res.attrCols = sb.run('_aaDiscoverAttrCols()');
  return res;
}
expected.attrAnalysis = [
  attrAnalysis('all', [], { deep: true }),
  attrAnalysis('filter-brand', [], { brand: 'MAVIE' }),
  attrAnalysis('filter-mc', [], { mc: ' cap ', minStk: '100' }),
  attrAnalysis('filter-minstk', [], { minStk: '1,000' }),
  attrAnalysis('filter-nomatch', [], { brand: 'NON-PL', mc: 'zzz' }),
  attrAnalysis('filter-badbrand', [], { brand: 'NOPE' }),
  attrAnalysis('no-artmaster', ['artmaster'], { deep: true }),
  attrAnalysis('no-f1f3', ['f1', 'f3'], {}),
  attrAnalysis('no-f1', ['f1'], { deep: true })
];

// ── 2. Category insights + 3. Planner dashboard ───────────────────────────
const CD = ['cd-f-brand', 'cd-f-div', 'cd-f-mc', 'cd-f-month', 'cd-f-engine', 'cd-f-decision'];
function catInsights(name, reqs, f) {
  const sb = sandbox({ requests: reqs });
  CD.forEach((id, i) => { sb.run('void 0'); const e = sb.ctx.document.getElementById(id); e.value = f[i] || ''; });
  sb.run('renderCatInsights()');
  const opt = {};
  CD.slice(0, 4).forEach(id => { opt[id] = options(sb.els[id]); });
  return { name, reqIdx: reqs.map(r => FIX.requests.indexOf(r)), filters: f, options: opt, applied: CD.map(id => sb.els[id].value), view: parseDash(sb.els['cat-insights-body'].innerHTML, 19) };
}
const R = FIX.requests;
expected.catInsights = [
  catInsights('all', R, []),
  catInsights('empty', [], []),
  catInsights('brand-mavie', R, ['MAVIE']),
  catInsights('div-mc', R, ['', 'LADIES', 'L-JEANS']),
  catInsights('month', R, ['', '', '', '11.2026']),
  catInsights('month-missing', R, ['', '', '', '—']),
  catInsights('engine-B', R, ['', '', '', '', 'B']),
  catInsights('engine-NEW', R, ['', '', '', '', 'NEW']),
  catInsights('decision-block', R, ['', '', '', '', '', 'Block']),
  catInsights('decision-allow', R, ['', '', '', '', '', 'Auto Allow']),
  catInsights('nomatch', R, ['ZED', '', '', '', 'A', 'Allow']),
  catInsights('stale-brand', R, ['GONE']),
  catInsights('unclassified', R, ['', '', 'Unclassified']),
  catInsights('allow-only', R.filter(r => r.decision === 'Allow' || r.decision === 'Auto Allow'), []),
  catInsights('few', R.slice(0, 4), []),
  catInsights('watch-heavy', R.filter(r => r.decision === 'Watch List' || r.decision === 'Allow'), []),
  catInsights('one', R.slice(5, 6), [])
];
const PD = ['pd-f-brand', 'pd-f-div', 'pd-f-mc', 'pd-f-mrp', 'pd-f-flag', 'pd-f-dm', 'pd-f-engine', 'pd-f-decision'];
function planner(name, rows, f) {
  const sb = sandbox({ rows });
  ['planner-dash-placeholder', 'planner-dash-filters', 'planner-dash-body'].forEach(id => sb.ctx.document.getElementById(id));
  PD.forEach((id, i) => { sb.ctx.document.getElementById(id).value = f[i] || ''; });
  sb.run('renderPlannerDashboard()');
  const opt = {};
  PD.slice(0, 6).forEach(id => { opt[id] = options(sb.els[id]); });
  return { name, rowIdx: rows.map(r => FIX.tool2Rows.indexOf(r)), filters: f, options: opt, applied: PD.map(id => sb.els[id].value), placeholder: sb.els['planner-dash-placeholder'].style.display || '',
    filtersBox: sb.els['planner-dash-filters'].style.display || '', view: rows.length ? parseDash(sb.els['planner-dash-body'].innerHTML, 18) : null };
}
const T = FIX.tool2Rows;
expected.planner = [
  planner('all', T, []),
  planner('empty', [], []),
  planner('brand', T, ['MAVIE']),
  planner('div-missing', T, ['', '—']),
  planner('mc', T, ['', '', 'L-JEANS']),
  planner('mrp', T, ['', '', '', '599']),
  planner('mrp-num', T, ['', '', '', '1299']),
  planner('flag', T, ['', '', '', '', 'BOGO']),
  planner('dm-missing', T, ['', '', '', '', '', '—']),
  planner('engine-A', T, ['', '', '', '', '', '', 'A']),
  planner('decision', T, ['', '', '', '', '', '', '', 'Cancel']),
  planner('nomatch', T, ['NON-PL', '', 'K-TOP']),
  planner('retain-only', T.filter(r => r.decision === 'Retain'), []),
  planner('few', T.slice(0, 3), []),
  planner('watch', T.filter(r => r.decision === 'Watch List'), [])
];

// ── 4. AI chat context + AI Insights ──────────────────────────────────────
async function chat(name, role, reqs, rows, questions) {
  const sb = sandbox({ role, requests: reqs, rows, chatResponse: n => n === 3 ? { ok: false, status: 503, data: { error: 'AI chat is not enabled' } }
    : n === 4 ? { ok: false, status: 500, data: { error: { message: 'overloaded' } } }
    : { ok: true, status: 200, data: { content: [{ type: 'text', text: ' Answer ' + n }, { type: 'tool_use' }, { type: 'text', text: 'line2 ' }] } } });
  sb.ctx.document.getElementById('ai-chat-messages');
  const turns = [];
  for (const q of questions) {
    sb.els['ai-chat-input'] = sb.els['ai-chat-input'] || makeEl('ai-chat-input');
    sb.els['ai-chat-input'].value = q;
    sb.run('sendAIChat()');
    await new Promise(r => setImmediate(r));
    const c = sb.captured[sb.captured.length - 1];
    const msgs = sb.els['ai-chat-messages'].children.map(b => b.children[0].textContent);
    turns.push({ question: q, system: c ? c.system : null, messages: c ? c.messages : null, lastBubble: msgs[msgs.length - 1] });
  }
  const ctxs = questions.map(q => JSON.parse(JSON.stringify(sb.run('_aiBuildContext(' + JSON.stringify(q) + ')'))));
  return { name, role, reqIdx: reqs.map(r => FIX.requests.indexOf(r)), rowIdx: rows.map(r => FIX.tool2Rows.indexOf(r)), turns, contexts: ctxs.map(c => ({ summary: JSON.stringify(c.summary), sample: JSON.stringify(c.sample) })) };
}
const QS = ['why was 1101 blocked?', 'which MC is riskiest', 'summarize', 'BLK colour in L-JEANS?', 'zzzz qqqq', 'what about mavie kurta and v100203', 'ok', 'po 4500002010 status'];
async function insights(name, rows, focus, resp) {
  const sb = sandbox({ rows, insightsResponse: resp });
  if (focus) sb.ctx.document.getElementById('ai-focus').value = focus;
  const summary = sb.run('_aiBuildSummary(' + JSON.stringify(focus) + ')');
  const out = { name, rowIdx: rows.map(r => FIX.tool2Rows.indexOf(r)), focus, response: resp, summary: summary ? JSON.parse(JSON.stringify(summary)) : null };
  if (summary) out.prompt = sb.run('_aiPromptFor(' + JSON.stringify(focus || 'risk') + ', _aiBuildSummary())');
  await sb.run('renderAIInsights()');
  const g = id => sb.els[id];
  if (g('ai-kpi-strip')) {
    out.kpis = all(/border-top:3px solid ([^"]+)"><div [^>]*>([^<]*)<\/div><div [^>]*>([^<]*)<\/div>/g, g('ai-kpi-strip').innerHTML).map(m => [m[1], m[2], m[3]]);
    out.cards = all(/<div class="card" style="padding:14px 16px;border-left:4px solid var\(--vmm-cyan\)"><div [^>]*>([\s\S]*?)<\/div>(?:<div style="font-size:18px[^"]*">([^<]*)<span[^>]*>[^<]*<\/span><\/div>)?<div style="font-size:11\.5px[^"]*">([\s\S]*?)<\/div><\/div>/g, g('ai-risk-cards').innerHTML)
      .map(m => [decode(m[1]), m[2] === undefined ? null : m[2], decode(m[3])]);
    const cal = /<div style="font-size:10px;[^"]*">([^<]*)<\/div><div style="font-size:13px;[^"]*">([^<]*)<\/div><div style="font-size:12px;[^"]*">([^<]*)<\/div>/.exec(g('ai-action-card').innerHTML);
    out.callout = cal ? [cal[1], decode(cal[2]), decode(cal[3])] : null;
    out.meta = g('ai-insights-meta').textContent;
    out.fallback = g('ai-insights-fallback').style.display === 'block' ? g('ai-insights-text').textContent : null;
  }
  out.toasts = sb.toasts;
  return out;
}
const goodJson = JSON.stringify({ findings: [{ title: 'Vendor V100203 <dominates>', impactQty: 4200, insight: 'Excess & clustering.' },
  { title: 'Nov cluster', impactQty: '1500.5', insight: 'Month 11.2026.' }, { title: 'Rule check', impactQty: null, insight: 'Q1 cut.' },
  { title: 'Extra', impactQty: 'n/a', insight: 'fourth' }], action: { title: 'Fix forecast', detail: 'Re-plan <MRP 599>.' } });
const ok = content => ({ ok: true, status: 200, data: { choices: [{ message: { role: 'assistant', content } }], _provider: 'anthropic' } });
(async () => {
  expected.chat = [
    await chat('tool1', 'category', R, [], QS),
    await chat('tool1-empty', 'category', [], [], ['hello there']),
    await chat('tool2', 'planner', [], T, QS),
    await chat('tool2-empty', 'planner', [], [], ['hello there'])
  ];
  expected.insights = [
    await insights('risk-json', T, 'risk', ok(goodJson)),
    await insights('brand-fenced', T, 'brand', ok('```json\n' + goodJson + '\n```')),
    await insights('action-prose', T, 'action', ok('Here you go: ' + JSON.stringify({ findings: [{ title: 'A', insight: 'B', impactQty: 0 }] }) + ' thanks')),
    await insights('unknown-focus', T, 'weird', ok('not json at all')),
    await insights('empty-content', T, 'risk', ok('')),
    await insights('no-findings', T, '', ok(JSON.stringify({ findings: [], action: { title: 'x', detail: 'y' } }))),
    await insights('no-action', T.filter(r => r.decision === 'Retain'), 'risk', ok(JSON.stringify({ findings: [{ title: 't', impactQty: true, insight: 'i' }] }))),
    await insights('error-obj', T, 'risk', { ok: false, status: 429, data: { error: { message: 'rate limited' } } }),
    await insights('error-str', T, 'risk', { ok: false, status: 503, data: { error: 'AI Insights is not enabled' } }),
    await insights('error-status', T, 'risk', { ok: false, status: 502, data: {} }),
    await insights('no-rows', [], 'risk', ok('{}'))
  ];
  // ── 5. How it works live values ──────────────────────────────────────────
  expected.help = [{}, { T1: 60, T2: 0, strBuffer: 0, mix: 0, D: 0 }, { T1: 300, T2: 412.5, strBuffer: 12.5, D: 45, N: 3, mixWindowDays: 0 },
    { T1: 230, T2: 221.5, strBuffer: 5, mix: 7.5, mixWindowDays: 30 }, { T1: 75.333, strBuffer: undefined, T2: 1000 }].map(p => {
    const sb = sandbox({ params: p });
    sb.run('renderEngineBHelp()');
    return { params: sb.S.params, values: sb.sel };
  });
  fs.writeFileSync(path.join(__dirname, 'expected.json'), JSON.stringify(expected, null, 1));
  console.log('wrote expected.json: attr', expected.attrAnalysis.length, 'cat', expected.catInsights.length, 'planner', expected.planner.length,
    'chat', expected.chat.length, 'insights', expected.insights.length, 'help', expected.help.length);
})().catch(e => { console.error(e); process.exit(1); });
