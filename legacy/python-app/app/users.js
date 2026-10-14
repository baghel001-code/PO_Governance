/* ════════════════════════════════════════════════════════════════════
   Approvals (category heads) and Users & Security (admin)
   ════════════════════════════════════════════════════════════════════ */
(function () {
  'use strict';
  function el(id) { return document.getElementById(id); }
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function n(v) { return v == null || v === '' ? '—' : Math.round(v).toLocaleString('en-IN'); }
  function when(iso) { return iso ? new Date(iso).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—'; }
  function ymd(d) { return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0'); }
  async function call(method, path, body) {
    var opts = { method: method, headers: { 'X-VMM': '1' }, cache: 'no-store' };
    if (body !== undefined) { opts.headers['Content-Type'] = 'application/json'; opts.body = JSON.stringify(body); }
    var r = await fetch('api/' + path, opts);
    if (r.status === 401) { location.replace('login.html'); throw new Error('Signed out'); }
    var d = await r.json().catch(function () { return {}; });
    if (!r.ok) throw new Error(d.error || ('HTTP ' + r.status));
    return d;
  }
  var H1 = function (icon, title, sub, right) {
    return '<div style="margin-bottom:18px;display:flex;align-items:flex-start;gap:12px;flex-wrap:wrap"><div style="flex:1;min-width:260px"><div class="pg-title">' + icon + ' ' + title + '</div>' +
      '<div style="height:3px;width:60px;background:linear-gradient(90deg,var(--vmm-pink),var(--vmm-cyan));border-radius:2px;margin:5px 0 6px"></div><div class="pg-sub">' + sub + '</div></div>' + (right || '') + '</div>';
  };
  function pills(list, active, fn) {
    return '<div style="display:flex;gap:6px;flex-wrap:wrap;margin-bottom:12px">' + list.map(function (t) {
      var on = t[0] === active;
      return '<button onclick="' + fn + '(\'' + t[0] + '\')" style="padding:7px 14px;border-radius:8px;font-size:12px;font-weight:700;cursor:pointer;font-family:var(--font);border:1.5px solid ' +
        (on ? 'var(--vmm-pink)' : 'var(--brd2)') + ';background:' + (on ? 'var(--vmm-pink)' : '#fff') + ';color:' + (on ? '#fff' : 'var(--ink2)') + '">' + t[1] + '</button>';
    }).join('') + '</div>';
  }
  function table(head, rows, empty) {
    if (!rows.length) return '<div class="card" style="padding:40px;text-align:center;color:var(--ink3)">' + empty + '</div>';
    return '<div class="card" style="padding:0;overflow:hidden"><div style="overflow-x:auto;max-height:70vh"><table class="sum-tbl" style="width:100%"><thead><tr>' +
      head.map(function (h) { return '<th style="position:sticky;top:0;z-index:1">' + h + '</th>'; }).join('') + '</tr></thead><tbody>' + rows.join('') + '</tbody></table></div></div>';
  }
  var inputCss = 'padding:7px 10px;border:1.5px solid var(--brd2);border-radius:7px;font-family:var(--font);font-size:12px';

  // ══════════════════════ APPROVALS ══════════════════════
  var A = { tab: 'pending', data: null };
  window.Approvals = A;
  A.show = async function () {
    var v = el('view-approvals');
    v.innerHTML = H1('✅', 'Approvals', 'Tool 1 requests that came out as <strong>Watch List</strong> wait here for the category head of that MC. Approve (optionally a smaller quantity) or reject with a reason. The buyer sees your decision in their Requests list.') +
      '<div id="ap-tabs"></div><div id="ap-body"><div style="padding:40px;text-align:center;color:var(--ink3)">Loading…</div></div>';
    try { A.data = await call('GET', 'approvals?days=120'); } catch (e) { el('ap-body').innerHTML = '<div class="card" style="padding:30px;color:#DC2626">' + esc(e.message) + '</div>'; return; }
    Shared.setApprovalCount(A.data.pending.length);
    A.render();
  };
  A.setTab = function (t) { A.tab = t; A.render(); };
  A.render = function () {
    el('ap-tabs').innerHTML = pills([['pending', '⏳ Waiting for me (' + A.data.pending.length + ')'], ['decided', '📜 Decided (' + A.data.decided.length + ')']], A.tab, 'Approvals.setTab');
    var body = el('ap-body');
    if (A.tab === 'pending') {
      if (!A.data.pending.length) { body.innerHTML = '<div class="card" style="padding:48px;text-align:center;color:var(--ink3)"><div style="font-size:36px">🎉</div>Nothing waiting for your approval.</div>'; return; }
      body.innerHTML = A.data.pending.map(function (r) {
        var i = r.info || {};
        return '<div class="card" style="padding:16px 18px;margin-bottom:12px;border-left:4px solid #D97706" id="ap-' + esc(r.reqId) + '">' +
          '<div style="display:flex;flex-wrap:wrap;gap:18px;align-items:flex-start">' +
            '<div style="min-width:170px"><div style="font-family:var(--mono);font-weight:800;color:var(--vmm-pink)">' + esc(r.reqId) + '</div><div style="font-size:11px;color:var(--ink3)">' + when(r.savedAt) + '</div><div style="font-size:12px;margin-top:4px">by <strong>' + esc(r.requestedBy) + '</strong></div></div>' +
            '<div style="flex:1;min-width:260px;font-size:12.5px;line-height:1.7">' +
              '<strong style="font-family:var(--mono)">' + esc(r.gm) + '</strong> · ' + esc(r.colour) + ' · ' + esc(i.mcDesc || '') + ' · ₹' + esc(i.mrp || '—') + ' · ' + esc(i.brand || '') + ' · ' + esc(i.div || '') +
              '<br>Delivery <strong>' + esc(r.month) + '</strong> · Requested <strong>' + n(r.qty) + '</strong> units · ' + esc(r.engine === 'B' ? 'Single Concept MRP' : 'Multi Concept · ' + (r.optionType || '')) +
              (r.quartile_ ? ' · ' + esc(r.quartile_) + (r.percentile != null ? ' (' + r.percentile + 'th pct)' : '') : '') +
              '<div style="margin-top:6px;padding:8px 10px;background:#FFFBEB;border-radius:7px;color:#92400E;font-size:12px"><strong>Why Watch List:</strong> ' + esc(r.decisionReason || '—') + '</div></div>' +
            '<div style="min-width:250px">' +
              '<label style="font-size:10px;font-weight:700;color:var(--ink3);text-transform:uppercase">Approve quantity</label>' +
              '<input type="number" min="0" max="' + (r.qty || 0) + '" value="' + (r.qty || 0) + '" id="apq-' + esc(r.reqId) + '" style="' + inputCss + ';width:100%;margin:4px 0 8px;font-family:var(--mono);font-weight:700">' +
              '<textarea id="apc-' + esc(r.reqId) + '" rows="2" placeholder="Comment (required to reject)" style="' + inputCss + ';width:100%;resize:vertical"></textarea>' +
              '<div style="display:flex;gap:8px;margin-top:8px"><button class="btn-pri" style="flex:1;justify-content:center;font-size:12px;padding:8px" onclick="Approvals.decide(\'' + esc(r.reqId) + '\',\'approved\')">✓ Approve</button>' +
              '<button class="btn-sec" style="flex:1;justify-content:center;font-size:12px;padding:8px;color:#DC2626;border-color:#FECACA" onclick="Approvals.decide(\'' + esc(r.reqId) + '\',\'rejected\')">✕ Reject</button></div>' +
            '</div>' +
          '</div></div>';
      }).join('');
      return;
    }
    var rows = A.data.decided.map(function (r) {
      var a = r.approval, i = r.info || {};
      return '<tr><td style="font-family:var(--mono);font-weight:700;white-space:nowrap">' + esc(r.reqId) + '</td><td>' + esc(r.requestedBy) + '</td><td style="font-family:var(--mono)">' + esc(r.gm) + '</td><td>' + esc(r.colour) + '</td><td style="font-size:11px">' + esc(i.mcDesc || '') + '</td>' +
        '<td style="font-family:var(--mono)">' + n(r.qty) + '</td><td>' + Shared.approvalBadge(r) + '</td><td style="font-family:var(--mono);font-weight:700">' + (a.status === 'approved' ? n(a.qty) : '0') + '</td>' +
        '<td style="font-size:11px;color:var(--ink2);max-width:260px">' + esc(a.comment || '') + '</td><td style="white-space:nowrap">' + when(a.at) + '</td></tr>';
    });
    body.innerHTML = table(['Request ID', 'Requested by', 'GM', 'Colour', 'MC', 'Requested', 'Decision', 'Approved qty', 'Comment', 'Decided at'], rows, 'No decisions yet.');
  };
  A.decide = async function (rid, decision) {
    var qty = parseInt(el('apq-' + rid).value, 10), comment = el('apc-' + rid).value.trim();
    if (decision === 'approved' && (isNaN(qty) || qty <= 0)) { toast('Enter the quantity to approve (more than 0)'); el('apq-' + rid).focus(); return; }
    if (decision === 'rejected' && !comment) { toast('Please add a reason for rejecting'); el('apc-' + rid).focus(); return; }
    try {
      await call('POST', 'approvals/' + encodeURIComponent(rid), { decision: decision, qty: qty, comment: comment });
      toast(decision === 'approved' ? 'Approved ' + rid : 'Rejected ' + rid);
      if (Shared.refreshRequests) await Shared.refreshRequests();
      A.show();
    } catch (e) { toast('⚠ ' + e.message); }
  };

  // ══════════════════════ USERS & SECURITY (admin) ══════════════════════
  var U = { tab: 'users', data: null, q: '', role: '', edit: false, auditQ: '', auditFrom: '', auditTo: '' };
  window.Users = U;
  var ROLE_COL = { admin: '#7C3AED', planner: '#E8006E', cathead: '#1A4FAF', buyer: '#0891B2' };
  function roleChips(roles, labels) {
    return (roles || []).map(function (r) {
      return '<span style="display:inline-block;margin:1px 3px 1px 0;padding:1px 8px;border-radius:9px;font-size:10px;font-weight:700;color:' + ROLE_COL[r] + ';border:1px solid ' + ROLE_COL[r] + '55;background:' + ROLE_COL[r] + '10">' + esc(labels[r] || r) + '</span>';
    }).join('');
  }
  U.show = async function () {
    el('view-users').innerHTML = H1('👥', 'Users &amp; Security', 'Sign-in accounts, roles and passwords. New buyers and category heads are created automatically from the SCR products file; everyone starts with the default password and must change it at first sign-in.') +
      '<div id="us-tabs"></div><div id="us-body"></div>';
    U.render();
  };
  U.setTab = function (t) { U.tab = t; U.render(); };
  U.render = async function () {
    el('us-tabs').innerHTML = pills([['users', '👥 Users'], ['scr', '📥 Import from SCR'], ['feedback', '💬 Feedback'], ['audit', '🧾 Audit log'], ['backups', '💾 Backups']], U.tab, 'Users.setTab');
    var body = el('us-body');
    body.innerHTML = '<div style="padding:40px;text-align:center;color:var(--ink3)">Loading…</div>';
    try {
      if (U.tab === 'users') { U.data = await call('GET', 'users'); renderUsers(body); }
      else if (U.tab === 'scr') { renderScr(body, await call('GET', 'users/scr')); }
      else if (U.tab === 'audit') { await renderAudit(body); }
      else if (U.tab === 'feedback') { if (window.Feedback) await window.Feedback.renderAdmin(body); }
      else { renderBackups(body, await call('GET', 'backups')); }
    } catch (e) { body.innerHTML = '<div class="card" style="padding:30px;color:#DC2626">' + esc(e.message) + '</div>'; }
  };

  // ── users list ──
  function renderUsers(body) {
    var labels = {}; U.data.roles.forEach(function (r) { labels[r.id] = r.label; });
    var q = U.q.toLowerCase();
    var list = U.data.users.filter(function (u) {
      if (U.role === 'inactive') return !u.active;
      if (U.role && u.roles.indexOf(U.role) < 0) return false;
      return !q || (u.name + ' ' + u.email).toLowerCase().indexOf(q) >= 0;
    });
    var counts = {}; U.data.users.forEach(function (u) { if (u.active) u.roles.forEach(function (r) { counts[r] = (counts[r] || 0) + 1; }); });
    var rows = list.map(function (u) {
      var status = !u.active ? '<span style="color:#6B7280">Inactive</span>' : u.locked ? '<span style="color:#DC2626;font-weight:700">🔒 Locked</span>' : u.mustChange ? '<span style="color:#B45309">Not signed in yet</span>' : '<span style="color:#00873E">Active</span>';
      return '<tr style="' + (u.active ? '' : 'opacity:.55') + '"><td><strong>' + esc(u.name) + '</strong><div style="font-size:11px;color:var(--ink3)">' + esc(u.email) + '</div></td>' +
        '<td>' + roleChips(u.roles, labels) + '</td><td>' + status + '</td><td style="font-family:var(--mono)">' + (u.mcCount || '—') + '</td>' +
        '<td style="white-space:nowrap;font-size:11.5px">' + (u.lastLogin ? when(u.lastLogin) : '—') + '</td><td style="font-size:11px;color:var(--ink3)">' + esc({ scr: 'SCR file', manual: 'Added by admin', bulk: 'Bulk add', system: 'Built-in' }[u.source] || u.source || '') + '</td>' +
        '<td style="white-space:nowrap"><button class="btn-sec" style="font-size:11px;padding:4px 10px" onclick="Users.editUser(\'' + esc(u.email) + '\')">Edit</button> ' +
        '<button class="btn-sec" style="font-size:11px;padding:4px 10px" onclick="Users.reset(\'' + esc(u.email) + '\')">Reset password</button>' +
        (u.locked ? ' <button class="btn-sec" style="font-size:11px;padding:4px 10px" onclick="Users.update(\'' + esc(u.email) + '\',{unlock:true})">Unlock</button>' : '') + '</td></tr>';
    });
    body.innerHTML =
      '<div style="display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin-bottom:12px">' +
        '<input id="us-q" placeholder="Search name or email…" value="' + esc(U.q) + '" style="' + inputCss + ';min-width:240px">' +
        '<select id="us-role" style="' + inputCss + '"><option value="">All roles</option>' + U.data.roles.map(function (r) { return '<option value="' + r.id + '"' + (U.role === r.id ? ' selected' : '') + '>' + r.label + ' (' + (counts[r.id] || 0) + ')</option>'; }).join('') +
        '<option value="inactive"' + (U.role === 'inactive' ? ' selected' : '') + '>Inactive</option></select>' +
        '<span style="font-size:11.5px;color:var(--ink2)">' + list.length + ' of ' + U.data.users.length + ' users</span>' +
        '<button class="btn-sec" style="margin-left:auto;font-size:12px;padding:7px 14px" onclick="Users.bulk()">Bulk add…</button>' +
        '<button class="btn-pri" style="font-size:12px;padding:7px 14px" onclick="Users.editUser(null)">+ Add user</button>' +
      '</div><div id="us-edit"></div>' +
      table(['User', 'Roles', 'Status', 'MCs (SCR)', 'Last sign-in', 'Source', ''], rows, 'No users match.');
    el('us-q').oninput = function () { var p = this.selectionStart; U.q = this.value; renderUsers(body); var x = el('us-q'); x.focus(); x.setSelectionRange(p, p); };
    el('us-role').onchange = function () { U.role = this.value; renderUsers(body); };
    if (U.edit !== undefined && U.edit !== false) drawEdit();
  }
  U.editUser = function (email) { U.edit = email; drawEdit(); };
  function drawEdit() {
    var box = el('us-edit'); if (!box) return;
    if (U.edit === false || U.edit === undefined) { box.innerHTML = ''; return; }
    var u = U.edit ? U.data.users.filter(function (x) { return x.email === U.edit; })[0] : { email: '', name: '', roles: ['buyer'], active: true };
    var isNew = !U.edit;
    box.innerHTML = '<div class="card" style="padding:16px 18px;margin-bottom:12px;border-left:4px solid var(--vmm-pink)">' +
      '<div style="font-weight:700;margin-bottom:10px">' + (isNew ? 'Add a user' : 'Edit ' + esc(u.name)) + '</div>' +
      '<div style="display:flex;flex-wrap:wrap;gap:12px;align-items:flex-end">' +
        '<div><div style="font-size:10px;font-weight:700;color:var(--ink3);text-transform:uppercase">Email</div><input id="ue-email" value="' + esc(u.email) + '" ' + (isNew ? '' : 'disabled') + ' style="' + inputCss + ';min-width:280px"></div>' +
        '<div><div style="font-size:10px;font-weight:700;color:var(--ink3);text-transform:uppercase">Name</div><input id="ue-name" value="' + esc(u.name) + '" style="' + inputCss + ';min-width:200px"></div>' +
        '<div><div style="font-size:10px;font-weight:700;color:var(--ink3);text-transform:uppercase;margin-bottom:5px">Roles</div>' +
          U.data.roles.map(function (r) { return '<label style="margin-right:12px;font-size:12px;cursor:pointer"><input type="checkbox" class="ue-role" value="' + r.id + '"' + (u.roles.indexOf(r.id) >= 0 ? ' checked' : '') + '> ' + r.label + '</label>'; }).join('') + '</div>' +
        (isNew ? '' : '<label style="font-size:12px;cursor:pointer"><input type="checkbox" id="ue-active"' + (u.active ? ' checked' : '') + '> Active (can sign in)</label>') +
        '<div style="display:flex;gap:8px;margin-left:auto">' +
        (isNew || (Shared.user && u.email === Shared.user.email) ? '' : '<button class="btn-sec" style="font-size:12px;padding:7px 14px;color:#DC2626;border-color:#FCA5A5" onclick="Users.remove()">Delete user</button>') +
        '<button class="btn-sec" style="font-size:12px;padding:7px 14px" onclick="Users.editUser(false)">Cancel</button>' +
        '<button class="btn-pri" style="font-size:12px;padding:7px 14px" onclick="Users.saveEdit()">' + (isNew ? 'Add user' : 'Save') + '</button></div>' +
      '</div>' +
      (isNew ? '<div style="font-size:11px;color:var(--ink3);margin-top:8px">They sign in with password <strong style="font-family:var(--mono)">' + esc(U.data.defaultPassword) + '</strong> and must change it straight away. <strong>Planner</strong> = upload data, parameters, Tool 2. <strong>Admin</strong> = also manage users.</div>' : '') +
      '</div>';
    if (isNew) el('ue-email').focus();
  }
  U.saveEdit = async function () {
    var roles = Array.prototype.filter.call(document.querySelectorAll('.ue-role'), function (c) { return c.checked; }).map(function (c) { return c.value; });
    try {
      if (!U.edit) {
        await call('POST', 'users', { email: el('ue-email').value.trim(), name: el('ue-name').value.trim(), roles: roles });
        toast('User added — starting password ' + U.data.defaultPassword);
      } else {
        await call('POST', 'users/' + encodeURIComponent(U.edit) + '/update', { name: el('ue-name').value.trim(), roles: roles, active: el('ue-active').checked });
        toast('Saved');
      }
      U.edit = false; U.render();
    } catch (e) { toast('⚠ ' + e.message); }
  };
  U.remove = async function () {
    var email = U.edit; if (!email) return;
    if (!confirm('Delete the login ' + email + '?\n\nThey can no longer sign in. Their past requests and approvals stay in History.\nIf they are in the SCR products file, the next SCR upload creates the login again.\n\nTo block someone without deleting, untick "Active" instead.')) return;
    try { await call('POST', 'users/' + encodeURIComponent(email) + '/delete', {}); toast('Deleted ' + email); U.edit = false; U.render(); } catch (e) { toast('⚠ ' + e.message); }
  };
  U.update = async function (email, changes) {
    try { await call('POST', 'users/' + encodeURIComponent(email) + '/update', changes); toast('Done'); U.render(); } catch (e) { toast('⚠ ' + e.message); }
  };
  U.reset = async function (email) {
    if (!confirm('Reset the password of ' + email + ' to the default (' + U.data.defaultPassword + ')?\n\nThey are signed out everywhere and must choose a new password at next sign-in.')) return;
    try { await call('POST', 'users/' + encodeURIComponent(email) + '/reset-password', {}); toast('Password reset to ' + U.data.defaultPassword); U.render(); } catch (e) { toast('⚠ ' + e.message); }
  };
  U.bulk = function () {
    var box = el('us-edit');
    box.innerHTML = '<div class="card" style="padding:16px 18px;margin-bottom:12px;border-left:4px solid var(--vmm-pink)">' +
      '<div style="font-weight:700;margin-bottom:6px">Bulk add users</div>' +
      '<div style="font-size:11.5px;color:var(--ink2);margin-bottom:8px">One per line: <code>email, name, role</code> — role is one of admin, planner, cathead, buyer (use + for several, e.g. <code>planner+buyer</code>). You can paste straight from Excel (tab-separated works too).</div>' +
      '<textarea id="ub-text" rows="6" style="' + inputCss + ';width:100%;font-family:var(--mono)" placeholder="ravi.kumar@vishalwholesale.co.in, Ravi Kumar, planner"></textarea>' +
      '<div style="display:flex;gap:8px;margin-top:8px;justify-content:flex-end"><button class="btn-sec" style="font-size:12px;padding:7px 14px" onclick="Users.editUser(false)">Cancel</button><button class="btn-pri" style="font-size:12px;padding:7px 14px" onclick="Users.saveBulk()">Add all</button></div></div>';
  };
  U.saveBulk = async function () {
    var users = el('ub-text').value.split(/\n/).map(function (l) { return l.trim(); }).filter(Boolean).map(function (l) {
      var p = l.split(/\t|,|;/).map(function (x) { return x.trim(); });
      return { email: p[0], name: p[1] || '', roles: (p[2] || 'buyer').toLowerCase().replace(/category ?head/g, 'cathead').split(/[+|\/ ]+/) };
    });
    try {
      var d = await call('POST', 'users/bulk', { users: users });
      toast('Added ' + d.added.length + (d.errors.length ? ' · ' + d.errors.length + ' skipped' : ''));
      if (d.errors.length) alert('Skipped:\n' + d.errors.join('\n'));
      U.edit = false; U.render();
    } catch (e) { toast('⚠ ' + e.message); }
  };

  // ── SCR import ──
  function renderScr(body, d) {
    if (!d.hasScr) {
      body.innerHTML = '<div class="card" style="padding:30px;line-height:1.7">No SCR products file on the server yet. Upload it in <strong>Data Setup → SCR Products / Cat Head Mapping</strong> (or inside the ZIP bundle). Logins for its buyers and category heads are then created automatically, and you can fine-tune which columns are used here.</div>';
      return;
    }
    var map = (d.mapping && d.mapping.columns) || {};
    var cols = d.analysis.columns;
    var newPeople = d.people.filter(function (p) { return !p.exists; });
    body.innerHTML =
      '<div class="card" style="padding:16px 18px;margin-bottom:12px">' +
        '<div style="font-weight:700;margin-bottom:4px">Which columns hold whose email?</div>' +
        '<div style="font-size:11.5px;color:var(--ink2);margin-bottom:10px">Found ' + cols.length + ' email column(s) in the current SCR file (' + n(d.scrRows) + ' rows). Division heads are skipped for now. Existing users are never removed or demoted.</div>' +
        table(['Column in SCR file', 'Example value', 'Distinct emails', 'Treat as'], cols.map(function (c) {
          var cur = map[c.column] || c.guess;
          return '<tr><td><strong>' + esc(c.column) + '</strong></td><td style="font-size:11px;color:var(--ink3)">' + esc(c.example) + '</td><td style="font-family:var(--mono)">' + c.distinctEmails + '</td>' +
            '<td><select class="scr-map" data-col="' + esc(c.column) + '" style="' + inputCss + '">' +
            [['buyer', 'Buyer'], ['cathead', 'Category Head'], ['skip', 'Skip (e.g. Division Head)']].map(function (o) { return '<option value="' + o[0] + '"' + (cur === o[0] ? ' selected' : '') + '>' + o[1] + '</option>'; }).join('') + '</select></td></tr>';
        }), 'No column with email addresses found in the SCR file.') +
        '<div style="display:flex;gap:10px;align-items:center;margin-top:12px"><span style="font-size:12px;color:var(--ink2)">With the current settings: <strong>' + d.people.length + '</strong> people, <strong>' + newPeople.length + '</strong> new.</span>' +
        '<button class="btn-pri" style="margin-left:auto;font-size:12px;padding:8px 16px" onclick="Users.saveScr()">Save &amp; create logins</button></div>' +
      '</div>' +
      table(['Person', 'Role(s) from SCR', 'MCs', 'Login'], d.people.map(function (p) {
        return '<tr><td><strong>' + esc(p.name || '—') + '</strong><div style="font-size:11px;color:var(--ink3)">' + esc(p.email) + '</div></td><td>' + roleChips(p.roles, { buyer: 'Buyer', cathead: 'Category Head' }) + '</td><td style="font-family:var(--mono)">' + p.mcs + '</td>' +
          '<td>' + (p.exists ? '<span style="color:#00873E">✓ exists</span>' : '<span style="color:#B45309">will be created</span>') + '</td></tr>';
      }), 'Nobody found with these settings.');
  }
  U.saveScr = async function () {
    var columns = {};
    document.querySelectorAll('.scr-map').forEach(function (s) { columns[s.dataset.col] = s.value; });
    try {
      var d = await call('POST', 'users/scr/mapping', { mapping: { columns: columns } });
      toast('Created ' + d.result.added.length + ' login(s), updated ' + d.result.updated.length);
      U.render();
    } catch (e) { toast('⚠ ' + e.message); }
  };

  // ── audit ──
  async function renderAudit(body) {
    if (!U.auditFrom) { var t = new Date(); U.auditTo = ymd(t); U.auditFrom = ymd(new Date(t.getFullYear(), t.getMonth(), t.getDate() - 6)); }
    var d = await call('GET', 'audit?from=' + U.auditFrom + '&to=' + U.auditTo + '&q=' + encodeURIComponent(U.auditQ));
    var LBL = { login: '🔓 Signed in', 'login-fail': '⚠ Failed sign-in', locked: '🔒 Locked out', logout: 'Signed out', 'password-change': '🔑 Changed password', 'password-reset': '🔑 Password reset',
                'user-add': '👤 User added', 'user-update': '👤 User changed', upload: '📂 Upload', delete: '📂 File cleared', restore: '↩ Made current', 'restore-upload': '↩ Upload made current',
                request: '✏️ Tool 1 request', approval: '✅ Approval', params: '⚙ Parameters', 'run-saved': '📋 Tool 2 run', 'clear-requests': '🧹 Requests cleared', 'scr-sync': '📥 SCR sync', 'scr-mapping': '📥 SCR columns', backup: '💾 Backup' };
    var rows = d.items.map(function (e) {
      var bad = /fail|locked/.test(e.action);
      return '<tr><td style="white-space:nowrap">' + when(e.at) + '</td><td>' + esc(e.who) + '</td><td style="white-space:nowrap;' + (bad ? 'color:#DC2626;font-weight:700' : '') + '">' + esc(LBL[e.action] || e.action) + '</td><td style="font-size:11px;color:var(--ink2);max-width:520px;word-break:break-word">' + esc(e.detail) + '</td></tr>';
    });
    body.innerHTML = '<div class="card" style="padding:12px 14px;margin-bottom:12px;display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end">' +
      '<div><div style="font-size:10px;font-weight:700;color:var(--ink3);text-transform:uppercase">From</div><input type="date" id="au-from" value="' + U.auditFrom + '" style="' + inputCss + '"></div>' +
      '<div><div style="font-size:10px;font-weight:700;color:var(--ink3);text-transform:uppercase">To</div><input type="date" id="au-to" value="' + U.auditTo + '" style="' + inputCss + '"></div>' +
      '<input id="au-q" placeholder="Search user, action, ID…" value="' + esc(U.auditQ) + '" style="' + inputCss + ';min-width:240px">' +
      '<button class="btn-pri" style="font-size:12px;padding:7px 14px" onclick="Users.auditGo()">Show</button>' +
      '<span style="font-size:11.5px;color:var(--ink2);margin-left:auto">' + d.total + ' event(s)' + (d.total > d.items.length ? ' (latest ' + d.items.length + ' shown)' : '') + '</span></div>' +
      table(['When', 'Who', 'What', 'Details'], rows, 'Nothing recorded in this range.');
    el('au-q').onkeydown = function (e) { if (e.key === 'Enter') U.auditGo(); };
  }
  U.auditGo = function () { U.auditFrom = el('au-from').value; U.auditTo = el('au-to').value; U.auditQ = el('au-q').value; U.render(); };

  // ── backups ──
  function renderBackups(body, d) {
    var rows = d.items.map(function (b) {
      return '<tr><td style="font-family:var(--mono)">' + esc(b.name) + '</td><td>' + when(b.at) + '</td><td>' + (b.bytes > 1048576 ? (b.bytes / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round(b.bytes / 1024)) + ' KB') + '</td></tr>';
    });
    body.innerHTML = '<div class="card" style="padding:14px 16px;margin-bottom:12px;display:flex;gap:12px;align-items:center;flex-wrap:wrap"><div style="font-size:12px;color:var(--ink2);line-height:1.6">A copy of all shared data is saved automatically every night after ' + String(d.hour).padStart(2, '0') + ':00, and kept for ' + d.keepDays + ' days, in <code>' + esc(d.dir) + '</code> on the server. Copy that folder to another drive or share regularly — a backup on the same disk won\'t survive a disk failure.</div>' +
      '<button class="btn-pri" style="margin-left:auto;font-size:12px;padding:8px 16px" onclick="Users.backupNow()">💾 Back up now</button></div>' +
      table(['Backup file', 'Made at', 'Size'], rows, 'No backups yet.');
  }
  U.backupNow = async function () {
    toast('Backing up…');
    try { var d = await call('POST', 'backups', {}); toast('Saved ' + d.name); U.render(); } catch (e) { toast('⚠ ' + e.message); }
  };
})();
