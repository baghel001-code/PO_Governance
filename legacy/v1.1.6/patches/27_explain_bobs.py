# v1.1.6 S8 + B1: "How was this number worked out?" side panel (single concept only) and the BOBs sibling list.
# Ported from v1.2.5 (80_v125_explain.py, 82_v125_bobs.py) + app/explain.js (multi concept part left out).
# DISPLAY ONLY: the engines keep every number they had; they also hand back the month-by-month roll they already
# computed, so app/explain.js can show the steps and the Excel with formulas.

rep("""  <script src="feedback.js"></script>""", """  <script src="feedback.js"></script>
  <script src="explain.js"></script>""", label="v116 explain script")

# _coreColourSTRFast (Recommendation): keep the pre-roll months and the plan inputs of each chain month
rep("""  var runStock=opStock;
  for(var i=0;;i++){
    var ym=addMonths(currY,currM,i);
    var mkey=mmyyyy(ym.y,ym.m);
    if(mkey===dmKey) break;
    if(i>24) break;
    var ePO=_ePOWithCarry(f2RawMap, mkey, prevMonthCarry);
    var fc=forecastByMon[mkey]||0;
    var totalDays=daysInMonth(ym.y,ym.m);
    var remainingDays=i===0?(countToday?_daysLeft(totalDays,todayDate):(totalDays-todayDate)):totalDays;
    var cp=fc*mixPct*(remainingDays/totalDays);
    runStock=Math.max(runStock+ePO-cp,0);
  }""", """  var runStock=opStock, pre=[];   // v1.1.6: pre = the same months, kept for the side panel (display only)
  for(var i=0;;i++){
    var ym=addMonths(currY,currM,i);
    var mkey=mmyyyy(ym.y,ym.m);
    if(mkey===dmKey) break;
    if(i>24) break;
    var ePO=_ePOWithCarry(f2RawMap, mkey, prevMonthCarry);
    var fc=forecastByMon[mkey]||0;
    var totalDays=daysInMonth(ym.y,ym.m);
    var remainingDays=i===0?(countToday?_daysLeft(totalDays,todayDate):(totalDays-todayDate)):totalDays;
    var cp=fc*mixPct*(remainingDays/totalDays);
    var _open=runStock;
    runStock=Math.max(runStock+ePO-cp,0);
    pre.push({mkey:mkey,y:ym.y,m:ym.m,fc:fc,days:totalDays,counted:remainingDays,po:ePO,sale:cp,open:_open,close:runStock});
  }""", label="v116 explain reco pre-roll kept")
rep("""    chain.push({y:ym2.y,m:ym2.m,mkey:mkey2,openStock:openStock,poIn:ePO2,colPlan:colPlan,closeStock:closeStock});""",
    """    chain.push({y:ym2.y,m:ym2.m,mkey:mkey2,openStock:openStock,poIn:ePO2,colPlan:colPlan,closeStock:closeStock,fc:fc2,demandDays:demandDays,totalDays:totalDays2});""",
    label="v116 explain reco chain inputs")
rep("""            chain:chain,winDays:winDays,d1Idx:d1Idx};""", """            chain:chain,winDays:winDays,d1Idx:d1Idx,pre:pre};""", label="v116 explain reco pre (no data)")
rep("""          chain:chain,winDays:winDays,d1Idx:d1Idx};""", """          chain:chain,winDays:winDays,d1Idx:d1Idx,pre:pre};""", label="v116 explain reco pre")

# runEngineB (New PO request): keep the pre-roll months too
rep("""  // Pre-roll from today's month to delivery month
  let runStock=opStock;
  for(let i=0;;i++){""", """  // Pre-roll from today's month to delivery month
  let runStock=opStock;
  const preRoll=[];   // v1.1.6: the same months, kept for the side panel (display only)
  for(let i=0;;i++){""", label="v116 explain Tool 1 pre-roll list")
rep("""    const remainingDays=i===0?_daysLeft(totalDays,todayDate):totalDays;
    const cp=effectiveMixPct!==null?fc*effectiveMixPct*(remainingDays/totalDays):0;
    runStock=Math.max(runStock+ePO-cp,0);
  }

  // Build forward chain from DM""", """    const remainingDays=i===0?_daysLeft(totalDays,todayDate):totalDays;
    const cp=effectiveMixPct!==null?fc*effectiveMixPct*(remainingDays/totalDays):0;
    const _open=runStock;
    runStock=Math.max(runStock+ePO-cp,0);
    preRoll.push({mkey,y:cy,m:cm,fc,days:totalDays,counted:remainingDays,po:ePO,sale:cp,open:_open,close:runStock});
  }

  // Build forward chain from DM""", label="v116 explain Tool 1 pre-roll kept")
rep("""    chain,d1Idx,winStockStart,windowDemand,windowDailyRate,artSTR,hasBreach,maxPermQty,mixFailed:false,hasDailyF3,forecastByMon,existingPO,prevMonthCarryNote,prevMonthCarry,futureRiskMonths};""",
    """    chain,d1Idx,winStockStart,windowDemand,windowDailyRate,artSTR,hasBreach,maxPermQty,mixFailed:false,hasDailyF3,forecastByMon,existingPO,prevMonthCarryNote,prevMonthCarry,futureRiskMonths,preRoll,effMix:effectiveMixPct};""",
    label="v116 explain Tool 1 pre-roll returned")

# _runB: keep the roll of each colour x month and the 45-day share (display only)
rep("""      if(!cs) return {mon:mon,pend:pend,appr:appr,rec:0,str:null,dr:0,action:'No data'};
      return {mon:mon,pend:pend,appr:appr,rec:cs.rec,str:cs.str,dr:cs.dr,eff:cs.eff,action:cs.action};""",
    """      if(!cs) return {mon:mon,pend:pend,appr:appr,rec:0,str:null,dr:0,action:'No data'};
      return {mon:mon,pend:pend,appr:appr,rec:cs.rec,str:cs.str,dr:cs.dr,eff:cs.eff,action:cs.action,cs:cs,mixUsed:mixRatio};""",
    label="v116 explain reco roll kept per month")
rep("""    return {col:col,stk:stk,s45:s45,mix:mix,stkmix:stkmix,strRos:strRos,watchList:watchList,watchReason:watchReason,mdata:mdata};""",
    """    return {col:col,stk:stk,s45:s45,mix:mix,mixRatio:mixRatio,stkmix:stkmix,strRos:strRos,watchList:watchList,watchReason:watchReason,mdata:mdata};""",
    label="v116 explain reco 45-day share kept")

# _paintRec (single concept): header with the window, Excel (all lines), clickable quantities
rep("""      html+='<div class="rec-sec">Colour-wise Stock &amp; PO Recommendation — '+res.rows.length+' colours · ranked Stock High to Low · EffT='+res.eff+'d</div>';""",
    """      html+='<div class="rec-sec" style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">Colour-wise Stock &amp; PO Recommendation — '+res.rows.length+' colours · ranked Stock High to Low · window '+(S.params.D||75)+' days from the month after delivery · limit '+(Math.round(eff*100)/100)+' days'+
        '<button type="button" id="xs-all" style="margin-left:auto;background:#00873E;border:none;border-radius:6px;color:#fff;font-size:10.5px;font-weight:700;cursor:pointer;padding:5px 12px">⬇ Download Excel (all lines)</button></div>'+
        '<div style="font-size:11px;color:#5A6480;margin:-2px 0 8px">Click any quantity, Hold or Watch List to see how it was worked out.</div>';""",
    label="v116 explain single reco header")
rep("""            html+='<td><span title="'+r.watchReason+'" style="background:#FFF7ED;color:#92400E;padding:2px 8px;border-radius:5px;font-weight:700;font-size:10px;font-family:var(--mono);cursor:help;border-bottom:1px dotted #92400E">&#128065; Watch List</span></td>';""",
    """            html+='<td><span data-xs="'+i+'|'+r.mdata.indexOf(md)+'" title="'+r.watchReason+' Click to see why." style="background:#FFF7ED;color:#92400E;padding:2px 8px;border-radius:5px;font-weight:700;font-size:10px;font-family:var(--mono);cursor:pointer;border-bottom:1px dotted #92400E">&#128065; Watch List</span></td>';""",
    label="v116 explain single reco Watch List clickable")
rep("""            html+='<td><span title="'+tip+'" style="background:'+ab+';color:'+ac+';padding:2px 8px;border-radius:5px;font-weight:700;font-size:10px;font-family:var(--mono);cursor:help;border-bottom:1px dotted '+ac+'">'+(md.rec>0?_fmt(md.rec):md.action)+'</span></td>';""",
    """            html+='<td><span data-xs="'+i+'|'+r.mdata.indexOf(md)+'" title="'+tip+'. Click to see how." style="background:'+ab+';color:'+ac+';padding:2px 8px;border-radius:5px;font-weight:700;font-size:10px;font-family:var(--mono);cursor:pointer;border-bottom:1px dotted '+ac+'">'+(md.rec>0?_fmt(md.rec):md.action)+'</span></td>';""",
    label="v116 explain single reco quantity clickable")

# B1: BOBs sibling list (82_v125_bobs.py, unchanged)
rep("""function routeRequest(gm,colour) {""", """// v1.1.6 B1: compact list of a BOBs request's sibling colours (saved with the request; the full BSR rows are not)
function _bobsSibList(eng){
  if(!eng||eng.optionType!=='BOBs'||!eng.siblings||!eng.siblings.length) return null;
  var rk=new Map(); (eng.sibRanked||[]).forEach(function(s){ rk.set(s.row,s.q); });
  var f3st=function(r){ var k=Object.keys(r).filter(function(h){ return /first\\s*3\\s*week/i.test(h)&&/(thru|through|st\\s*%|sell)/i.test(h); })[0]; return k!=null&&String(r[k]).trim()!==''?num(r[k]):null; };
  var out=eng.siblings.map(function(r){
    return {gm:String(col(r,'Generic Material','generic material','GENERIC MATERIAL')).trim(), colour:String(col(r,'Color','Colour','COLOR','COLOUR')).trim(),
      wof:getWOF(r), q:rk.has(r)?rk.get(r):null, pdstr:getPDSTR(r), first3:num(col(r,'First 3 Weeks Sale','FIRST 3 WEEKS SALE')), first3st:f3st(r),
      season:String(col(r,'SEASON','Season')||'').trim()};
  });
  var qo={Q1:0,Q2:1,Q3:2,Q4:3};
  out.sort(function(a,b){ return ((a.q?qo[a.q]:9)-(b.q?qo[b.q]:9))||(b.wof-a.wof); });
  return out.slice(0,60);
}
function routeRequest(gm,colour) {""", label="v116 B1 sibling list helper")
rep("""        result = Object.assign({ id:S.reqCount, pfx:pfx, gm:gm, colour:colour, month:month, qty:qty, engine:'A', optionType:route.optionType, info:info }, eng2);""",
    """        result = Object.assign({ id:S.reqCount, pfx:pfx, gm:gm, colour:colour, month:month, qty:qty, engine:'A', optionType:route.optionType, info:info }, eng2);
        result.bobsSiblings=_bobsSibList(eng2);""", label="v116 B1 keep list (category form)")
rep("""        result={id:S.reqCount,gm,colour,month,qty,engine:'A',optionType:route.optionType,info,...eng};""",
    """        result={id:S.reqCount,gm,colour,month,qty,engine:'A',optionType:route.optionType,info,...eng};
        result.bobsSiblings=_bobsSibList(eng);""", label="v116 B1 keep list (planner form)")
