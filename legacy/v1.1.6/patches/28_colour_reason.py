# v1.1.6 S5 (agreed 4 Nov 2026): single concept Watch List on the colour (colour share under 2%, or no sale in F3):
# before the request is saved and sent to the Category Head, the buyer must type "Why introduce this colour?".
# "Don't send" -> nothing is saved (no request, nothing for the approver). The reason is saved with the request and
# shown on the result card and to the approver (Approvals). It does not change the tool's decision.

rep("""function submitRequest() {""", """// v1.1.6 S5: required reason before a single concept Watch List colour request is sent (null = don't send)
function _askColourReason(r){
  return new Promise(function(resolve){
    var o=document.getElementById('v116-why'); if(o) o.remove();
    o=document.createElement('div'); o.id='v116-why';
    o.style.cssText='position:fixed;inset:0;z-index:9400;background:rgba(10,20,60,0.6);display:flex;align-items:center;justify-content:center;padding:16px';
    var e=function(s){return String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;');};
    o.innerHTML='<div role="dialog" aria-modal="true" aria-labelledby="v116-why-t" style="background:#fff;border-radius:14px;max-width:560px;width:100%;padding:22px 24px;box-shadow:0 20px 60px rgba(0,0,0,.3);font-family:var(--font)">'
      +'<div id="v116-why-t" style="font-size:17px;font-weight:700;color:#13254A">Why introduce this colour?</div>'
      +'<div style="font-size:12.5px;color:#4B5563;margin:6px 0 12px;line-height:1.55"><b>'+e(r.gm)+' · '+e(r.colour)+'</b> · delivery '+e(r.month)+' · '+e(fmt(r.qty))+' pcs<br>'+e(r.decisionReason||'')+'<br>The request goes to the Category Head only with your reason.</div>'
      +'<label for="v116-why-txt" style="font-size:11px;font-weight:700;color:#4B5563;text-transform:uppercase;letter-spacing:.05em">Your reason (required)</label>'
      +'<textarea id="v116-why-txt" rows="4" style="width:100%;box-sizing:border-box;margin-top:4px;padding:9px 11px;border:1.5px solid #C9CFDB;border-radius:8px;font:inherit;font-size:13px;resize:vertical"></textarea>'
      +'<div id="v116-why-err" style="display:none;color:#B42318;font-size:12px;margin-top:4px">Please type the reason before sending.</div>'
      +'<div style="display:flex;gap:10px;justify-content:flex-end;margin-top:16px">'
      +'<button type="button" id="v116-why-no" style="padding:8px 16px;border-radius:8px;border:1.5px solid #C9CFDB;background:#fff;color:#374151;font-weight:600;cursor:pointer;font-family:var(--font)">Don\\'t send</button>'
      +'<button type="button" id="v116-why-go" style="padding:8px 18px;border-radius:8px;border:none;background:#1A4FAF;color:#fff;font-weight:700;cursor:pointer;font-family:var(--font)">Send to Category Head</button></div></div>';
    document.body.appendChild(o);
    var t=document.getElementById('v116-why-txt'); t.focus();
    document.getElementById('v116-why-no').onclick=function(){ o.remove(); resolve(null); };
    document.getElementById('v116-why-go').onclick=function(){
      var v=t.value.trim();
      if(!v){ document.getElementById('v116-why-err').style.display='block'; t.focus(); return; }
      o.remove(); resolve(v.slice(0,1000));
    };
  });
}
function _needsColourReason(r){ return !!r && r.engine==='B' && r.decision==='Watch List'; }
function _colourReasonHtml(r){
  if(!r||!r.colourReason) return '';
  return '<div style="margin-top:10px;background:#F4F7FD;border:1px solid #D6DEEE;border-left:4px solid #1A4FAF;border-radius:0 8px 8px 0;padding:9px 13px;font-size:12px;line-height:1.6;color:#1A2B4A">'
    +'<strong style="font-size:10px;text-transform:uppercase;letter-spacing:.06em;color:#1A4FAF">Why introduce this colour? (buyer)</strong><br>'+String(r.colourReason).replace(/&/g,'&amp;').replace(/</g,'&lt;')+'</div>';
}
function submitRequest() {""", label="v116 S5 helpers")

# planner form
rep("""      result.reqDate=_todayStr();
      S.requests.push(result);
      renderResult(result);
      updateSummary();""", """      if(_needsColourReason(result)){
        const why=await _askColourReason(result);
        if(why===null){ S.reqCount--; document.getElementById('req-count').textContent=S.reqCount; toast('Not sent: nothing was saved for '+gm+' '+colour+'.'); throw {v116NotSent:true}; }
        result.colourReason=why;
      }
      result.reqDate=_todayStr();
      S.requests.push(result);
      renderResult(result);
      updateSummary();""", label="v116 S5 planner form")
rep("""    }catch(e){toast('Error: '+e.message);console.error(e);}
    document.getElementById('analyzing').classList.remove('show');
    document.getElementById('btn-sub').disabled=false;""", """    }catch(e){ if(!(e&&e.v116NotSent)){toast('Error: '+e.message);console.error(e);} }
    document.getElementById('analyzing').classList.remove('show');
    document.getElementById('btn-sub').disabled=false;""", label="v116 S5 planner form not sent")

# category form (the one buyers use)
rep("""      result.reqDate=_todayStr();
      S.requests.push(result);
      if (window.Shared && Shared.online) await Shared.saveRequest(result);""", """      if (_needsColourReason(result)) {
        var _why = await _askColourReason(result);
        if (_why === null) { S.reqCount--; toast('Not sent: nothing was saved for ' + gm + ' ' + colour + '.'); throw { v116NotSent: true }; }
        result.colourReason = _why;
      }
      result.reqDate=_todayStr();
      S.requests.push(result);
      if (window.Shared && Shared.online) await Shared.saveRequest(result);""", label="v116 S5 category form")
rep("""    } catch(e) { toast('Error: ' + e.message); console.error(e); }
    $g(m.analyzing).classList.remove('show');""", """    } catch(e) { if (!(e && e.v116NotSent)) { toast('Error: ' + e.message); console.error(e); } }
    $g(m.analyzing).classList.remove('show');""", label="v116 S5 category form not sent")

# the reason on the result card (under the decision reason / caution)
rep("""  html+=_laterPOCautionHtml(r);   // v1.2.7 caution (single concept, information only)""",
    """  html+=_laterPOCautionHtml(r);   // v1.2.7 caution (single concept, information only)
  html+=_colourReasonHtml(r);     // v1.1.6 S5""", label="v116 S5 reason on the card")
