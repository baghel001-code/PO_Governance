# v1.1.6 S3 + S4: ported from v1.2.7 (89_v127_caution.py). v1.1.4 had '60-Day' written in the heading; it now shows D.
# v1.2.7 items 3 + 4 (agreed 29 Oct 2026), single concept New PO request card only.
#  3. Step 4 heading / text: the window starts on Day 1 of the month AFTER delivery (wording only).
#  4. Caution note (Proposal 3, information only): when the asked qty is allowed but a LATER month's 1st is above
#     the limit (today's "Heads-up"), name the PO landing just before that 1st (latest first: F2 POs plus sent and
#     approved requests) and the qty that would be too much, e.g. "the Feb 2027 PO 5,000 will be 1,963 too much on
#     1 Mar 2027 - consider reducing it to 3,037". Worked out on the card from the request's own month-by-month
#     roll (r.chain) - the decision, Max Permissible, recommendation, PO Assessment and the saved request are NOT
#     changed. Shown only when the excess is 1,000 pcs or more and a later PO / request exists to name.
#     Step 6 in the step-by-step panel shows the working.

rep("""            <span style="font-size:11px;font-weight:700;color:#fff">STR Calculation — 60-Day Window from Day 1 of Delivery Month</span>""",
    """            <span style="font-size:11px;font-weight:700;color:#fff">STR Calculation — ${S.params.D}-Day Window from Day 1 of the Month after Delivery</span>""",
    label="v127 step 4 heading")
rep("""              From <strong>Day 1 of the Delivery Month</strong>, we look ahead <strong>D=${S.params.D} days</strong> and compute
              total colour demand (F4 × Mix%) across those days. This gives us the Daily Rate.
              STR tells us how many days the opening stock of the Delivery Month will last at that daily rate.""",
    """              From <strong>Day 1 of the month after delivery</strong> (the window start), we look ahead <strong>D=${S.params.D} days</strong> and compute
              total colour demand (F4 × Mix%) across those days. This gives us the Daily Rate.
              STR tells us how many days the stock at the window start will last at that daily rate.""",
    label="v127 step 4 text")

rep("""function _cautionBoxHtml(sc, idPrefix){""",
    """// v1.2.7: later PO caution (single concept New PO request card, information only - see dev/patches/89)
var LATER_PO_CAUTION_MIN = 1000;   // pcs: smaller excesses are not shown (agreed 29 Oct 2026)
function _laterPOCaution(r){
  if(!r||r.engine!=='B'||r.decision!=='Auto Allow'||!r.chain||r.chain.length<3||r.noPlan) return null;
  var ch=r.chain, D=parseFloat(S.params.D)||75, effT=(parseFloat(S.params.T1)||75)*(1+(parseFloat(S.params.strBuffer)||0)/100);
  var d1=(r.d1Idx!=null)?r.d1Idx:1;
  var rateAt=function(i){           // same rolling D-day plan rate as the engine's per-month STR
    var dem=0, left=D;
    for(var j=i;j<ch.length&&left>0;j++){ var md=daysInMonth(ch[j].y,ch[j].m), t=Math.min(md,left); dem+=ch[j].colPlan*(t/md); left-=t; }
    return dem>0?dem/D:0;
  };
  for(var k=d1+1;k<ch.length;k++){
    if(ch[k].status!=='Breach'||ch[k].str==null) continue;
    var rate=rateAt(k), limit=Math.floor(effT*rate), excess=Math.round(ch[k].openStock)-limit;   // limit rounded down as the Max Permissible
    if(excess<LATER_PO_CAUTION_MIN) return null;           // the first later breach only (as the Heads-up)
    var left=excess, items=[];
    for(var j=k-1;j>=d1&&left>0;j--){
      var q=ch[j].ePO||0; if(q<=0) continue;
      var ap=Math.min(q,(r.approvedPO&&r.approvedPO[ch[j].mkey])||0), cut=Math.min(q,left);
      items.push({mkey:ch[j].mkey,y:ch[j].y,m:ch[j].m,qty:q,requests:ap,cut:cut,keep:q-cut}); left-=cut;
    }
    if(!items.length) return null;                          // nothing later to name (the request itself is checked in step 5)
    var rows=[];
    for(var i=d1;i<=k;i++){ var rt=rateAt(i), lm=Math.floor(effT*rt);
      rows.push({mkey:ch[i].mkey,y:ch[i].y,m:ch[i].m,stock:ch[i].openStock,limit:lm,str:ch[i].str,excess:i===k?excess:null,
        po:(i<k&&ch[i].ePO>0)?ch[i].ePO:0}); }
    return {month:ch[k].mkey,y:ch[k].y,m:ch[k].m,stock:ch[k].openStock,limit:limit,str:ch[k].str,effT:effT,excess:excess,items:items,rows:rows};
  }
  return null;
}
function _laterPOName(it){ return monLabel(it.y,it.m)+(it.requests>0?(it.requests<it.qty?' POs and requests':' requests'):' PO'); }
function _laterPOText(c){
  var parts=c.items.map(function(it){ return 'the <strong>'+_laterPOName(it)+' '+fmt(Math.round(it.qty))+'</strong>'+(c.items.length>1?' (reduce to '+fmt(Math.round(it.keep))+')':''); });
  return 'With this request, '+parts.join(' and ')+' will be <strong>'+fmt(c.excess)+' too much</strong> on 1 '+monLabel(c.y,c.m)+
    ' ('+Math.round(c.str)+'d vs '+Math.round(c.effT)+'d limit).'+(c.items.length===1?' Consider reducing it to <strong>'+fmt(Math.round(c.items[0].keep))+'</strong>.':'');
}
function _laterPOCautionHtml(r){
  var c=_laterPOCaution(r); if(!c) return '';
  return '<div class="later-po-caution" style="margin-top:10px;background:#FFF6DA;border:1px solid #E9C46A;border-left:4px solid #B7791F;border-radius:0 8px 8px 0;padding:10px 14px;font-size:12px;color:#5A3A00;line-height:1.65">'+
    '<strong style="font-size:10px;text-transform:uppercase;letter-spacing:.06em;color:#7A4B00">Caution</strong><br>'+_laterPOText(c)+
    '<div style="font-size:10.5px;color:#6B5320;margin-top:4px">Information only · does not change the decision · working in Step 6 of the step-by-step computation</div></div>';
}
function _laterPOStep6Html(r){
  var c=_laterPOCaution(r); if(!c) return '';
  var td='padding:5px 10px;border-bottom:1px solid #e5e7eb;text-align:right;font-family:var(--mono)';
  var rows=c.rows.map(function(x,i){
    var bad=x.excess!=null;
    return '<tr style="background:'+(bad?'#FDECEC':(i===0?'#EAF0FB':'#fff'))+'"><td style="padding:5px 10px;border-bottom:1px solid #e5e7eb">1 '+monLabel(x.y,x.m)+(i===0?' <span style="font-size:9px;color:#1A4FAF;font-weight:700">← Window Start</span>':'')+'</td>'+
      '<td style="'+td+'">'+fmt(Math.round(x.stock))+'</td><td style="'+td+'">'+fmt(Math.round(x.limit))+'</td>'+
      '<td style="'+td+';color:'+(bad?'#DC2626':'#00873E')+';font-weight:700">'+(x.str!=null?Math.round(x.str)+'d':'—')+'</td>'+
      '<td style="'+td+';color:#DC2626;font-weight:700">'+(bad?fmt(x.excess):'—')+'</td></tr>';
  }).join('');
  var lines=c.items.map(function(it){ return _laterPOName(it)+' '+fmt(Math.round(it.qty))+' − '+fmt(Math.round(it.cut))+' = <strong>'+fmt(Math.round(it.keep))+'</strong>'; }).join('<br>');
  return '<div style="margin-top:12px;margin-bottom:4px;border:1px solid var(--brd);border-radius:10px;overflow:hidden">'+
    '<div style="background:#B7791F;padding:8px 14px;display:flex;align-items:center;gap:8px"><span style="background:rgba(255,255,255,.15);color:#fff;font-size:10px;font-weight:700;padding:2px 8px;border-radius:4px">STEP 6</span>'+
    '<span style="font-size:11px;font-weight:700;color:#fff">Caution check (information only) — stock on every 1st after the window start, with this request</span></div>'+
    '<div style="padding:12px 14px;background:#FFFBEF"><div style="overflow-x:auto"><table style="width:100%;border-collapse:collapse;font-size:10px;min-width:520px"><thead><tr style="background:#B7791F22">'+
    '<th style="padding:6px 10px;text-align:left;color:#7A4B00">1st of month</th><th style="padding:6px 10px;text-align:right;color:#7A4B00">Stock on the 1st</th>'+
    '<th style="padding:6px 10px;text-align:right;color:#7A4B00">Limit ('+(Math.round(c.effT*100)/100)+'d × daily rate)</th><th style="padding:6px 10px;text-align:right;color:#7A4B00">STR on the 1st</th><th style="padding:6px 10px;text-align:right;color:#7A4B00">Too much</th></tr></thead><tbody>'+rows+'</tbody></table></div>'+
    '<div style="font-family:var(--mono);font-size:11.5px;line-height:1.9;margin-top:10px;background:#fff;border:1px solid #e5e7eb;border-radius:8px;padding:10px 14px">'+
    'Too much on 1 '+monLabel(c.y,c.m)+' = Stock on the 1st − Limit = '+fmt(Math.round(c.stock))+' − '+fmt(Math.round(c.limit))+' = <strong style="color:#DC2626">'+fmt(c.excess)+'</strong><br>'+
    'Named to the POs / requests landing just before that 1st (latest first):<br>'+lines+'</div>'+
    '<div style="font-size:11px;color:var(--ink2);margin-top:8px">Information only: this request\\'s decision and Max Permissible (Step 5) do not change. Smaller excesses (under '+fmt(LATER_PO_CAUTION_MIN)+' pcs) are not shown.</div></div></div>';
}
function _cautionBoxHtml(sc, idPrefix){""", label="v127 later PO caution helpers")

# card: the caution box under the reason; the Heads-up sentence is not repeated on the card when the caution names it
rep("""    <span style="color:var(--ink2)">${r.decisionReason||'—'}</span>
  </div>`;
""", """    <span style="color:var(--ink2)">${(_laterPOCaution(r)?String(r.decisionReason||'').replace(/ \\[Heads-up:[^\\]]*\\]/,''):r.decisionReason)||'—'}</span>
  </div>`;
  html+=_laterPOCautionHtml(r);   // v1.2.7 caution (single concept, information only)
""", label="v127 caution on the card")

# step-by-step panel: Step 6 after Step 5
rep("""      </div><!-- end detail panel -->
    `;""", """        ${_laterPOStep6Html(r)}
      </div><!-- end detail panel -->
    `;""", label="v127 step 6 in the step-by-step panel")
