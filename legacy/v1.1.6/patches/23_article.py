# v1.1.6 S6 + S7: ported from v1.2.5 (81_v125_article.py); the pop-up uses a small own message box (no VMM.modal in v1.1.4).
# v1.2.5 change 3 (agreed 28 Oct 2026): Generic Article description and vendor everywhere, and
# "not in the Article Master -> the New PO request stops" (single and multi concept). Nothing is saved then.

rep("""  if (window.Shared && Shared.user && Shared.user.restricted) {
    var _si = resolveGM(gm) || {};""", """  if (!_inArticleMaster(gm)) {
    var _amMsg = S.files.artmaster ? 'Article ' + gm + ' is not in the Article Master. Ask the planning team to add it; this request cannot proceed. Nothing is saved.'
      : 'The Article Master is not uploaded, so article ' + gm + ' cannot be checked. Ask the planning team to upload it; this request cannot proceed. Nothing is saved.';
    _v116Msg('Article not in Article Master', _amMsg);
    return;
  }
  if (window.Shared && Shared.user && Shared.user.restricted) {
    var _si = resolveGM(gm) || {};""", label="v1.2.5 article: stop when not in the Article Master (Tool 1)")

rep("""function resolveGM(code) {""", """// v1.1.6: simple message box (title + plain text), closed with OK, the X or Esc
function _v116Msg(title,text){
  var o=document.getElementById('v116-msg'); if(o) o.remove();
  o=document.createElement('div'); o.id='v116-msg';
  o.style.cssText='position:fixed;inset:0;z-index:9500;background:rgba(10,20,60,0.55);display:flex;align-items:center;justify-content:center;padding:16px';
  var e=function(s){return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;');};
  o.innerHTML='<div role="dialog" aria-modal="true" style="background:#fff;border-radius:14px;max-width:520px;width:100%;padding:22px 24px;box-shadow:0 20px 60px rgba(0,0,0,.3);font-family:var(--font)">'
    +'<div style="font-size:17px;font-weight:700;color:#13254A;margin-bottom:10px">'+e(title)+'</div>'
    +'<div style="font-size:13.5px;line-height:1.55;color:#1A2B4A">'+e(text)+'</div>'
    +'<div style="text-align:right;margin-top:18px"><button type="button" id="v116-msg-ok" style="padding:8px 22px;border-radius:8px;border:none;background:#1A4FAF;color:#fff;font-weight:700;cursor:pointer;font-family:var(--font)">OK</button></div></div>';
  document.body.appendChild(o);
  var close=function(){ o.remove(); document.removeEventListener('keydown',esc); };
  var esc=function(ev){ if(ev.key==='Escape') close(); };
  document.addEventListener('keydown',esc);
  o.addEventListener('click',function(ev){ if(ev.target===o) close(); });
  document.getElementById('v116-msg-ok').onclick=close; document.getElementById('v116-msg-ok').focus();
}
// v1.2.5: Generic Materials of the Article Master (a New PO request stops when the article is not there)
function _inArticleMaster(code){
  var src=S.files.artmaster; if(!src||!src.length) return false;
  if(_idx.amSrc!==src||_idx.amKey!==src.length||!_idx.amSet){   // v1.1.6: a new upload (same row count) rebuilds it
    _idx.amSet=new Set(); _idx.amKey=src.length; _idx.amSrc=src;
    src.forEach(function(r){ var k=String(col(r,'Generic Material','generic material','GENERIC MATERIAL','GenMat')).trim().replace(/\\.0$/,''); if(k) _idx.amSet.add(k); });
  }
  return _idx.amSet.has(String(code).trim().replace(/\\.0$/,''));
}
// v1.2.5: description (grey, under the code) and vendor (hover) of a Generic Article
function _artLine(r){
  var i=(r&&r.info)||{}, d=i.artDesc||'', v=i.vendorName||'';
  if(!d&&!v) return '';
  var e=function(s){return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/"/g,'&quot;');};
  return '<div class="rh-art" title="'+e((d?d:'')+(v?' · Vendor: '+v:''))+'" style="font-size:11px;color:#6B7280;margin-top:1px;cursor:help">'+e(d||'—')+(v?' <span style="color:#9AA4B5">· '+e(v)+'</span>':'')+'</div>';
}
function resolveGM(code) {""", label="v1.2.5 article: helpers")

rep("""        <div class="rh-sub">${r.info?`${r.info.brand||'NON-PL'} · ${r.info.div||'—'} · ${r.info.mcDesc||'—'} · ₹${r.info.mrp||'—'} · ${r.info.flag||'—'}`:''}</div>""",
    """        ${_artLine(r)}<div class="rh-sub">${r.info?`${r.info.brand||'NON-PL'} · ${r.info.div||'—'} · ${r.info.mcDesc||'—'} · ₹${r.info.mrp||'—'} · ${r.info.flag||'—'}`:''}</div>""",
    label="v1.2.5 article: description on the result card")
rep("""      + '<td style="font-family:var(--mono);font-size:11px">' + r.gm + '</td>'""",
    """      + '<td style="font-family:var(--mono);font-size:11px" title="' + String(((r.info||{}).artDesc||'') + (((r.info||{}).vendorName) ? ' · Vendor: ' + r.info.vendorName : '')).replace(/"/g,'&quot;') + '">' + r.gm + (((r.info||{}).artDesc) ? '<div style="font-family:var(--font);font-size:10px;color:#6B7280;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:200px">' + String(r.info.artDesc).replace(/</g,'&lt;') + '</div>' : '') + '</td>'""",
    label="v1.2.5 article: description in Today's requests")
rep("""  if(missing.length){toast(`Please upload required files first (missing: ${missing.join(', ')})`);return;}""",
    """  if(missing.length){toast(`Please upload required files first (missing: ${missing.join(', ')})`);return;}
  if(!_inArticleMaster(gm)){_v116Msg('Article not in Article Master',S.files.artmaster?'Article '+gm+' is not in the Article Master. Ask the planning team to add it; this request cannot proceed. Nothing is saved.':'The Article Master is not uploaded, so article '+gm+' cannot be checked. Ask the planning team to upload it; this request cannot proceed. Nothing is saved.');return;}""",
    label="v1.2.5 article: stop when not in the Article Master (planner form)")
