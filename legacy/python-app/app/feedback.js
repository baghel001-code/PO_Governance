/* ════════════════════════════════════════════════════════════════════
   Feedback: every signed-in user can send feedback (button in the top
   bar); administrators see everything under Users → 💬 Feedback.
   ════════════════════════════════════════════════════════════════════ */
(function () {
  'use strict';
  function el(id) { return document.getElementById(id); }
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function when(iso) { return iso ? new Date(iso).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—'; }
  async function call(method, path, body) {
    var opts = { method: method, headers: { 'X-VMM': '1' }, cache: 'no-store' };
    if (body !== undefined) { opts.headers['Content-Type'] = 'application/json'; opts.body = JSON.stringify(body); }
    var r = await fetch('api/' + path, opts);
    if (r.status === 401) { location.replace('login.html'); throw new Error('Signed out'); }
    var d = await r.json().catch(function () { return {}; });
    if (!r.ok) throw new Error(d.error || ('HTTP ' + r.status));
    return d;
  }
  function toastMsg(m) { if (typeof window.toast === 'function') window.toast(m); }
  var KINDS = ['Problem', 'Suggestion', 'Question', 'Data looks wrong'];
  var SCREENS = ['New Request', 'View Recommendation', 'Requests', 'Approvals', 'History', 'How it works / Video', 'Data Setup', 'PO Assessment (Tool 2)', 'Sign-in / password', 'Other'];
  var STATUS = { 'new': ['New', '#1A4FAF', '#EAF0FB'], 'in progress': ['In progress', '#B45309', '#FEF3C7'], 'closed': ['Closed', '#047857', '#D1FAE5'] };
  function badge(st) { var s = STATUS[st] || STATUS['new']; return '<span style="padding:2px 9px;border-radius:9px;font-size:10.5px;font-weight:700;color:' + s[1] + ';background:' + s[2] + '">' + s[0] + '</span>'; }
  var css = 'padding:8px 10px;border:1.5px solid #D5DCEA;border-radius:8px;font-family:var(--font);font-size:12.5px;width:100%;box-sizing:border-box';

  var F = {};
  window.Feedback = F;

  function addButton() {
    var anchor = el('shared-user');
    if (!anchor || el('fb-btn')) return !!el('fb-btn');
    var b = document.createElement('button');
    b.id = 'fb-btn'; b.type = 'button'; b.textContent = '💬 Feedback';
    b.title = 'Report a problem or suggest an improvement';
    b.style.cssText = 'margin-left:auto;margin-right:10px;background:rgba(255,255,255,0.1);border:1px solid rgba(255,255,255,0.25);color:#fff;border-radius:16px;padding:5px 12px;font-size:11px;font-weight:700;cursor:pointer;font-family:var(--font);white-space:nowrap';
    b.onclick = F.open;
    anchor.parentNode.insertBefore(b, anchor);
    anchor.style.marginLeft = '0';
    return true;
  }
  var tries = 0;
  var t = setInterval(function () { if (addButton() || ++tries > 120) clearInterval(t); }, 500);

  F.open = async function () {
    var ov = el('fb-overlay');
    if (!ov) {
      ov = document.createElement('div'); ov.id = 'fb-overlay';
      ov.style.cssText = 'position:fixed;inset:0;z-index:9700;background:rgba(10,20,60,0.6);display:flex;align-items:center;justify-content:center;padding:16px;box-sizing:border-box';
      ov.addEventListener('click', function (e) { if (e.target === ov) ov.style.display = 'none'; });
      document.body.appendChild(ov);
    }
    ov.style.display = 'flex';
    ov.innerHTML = '<div style="background:#fff;border-radius:14px;max-width:620px;width:100%;max-height:90vh;overflow:auto;box-shadow:0 20px 60px rgba(0,0,0,.3);padding:22px 24px">' +
      '<div style="display:flex;align-items:center;gap:10px;margin-bottom:4px"><div style="font-size:17px;font-weight:800;color:#1A4FAF;flex:1">💬 Send feedback</div><button type="button" id="fb-x" style="border:none;background:none;font-size:18px;cursor:pointer;color:#6b7a99">✕</button></div>' +
      '<div style="font-size:12px;color:#6b7a99;margin-bottom:14px">Goes to the administrator. Include the article (GM) and colour if it is about a result.</div>' +
      '<div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">' +
        '<label style="font-size:11px;font-weight:700;color:#46526f">Type<select id="fb-kind" style="' + css + ';margin-top:4px">' + KINDS.map(function (k) { return '<option>' + k + '</option>'; }).join('') + '</select></label>' +
        '<label style="font-size:11px;font-weight:700;color:#46526f">Screen<select id="fb-screen" style="' + css + ';margin-top:4px">' + SCREENS.map(function (k) { return '<option>' + k + '</option>'; }).join('') + '</select></label>' +
      '</div>' +
      '<label style="display:block;font-size:11px;font-weight:700;color:#46526f;margin-top:10px">Article / colour (optional)<input id="fb-article" maxlength="120" placeholder="e.g. 1130153468 · NVY · Nov" style="' + css + ';margin-top:4px"></label>' +
      '<label style="display:block;font-size:11px;font-weight:700;color:#46526f;margin-top:10px">What happened, or what would help?<textarea id="fb-msg" rows="5" maxlength="2000" placeholder="Describe it in your own words" style="' + css + ';margin-top:4px;resize:vertical"></textarea></label>' +
      '<div style="font-size:11px;font-weight:700;color:#46526f;margin-top:10px">How useful is the tool so far? (optional)</div>' +
      '<div id="fb-rate" style="display:flex;gap:6px;margin-top:6px">' + [1, 2, 3, 4, 5].map(function (i) { return '<button type="button" data-v="' + i + '" style="width:40px;height:34px;border-radius:8px;border:1.5px solid #D5DCEA;background:#fff;font-weight:800;cursor:pointer;color:#46526f">' + i + '</button>'; }).join('') + '<span style="font-size:11px;color:#8a94ad;align-self:center;margin-left:6px">1 = not useful · 5 = very useful</span></div>' +
      '<div style="display:flex;gap:10px;margin-top:16px"><button type="button" id="fb-send" class="btn-pri" style="justify-content:center;flex:1">Send feedback</button><button type="button" id="fb-cancel" class="btn-sec" style="justify-content:center">Cancel</button></div>' +
      '<div id="fb-mine" style="margin-top:18px"></div></div>';
    var rating = null;
    Array.prototype.forEach.call(ov.querySelectorAll('#fb-rate button'), function (b) {
      b.onclick = function () {
        rating = +b.getAttribute('data-v');
        Array.prototype.forEach.call(ov.querySelectorAll('#fb-rate button'), function (x) { var on = +x.getAttribute('data-v') === rating; x.style.background = on ? '#1A4FAF' : '#fff'; x.style.color = on ? '#fff' : '#46526f'; x.style.borderColor = on ? '#1A4FAF' : '#D5DCEA'; });
      };
    });
    var cur = (window.S && S.role === 'planner') ? 'Data Setup' : 'New Request';
    el('fb-screen').value = cur;
    el('fb-x').onclick = el('fb-cancel').onclick = function () { ov.style.display = 'none'; };
    el('fb-send').onclick = async function () {
      var msg = el('fb-msg').value.trim();
      if (!msg) { el('fb-msg').focus(); el('fb-msg').style.borderColor = '#DC2626'; return; }
      el('fb-send').disabled = true;
      try {
        var r = await call('POST', 'feedback', { kind: el('fb-kind').value, screen: el('fb-screen').value, article: el('fb-article').value.trim(), message: msg, rating: rating, view: (window.S && S.role) || '' });
        toastMsg('Thank you — sent to the administrator (' + r.id + ')');
        ov.style.display = 'none';
      } catch (e) { toastMsg('⚠ ' + e.message); el('fb-send').disabled = false; }
    };
    try {
      var mine = await call('GET', 'feedback?mine=1');
      if (mine.items.length) {
        el('fb-mine').innerHTML = '<div style="font-size:11px;font-weight:800;color:#46526f;text-transform:uppercase;letter-spacing:.05em;margin-bottom:6px">Your earlier feedback</div>' +
          mine.items.slice(0, 8).map(function (f) {
            return '<div style="border:1px solid #EEF1F7;border-radius:8px;padding:8px 10px;margin-bottom:6px;font-size:12px"><div style="display:flex;gap:8px;align-items:center"><strong style="font-family:var(--mono);color:#E8006E">' + esc(f.id) + '</strong>' + badge(f.status) + '<span style="color:#8a94ad;font-size:11px;margin-left:auto">' + when(f.at) + '</span></div>' +
              '<div style="color:#46526f;margin-top:4px">' + esc(f.message).slice(0, 220) + '</div>' + (f.note ? '<div style="margin-top:4px;color:#047857"><strong>Admin:</strong> ' + esc(f.note) + '</div>' : '') + '</div>';
          }).join('');
      }
    } catch (e) { /* list is optional */ }
  };

  // ── admin roll-up (Users → 💬 Feedback) ──
  var A = { status: '', q: '' };
  F.setFilter = function (k, v) { A[k] = v; F.renderAdmin(el('us-body')); };
  F.renderAdmin = async function (body) {
    var d = await call('GET', 'feedback');
    var items = d.items.filter(function (f) {
      if (A.status && f.status !== A.status) return false;
      if (A.q) { var q = A.q.toLowerCase(); if ((f.message + ' ' + f.byName + ' ' + f.by + ' ' + f.article + ' ' + f.screen + ' ' + f.kind).toLowerCase().indexOf(q) < 0) return false; }
      return true;
    });
    var counts = { 'new': 0, 'in progress': 0, 'closed': 0 }; d.items.forEach(function (f) { counts[f.status] = (counts[f.status] || 0) + 1; });
    var rated = d.items.filter(function (f) { return f.rating; });
    var avg = rated.length ? (rated.reduce(function (a, f) { return a + f.rating; }, 0) / rated.length).toFixed(1) : '—';
    var head = '<div style="display:flex;flex-wrap:wrap;gap:10px;align-items:center;margin-bottom:12px">' +
      ['', 'new', 'in progress', 'closed'].map(function (s) {
        var on = A.status === s, label = s ? (STATUS[s][0] + ' (' + (counts[s] || 0) + ')') : ('All (' + d.items.length + ')');
        return '<button onclick="Feedback.setFilter(\'status\',\'' + s + '\')" style="padding:6px 12px;border-radius:8px;font-size:12px;font-weight:700;cursor:pointer;border:1.5px solid ' + (on ? '#1A4FAF' : '#D5DCEA') + ';background:' + (on ? '#1A4FAF' : '#fff') + ';color:' + (on ? '#fff' : '#46526f') + '">' + label + '</button>';
      }).join('') +
      '<input placeholder="Search text, person, article…" value="' + esc(A.q) + '" onchange="Feedback.setFilter(\'q\',this.value)" style="' + css + ';width:260px;margin-left:auto">' +
      '<span style="font-size:12px;color:#46526f">Average usefulness: <strong>' + avg + '</strong> / 5 (' + rated.length + ' ratings)</span></div>';
    if (!items.length) { body.innerHTML = head + '<div class="card" style="padding:40px;text-align:center;color:var(--ink3)">No feedback yet.</div>'; return; }
    body.innerHTML = head + items.map(function (f) {
      return '<div class="card" style="padding:14px 16px;margin-bottom:10px;border-left:4px solid ' + (STATUS[f.status] || STATUS['new'])[1] + '">' +
        '<div style="display:flex;flex-wrap:wrap;gap:10px;align-items:center"><strong style="font-family:var(--mono);color:#E8006E">' + esc(f.id) + '</strong>' + badge(f.status) +
        '<span style="font-size:12px;font-weight:700">' + esc(f.kind) + '</span><span style="font-size:12px;color:#46526f">· ' + esc(f.screen) + '</span>' + (f.article ? '<span style="font-size:12px;color:#46526f;font-family:var(--mono)">· ' + esc(f.article) + '</span>' : '') +
        (f.rating ? '<span style="font-size:12px;color:#46526f">· rated ' + f.rating + '/5</span>' : '') +
        '<span style="margin-left:auto;font-size:11.5px;color:#8a94ad">' + esc(f.byName || f.by) + ' · ' + when(f.at) + '</span></div>' +
        '<div style="margin:8px 0;font-size:13px;color:#15213f;white-space:pre-wrap">' + esc(f.message) + '</div>' +
        '<div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center">' +
          '<select id="fbs-' + esc(f.id) + '" style="' + css + ';width:150px">' + Object.keys(STATUS).map(function (s) { return '<option value="' + s + '"' + (s === f.status ? ' selected' : '') + '>' + STATUS[s][0] + '</option>'; }).join('') + '</select>' +
          '<input id="fbn-' + esc(f.id) + '" placeholder="Reply / note (the user sees it)" value="' + esc(f.note || '') + '" style="' + css + ';flex:1;min-width:220px">' +
          '<button class="btn-pri" style="font-size:12px;padding:7px 14px" onclick="Feedback.save(\'' + esc(f.id) + '\')">Save</button>' +
          (f.updatedBy ? '<span style="font-size:11px;color:#8a94ad">updated by ' + esc(f.updatedBy) + ' · ' + when(f.updatedAt) + '</span>' : '') +
        '</div></div>';
    }).join('');
  };
  F.save = async function (id) {
    try {
      await call('POST', 'feedback/' + encodeURIComponent(id) + '/status', { status: el('fbs-' + id).value, note: el('fbn-' + id).value.trim() });
      toastMsg('Saved ' + id);
      F.renderAdmin(el('us-body'));
    } catch (e) { toastMsg('⚠ ' + e.message); }
  };
})();
