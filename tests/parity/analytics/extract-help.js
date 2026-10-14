#!/usr/bin/env node
// Extracts the static "How it works" text (index.html view-cat-howitworks) into one HTML fragment per tab
// (Admin/wwwroot/help/hw-<tab>.html) plus the hw-* styles (how-it-works.css). Texts and inline styles are kept;
// <script> blocks and on* handlers are removed; the live values renderEngineBHelp() wrote into .hwb-* / .hwb2-*
// spans become {{PLACEHOLDER}}s (values: GET api/v1/help/params → placeholders).
'use strict';
const fs = require('fs');
const path = require('path');
const ROOT = path.resolve(__dirname, '../../..');
const html = fs.readFileSync(path.join(ROOT, 'legacy/python-app/app/index.html'), 'utf8');
const out = path.join(ROOT, 'Admin/wwwroot/help');
fs.mkdirSync(out, { recursive: true });

const start = html.indexOf('<div id="view-cat-howitworks"');
const end = html.indexOf('<!-- /hw-wrap -->', start);
const view = html.slice(start, end);
const ids = ['access', 'vision', 't1-engA', 't1-engB', 't2-engA', 't2-engB', 't2-export'];
const PH = {
  'hwb-D': 'D', 'hwb2-D': 'D', 'hwb-T': 'T1', 'hwb2-T': 'T2', 'hwb-buf': 'BUF', 'hwb2-buf': 'BUF', 'hwb-effT': 'EFFT1', 'hwb2-effT': 'EFFT2',
  'hwb-mix': 'MIX', 'hwb2-mix': 'MIX', 'hwb-N': 'N', 'hwb2-N': 'N', 'hwb-mwd': 'MWD', 'hwb2-mwd': 'MWD',
  'hwb-maxperm-calc': 'T1_CALC', 'hwb2-maxperm-calc': 'T2_CALC', 'hwb-maxperm-result': 'T1_RESULT', 'hwb2-maxperm-result': 'T2_RESULT'
};
const starts = ids.map(id => ({ id, at: view.indexOf('<div id="hw-' + id + '" class="hw-tab-panel') })).sort((a, b) => a.at - b.at);
let total = 0;
starts.forEach((s, i) => {
  if (s.at < 0) throw new Error('panel not found: ' + s.id);
  let frag = view.slice(s.at, i + 1 < starts.length ? starts[i + 1].at : view.length).trimEnd();
  // the last panel is followed by the hw-wrap closing </div> — drop it
  if (i + 1 === starts.length) frag = frag.replace(/\s*<\/div>\s*$/, '');
  frag = frag.replace(/<script[\s\S]*?<\/script>/g, '')
    .replace(/\s+on[a-z]+="[^"]*"/g, '')
    .replace(/(<div id="hw-[^"]+" class="hw-tab-panel)(?: active)?" style="display:(?:none|block)"/, '$1 active"')
    .replace(/<span class="(hwb2?-[A-Za-z-]+)">[^<]*<\/span>/g, (m, cls) => {
      if (!PH[cls]) throw new Error('unknown live class ' + cls);
      total++;
      return '<span class="' + cls + '">{{' + PH[cls] + '}}</span>';
    });
  const header = '<!-- How it works — tab "' + s.id + '". Extracted from legacy/python-app/app/index.html (v1.1.4) by tests/parity/analytics/extract-help.js.\n'
    + '     Styles: help/how-it-works.css. {{NAME}} = live value from GET api/v1/help/params (placeholders). -->\n';
  fs.writeFileSync(path.join(out, 'hw-' + s.id + '.html'), header + frag + '\n');
});
// hw-* CSS rules of the main <style>
const css = html.split('\n').filter(l => /^\s*\.hw-/.test(l)).map(l => l.trim()).join('\n');
fs.writeFileSync(path.join(out, 'how-it-works.css'), '/* How it works styles — the .hw-* rules of legacy index.html (v1.1.4). Uses the portal CSS variables (--ink, --vmm-navy, --brd …). */\n' + css + '\n');
// banner + tab bar (shared header)
const banner = view.slice(view.indexOf('<div class="hw-banner">'), view.indexOf('<div class="hw-tabs">'));
fs.writeFileSync(path.join(out, 'hw-banner.html'), banner.trim() + '\n');
console.log('wrote', starts.length, 'tab fragments,', total, 'live placeholders');
