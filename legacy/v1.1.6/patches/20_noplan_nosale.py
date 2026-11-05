# v1.1.6 D1 (agreed 4 Nov 2026): single concept, no F4 plan for the Brand x MC x MRP x Flag in the requirement
# window (D days from the 1st of the month after delivery) ->
#   New PO request: Hold - "No Plan Available" (was Auto Allow), checked first.
#   PO Assessment:  new status "No Plan - Review" (was Retain), no quantity suggested; the planner decides.
# Multi concept / new options: unchanged (user: no multi concept changes).
# Helpers and wording taken from v1.2.6 (85_v126_noplan.py), restricted to single concept.
# v1.1.6 D3: single concept, plan exists but the MC has no F3 sale at all (colour share cannot be worked out)
# -> Watch List (was Auto Allow "No forecast data"), same as a colour share under 2%.

rep("""function runEngineB(gm,colour,deliveryMonth,proposedQty,info) {""",
    """// v1.1.6 D1: "No Plan Available" (single concept)
function _windowMonths(month,days){
  var p=String(month||'').match(/^(\\d{2})\\.(\\d{4})$/); if(!p||+p[1]<1||+p[1]>12) return [];
  var y=+p[2], m=+p[1]+1; if(m>12){m=1;y++;}
  var left=parseInt(days,10)||0, out=[];
  while(left>0&&out.length<24){ out.push(String(m).padStart(2,'0')+'.'+y); left-=new Date(y,m,0).getDate(); m++; if(m>12){m=1;y++;} }
  return out;
}
function _noPlanReason(info,ks){
  var i=info||{}, lab=function(k){ return monLabel(+k.slice(3),+k.slice(0,2)); };
  var span=ks.length?(lab(ks[0])+(ks.length>1?' – '+lab(ks[ks.length-1]):'')):'';
  return 'No Plan Available: there is no F4 plan for '+(i.brand||'')+' · MC '+(i.mcCd||'')+' · ₹'+(i.mrp||'')+' · '+(i.flag||'')+' in the requirement window ('+span+'). Nothing else was checked. Ask the planning team to add the plan.';
}
// PO Assessment: same check on the pre-built F4 index; a PO two or more months late is checked from last month
function _paNoPlan(forecastByMon,dm,noShift){
  var p=String(dm||'').match(/^(\\d{2})\\.(\\d{4})$/), now=new Date();
  if(!noShift&&p&&(+p[2])*12+(+p[1])<now.getFullYear()*12+(now.getMonth()+1)-1){
    var py=now.getFullYear(), pm=now.getMonth(); if(pm<1){pm=12;py--;} dm=String(pm).padStart(2,'0')+'.'+py;
  }
  var ks=_windowMonths(dm,parseFloat(S.params.D)||75); if(!ks.length) return null;
  var tot=0; ks.forEach(function(k){ tot+=num((forecastByMon||{})[k]||0); });
  return tot>0?null:ks;
}
function runEngineB(gm,colour,deliveryMonth,proposedQty,info) {""", label="v116 D1 helpers")

rep("""  const dmParts=deliveryMonth.match(/^(\\d{2})\\.(\\d{4})$/);
  if(!dmParts) return {decision:'Cannot Validate',decisionReason:'Invalid delivery month.',mixPct:0,opStock:0,chain:[],mixFailed:false,recQty:null,artSTR:null};
  const dmM=parseInt(dmParts[1]),dmY=parseInt(dmParts[2]);
""", """  const dmParts=deliveryMonth.match(/^(\\d{2})\\.(\\d{4})$/);
  if(!dmParts) return {decision:'Cannot Validate',decisionReason:'Invalid delivery month.',mixPct:0,opStock:0,chain:[],mixFailed:false,recQty:null,artSTR:null};
  const dmM=parseInt(dmParts[1]),dmY=parseInt(dmParts[2]);
""", label="v116 D1 New PO request (dm)")

# v1.1.6 D1 (review fix): the check reads F4 exactly as runEngineB does (same row matcher, MM.YYYY / date / serial
# month headers), right after the row matcher is defined, so it is still checked before colour mix and STR.
rep("""    const cOk   = !withColour || colour_val===rColour.toLowerCase();
    return mcOk && mrpOk && bOk && fOk && cOk;
  }

  // ── F1: Op Stock — SUM all rows matching KEY-3 (colour level) ──""",
    """    const cOk   = !withColour || colour_val===rColour.toLowerCase();
    return mcOk && mrpOk && bOk && fOk && cOk;
  }

  // v1.1.6 D1: no F4 plan in the requirement window -> Hold, checked first (F4 read as in the F4 block below)
  {
    const _f4m={};
    if(S.files.f4){
      S.files.f4.forEach(r=>{ if(!matchRow(r,false)) return;
        Object.keys(r).forEach(h=>{ const hStr=String(h).trim();
          if(isMMYYYY(hStr)){ const v=num(r[h]); if(v&&!isNaN(v)) _f4m[hStr]=(_f4m[hStr]||0)+v; return; }
          const mmyy=dateToMMYYYY(h)||dateToMMYYYY(r[h]); if(mmyy){ const v=num(r[h]); if(v&&!isNaN(v)) _f4m[mmyy]=(_f4m[mmyy]||0)+v; } }); });
      if(!Object.keys(_f4m).length) S.files.f4.forEach(r=>{ if(!matchRow(r,false)) return;
        Object.keys(r).forEach(h=>{ const n=num(h); if(n>32874&&n<109574){ const mmyy=dateToMMYYYY(n); const v=num(r[h]); if(mmyy&&v) _f4m[mmyy]=(_f4m[mmyy]||0)+v; } }); });
    }
    const _np=_paNoPlan(_f4m,deliveryMonth,true);
    if(_np) return {decision:'Hold — Don\\'t Raise',decisionReason:_noPlanReason(info,_np),noPlan:true,
      mixPct:0,opStock:0,chain:[],mixFailed:false,recQty:null,artSTR:null,maxPermQty:0};
  }

  // ── F1: Op Stock — SUM all rows matching KEY-3 (colour level) ──""", label="v116 D1 New PO request (engine F4 read)")

rep("""  if(windowDemand===0){
    return {
      decision:'Auto Allow',
      decisionReason:`No forecast data found for MC CD ${rMC} MRP ${rMRP} in F4. `+
        `Cannot compute STR — treated as Auto Allow. `+
        `[DEBUG] forecastByMon keys: ${JSON.stringify(Object.keys(forecastByMon))} | `+
        `F4 rows total: ${S.files.f4?S.files.f4.length:0} | `+
        `F4 matched rows: ${S.files.f4?S.files.f4.filter(r=>matchRow(r,false)).length:0} | `+
        `effectiveMixPct: ${effectiveMixPct!==null?(effectiveMixPct*100).toFixed(4)+'%':'null'} | `+
        `rFlag: ${rFlag} | rMC: ${rMC} | rMRP: ${rMRP}`,""",
    """  if(windowDemand===0){
    // v1.1.6 D3: the plan exists (D1 is checked first) but there is no F3 sale for this MC, so the colour share
    // cannot be worked out -> Watch List, same as a colour share under 2%
    return {
      decision:'Watch List',
      decisionReason:`No sale in F3 for MC CD ${rMC} MRP ${rMRP} (last ${S.params.mixWindowDays||45} days), so the colour share cannot be worked out. Treated like a colour share under ${mixT}%.`,""",
    label="v116 D3 no sale -> Watch List")

rep("""  const mk4=_infoKey4(info);
  const colKey=mk4+'-'+String(colour).trim().toLowerCase();

  // F1: Op Stock — O(1) lookup (already summed across all matching rows at index build time)""",
    """  const mk4=_infoKey4(info);
  const colKey=mk4+'-'+String(colour).trim().toLowerCase();
  // v1.1.6 D1: no F4 plan in the requirement window -> "No Plan - Review" (no qty suggested), checked first
  const _npm=_paNoPlan(idx.f4Fcst.get(mk4)||{},deliveryMonth);
  if(_npm) return {decision:'No Plan – Review',decisionReason:_noPlanReason(info,_npm)+' The planner decides on this PO.',
    noPlan:true,artSTR:null,recQty:null,mixPct:0,opStock:idx.f1ColIdx.get(colKey)||0};

  // F1: Op Stock — O(1) lookup (already summed across all matching rows at index build time)""",
    label="v116 D1 PO Assessment")
