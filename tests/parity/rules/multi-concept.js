// Multi concept rule, Step 1 (Nov 2026) — the JavaScript reference of DecisionEngine/Tool1/Tool1EngineA.cs.
//
// The parity harnesses run the ORIGINAL page code (legacy/v1.1.6/app/index.html). The multi concept rule was changed on
// purpose after v1.1.6, so the harnesses that run runEngineA (tool1, tool2) load this file on top of the page:
//   * runEngineA  — replaced (appended after the page source: the later definition wins, as in the page itself);
//   * REPLACEMENTS — exact text changes in the page functions that paint the result (each must match exactly once):
//       - the quartile labels (only Q1 is allowed now: Q2 is no longer "Healthy", Q3 no longer "Watch");
//       - the "Step 4 — Border Zone Check" block is shown only for a result that went through the border zone check
//         (borderTolUsed set) — the Step 1 rule has no border zone.
//
// The rule (business request of 19 Nov 2026, "Step 1 – To be done now"), for articles NOT in the Universe Master:
//   * peer group: same Brand, MC, MRP and Flag (Brand added), WOF >= gate, current seasons with fallback — as before;
//     a peer's brand = its BSR "Brand Type", else the Article Master / Universe Master / F2 brand (unknown = kept);
//   * True Repeat ranked on AVG PERDAY SELL THRU%: Q1 (top 25%) -> Allow, Q2 / Q3 / Q4 -> Block; no Watch List, no border zone;
//   * BOBs (new colour of an existing option): all other colours with WOF >= gate must rank Q1 to Allow, else Block;
//     no other colours, or none with WOF >= gate -> Allow as today; other colours but no peer pool -> Allow;
//   * unchanged: WOF below the gate -> Allow, no peer pool -> Allow, absolute block (< 1%/day with WOF >= 4w), new articles -> Allow.
//
// Step 2 ("To be done next", implemented 20 Nov 2026): a Q1 is allowed only when its per-day sell-thru is at least the minimum
// of the option's Entry / Mid / Fashion type (S.files.emf, Brand x MC x Flag x MRP; Entry 2.0%, Mid 1.5%, Fashion 1.5%/day);
// below -> Block; not in the mapping -> Hold "Planner has not maintained this MC-MRP in the master." BOBs: checked on the
// oldest other colour (most weeks on floor). The flag NON-BOGO is the mapping's "Normal". Tool 2: the Hold -> Cannot Validate.
'use strict';

/* eslint-disable no-undef */
function runEngineA(gm, colour, optionType, info) {
  const {wof:wofGate,peer:peerMin}=S.params;
  const mcCd=info?.mcCd||'', mrp=info?.mrp||'', brand=String(info?.brand||'').trim(), flag=(info?.flag||'').trim();

  function peerBrand(r){
    const own=String(col(r,'Brand Type','BRAND TYPE','brand type')).trim();
    if(own) return own;
    const rGm=String(r['Generic Material']||r['GENERIC MATERIAL']||'').trim();
    const pi=_idx.resolveMap.get(rGm);
    return pi?String(pi.brand||'').trim():'';
  }
  function buildPool(src, seasonSet){
    if(!src) return [];
    const nMC=String(mcCd).trim().replace(/\.0$/,'');
    const nMRP=String(mrp).trim().replace(/\.0$/,'');
    const nFlag=flag.toLowerCase();
    const nBrand=brand.toLowerCase();
    return src.filter(r=>{
      const rMC=col(r,'MC CD','MC_CD','mc cd').trim().replace(/\.0$/,'');
      const rMRP=col(r,'MRP','mrp').trim().replace(/\.0$/,'');
      const rArt=col(r,'Article Type','article type','BOGO/NON-BOGO','Flag','flag').trim().toLowerCase();
      const wof=num(col(r,'WEEK\n ON\nFLOOR','WEEK ON FLOOR','WOF','wof'));
      if(!(rMC===nMC&&rMRP===nMRP&&(!nFlag||rArt===nFlag||rArt==='')&&wof>=wofGate)) return false;
      if(nBrand){
        const rBrand=peerBrand(r).toLowerCase();
        if(rBrand&&rBrand!==nBrand) return false;
      }
      if(!seasonSet) return true;
      const rGm=String(r['Generic Material']||r['GENERIC MATERIAL']||'').trim();
      const peerInfo=_idx.resolveMap.get(rGm);
      if(!peerInfo || !peerInfo.season) return false;
      const s=String(peerInfo.season).trim().toUpperCase().replace(/[-_]/g,'').replace(/\s+/g,'');
      return seasonSet.has(s);
    });
  }

  _buildResolveIndex();
  const curSet=new Set(S.params.seasonCurrent||[]), fbSet=new Set(S.params.seasonFallback||[]);
  let bsrSrc=S.files.bsr, bsrLabel='CY BSR ('+(S.params.seasonCurrent||[]).join('+')+')';
  let peerPool=buildPool(bsrSrc, curSet);
  if(peerPool.length<peerMin){
    const fbPool=buildPool(bsrSrc, fbSet);
    if(fbPool.length>=peerMin){ bsrLabel='Fallback BSR ('+(S.params.seasonFallback||[]).join('+')+')'; peerPool=fbPool; }
    else if(fbPool.length>0){ bsrLabel='Fallback BSR ('+(S.params.seasonFallback||[]).join('+')+', limited pool)'; peerPool=fbPool; }
  }
  const totalBSR=bsrSrc?bsrSrc.filter(r=>col(r,'MC CD','MC_CD','mc cd').trim()===String(mcCd).trim()&&String(col(r,'MRP','mrp')).trim().replace('.0','')=== String(mrp).trim().replace('.0','')).length:0;
  const cleanPool=peerPool;
  const peerAvg=cleanPool.length?cleanPool.reduce((s,r)=>s+getPDSTR(r),0)/cleanPool.length:0;
  const base={optionType,bsrLabel,peerPool,cleanPool,totalBSR,peerAvg};
  const colourOf=r=>String(col(r,'Color','Colour','COLOR','COLOUR','colour','color')).trim();

  // Step 2 — the Entry / Mid / Fashion minimum (DecisionEngine/Common/EmfMapping.cs)
  const EMF_MIN={Entry:0.02,Mid:0.015,Fashion:0.015};
  const NOT_MAINTAINED='Planner has not maintained this MC-MRP in the master.';
  const strip0=v=>{const t=String(v==null?'':v).trim(); return t.endsWith('.0')?t.slice(0,-2):t;};
  const flagKey=f=>{const u=String(f||'').trim().toUpperCase(); return (u==='NON-BOGO'||u==='NON BOGO'||u==='NONBOGO'||u==='NON_BOGO')?'NORMAL':u;};
  const emfKey=(b,m,f,p)=>[String(b||'').trim().toUpperCase(),strip0(m),flagKey(f),strip0(p)].join('|');
  const emfTagOf=raw=>{
    const t=String(raw==null?'':raw).trim(); if(!t) return null;
    const n=t.replace(/^\s*\d+\s*[.)\-:]?\s*/,'').trim().toLowerCase();
    return n==='entry'?'Entry':n==='mid'?'Mid':n==='fashion'?'Fashion':null;
  };
  const emfLookup=()=>{
    const rows=S.files.emf; if(!rows||!rows.length) return null;
    const m=new Map();
    for(const r of rows){const t=emfTagOf(r['EMF']); if(t){const k=emfKey(r['Brand Type'],r['MC CD'],r['Flag'],r['MRP']); if(!m.has(k)) m.set(k,t);}}
    return m.get(emfKey(brand,mcCd,flag,mrp))||null;
  };
  const meets=(pd,min)=>Math.round(pd*10000)>=Math.round(min*10000);
  const describe=()=>String(brand||'').trim()+' · MC '+strip0(mcCd)+' · Flag '+(String(flag||'').trim()||'(blank)')+' · MRP '+strip0(mrp);

  if(optionType==='BOBs'){
    const allSibs=(S.files.bsr||[]).filter(r=>{
      const gmc=String(col(r,'Generic Material','generic material','GENERIC MATERIAL')).trim();
      return gmc===String(gm).trim()&&colourOf(r)!==String(colour).trim();
    });
    const qualSibs=allSibs.filter(r=>getWOF(r)>=wofGate);
    if(qualSibs.length>0){
      const best=qualSibs.reduce((b,r)=>getWOF(r)>getWOF(b)?r:b,qualSibs[0]);
      const rankPool=cleanPool.length?cleanPool:peerPool;
      if(!rankPool.length){
        const bp=getPDSTR(best), bw=getWOF(best);
        const _ab0=absBlock('Allow',bp,bw,'best sibling colourway ('+gm+')');
        if(_ab0.overridden) return {decision:_ab0.decision,decisionReason:_ab0.decisionReason,...base,siblings:allSibs,qualSibs,sibRanked:[],wof:bw,pdstr:bp,quartile_:null,percentile:null};
        return {decision:'Allow',decisionReason:`BOBs: ${qualSibs.length} sibling colourway(s) with WOF ≥ ${wofGate}w found, but no peer pool to rank them against. Auto Allow.`,...base,siblings:allSibs,qualSibs,sibRanked:[],wof:bw,pdstr:null,quartile_:null,percentile:null};
      }
      const sibRanked=qualSibs.map(r=>({row:r,q:quartile(getPDSTR(r),rankPool),pdstr:getPDSTR(r)}));
      const low=sibRanked.filter(s=>s.q!=='Q1');
      const hasLow=low.length>0;
      let decision=hasLow?'Block':'Allow';
      let decisionReason=hasLow
        ?`BOBs: ${low.length} of ${qualSibs.length} sibling colourway(s) with WOF ≥ ${wofGate}w rank below Q1 (${low.map(s=>colourOf(s.row)+' '+s.q).join(', ')}). Every other colour must rank Q1 (top 25%) — new colour blocked.`
        :`BOBs: All ${qualSibs.length} sibling colourway(s) with WOF ≥ ${wofGate}w rank Q1 (top 25%) — parent style is a top seller. New colour permitted.`;
      let emf={};
      if(decision==='Allow'){
        const _ab=absBlock('Allow',getPDSTR(best),getWOF(best),'best sibling colourway ('+gm+')');
        if(_ab.overridden){decision=_ab.decision;decisionReason=_ab.decisionReason;}
        else {
          const oc=colourOf(best), ow=getWOF(best), opd=getPDSTR(best), tag=emfLookup();
          const allQ1=`BOBs: All ${qualSibs.length} sibling colourway(s) with WOF ≥ ${wofGate}w rank Q1 (top 25%)`;
          if(!tag){
            emf={emfCheck:'notMapped',emfPdstr:opd}; decision="Hold — Don't Raise";
            decisionReason=NOT_MAINTAINED+' ('+describe()+' is not in the Entry / Mid / Fashion mapping. '+allQ1+'.)';
          } else {
            const min=EMF_MIN[tag], minTxt=`the ${tag} minimum ${(min*100).toFixed(1)}%/day`;
            const oldTxt=`the oldest colour ${oc} (${ow}w on floor) sells ${(opd*100).toFixed(1)}%/day`;
            if(meets(opd,min)){ emf={emfTag:tag,emfMin:min,emfCheck:'ok',emfPdstr:opd}; decisionReason=allQ1+' and '+oldTxt+' — at least '+minTxt+'. New colour permitted.'; }
            else { emf={emfTag:tag,emfMin:min,emfCheck:'below',emfPdstr:opd}; decision='Block'; decisionReason=allQ1+', but '+oldTxt+' — below '+minTxt+'. New colour blocked.'; }
          }
        }
      }
      const qOrder={Q4:0,Q3:1,Q2:2,Q1:3};
      const repSib=hasLow
        ? sibRanked.reduce((w,s)=>(qOrder[s.q]<qOrder[w.q]||(qOrder[s.q]===qOrder[w.q]&&s.pdstr<w.pdstr))?s:w, sibRanked[0])
        : sibRanked.reduce((b,s)=>(qOrder[s.q]>qOrder[b.q]||(qOrder[s.q]===qOrder[b.q]&&s.pdstr>b.pdstr))?s:b, sibRanked[0]);
      return {decision,decisionReason,...base,siblings:allSibs,qualSibs,sibRanked,wof:getWOF(repSib.row),pdstr:repSib.pdstr,quartile_:repSib.q,percentile:pctRank(repSib.pdstr,rankPool),repSibling:repSib.row,...emf};
    }
    if(allSibs.length>0){
      const bestSib=allSibs.reduce((b,r)=>getWOF(r)>getWOF(b)?r:b,allSibs[0]);
      const parentWOF=getWOF(bestSib), sp=getPDSTR(bestSib);
      const _ab=absBlock('Allow',sp,parentWOF,'best sibling of GM '+gm);
      if(_ab.overridden) return {decision:_ab.decision,decisionReason:_ab.decisionReason,...base,siblings:allSibs,qualSibs:[],sibRanked:[],wof:parentWOF,pdstr:sp,quartile_:null,percentile:null};
      return {decision:'Allow',decisionReason:`BOBs: ${allSibs.length} sibling(s) found but none qualify on WOF ≥ ${wofGate}w (best WOF=${parentWOF}w). Insufficient history — Auto Allow.`,...base,siblings:allSibs,qualSibs:[],sibRanked:[],wof:parentWOF,pdstr:null,quartile_:null,percentile:null};
    }
    return {decision:'Allow',decisionReason:'BOBs: No sibling colourways found in CY BSR. Auto Allow (new colourway of untracked parent).',...base,siblings:[],qualSibs:[],sibRanked:[],wof:0,pdstr:null,quartile_:null,percentile:null};
  }

  // True Repeat
  const bsrRows=getBSRRows(S.files.bsr,gm);
  const artRow=bsrRows.find(r=>colourOf(r)===String(colour).trim());
  const wof=artRow?getWOF(artRow):0;
  const label='GM+Colour ('+gm+' '+colour+')';
  if(wof<wofGate){
    const ap=artRow?getPDSTR(artRow):0;
    const _ab=absBlock('Allow',ap,wof,label);
    if(_ab.overridden) return {decision:_ab.decision,decisionReason:_ab.decisionReason,...base,siblings:[],qualSibs:[],sibRanked:[],wof,pdstr:ap,quartile_:null,percentile:null};
    return {decision:'Allow',decisionReason:`WOF = ${wof}w < ${wofGate}w gate. Insufficient selling history — Auto Allow.`,...base,siblings:[],qualSibs:[],sibRanked:[],wof,pdstr:null,quartile_:null,percentile:null};
  }
  if(!cleanPool.length){
    const ap=artRow?getPDSTR(artRow):0;
    const _ab=absBlock('Allow',ap,wof,label);
    if(_ab.overridden) return {decision:_ab.decision,decisionReason:_ab.decisionReason,...base,siblings:[],qualSibs:[],sibRanked:[],wof,pdstr:ap,quartile_:null,percentile:null};
    return {decision:'Allow',decisionReason:'No peer pool available. Auto Allow.',...base,siblings:[],qualSibs:[],sibRanked:[],wof,pdstr:null,quartile_:null,percentile:null};
  }
  const pdstr=artRow?getPDSTR(artRow):0;
  const q=quartile(pdstr,cleanPool);
  const pct=pctRank(pdstr,cleanPool);
  const gap=peerAvg>0?(peerAvg-pdstr)/peerAvg*100:0;
  const head=`True Repeat: ranks ${q} (${pct}th pct) in peer group of ${cleanPool.length} styles (${bsrLabel}). `;
  let dec,reason,emf={};
  if(q==='Q1'){
    dec='Allow'; reason=head+`PDSTR = ${(pdstr*100).toFixed(1)}%/day — top 25% (Q1). Allow.`;
    const _ab=absBlock(dec,pdstr,wof,label);
    if(_ab.overridden){dec=_ab.decision;reason=_ab.decisionReason;}
    else {
      const tag=emfLookup();
      if(!tag){
        emf={emfCheck:'notMapped',emfPdstr:pdstr}; dec="Hold — Don't Raise";
        reason=NOT_MAINTAINED+' ('+describe()+' is not in the Entry / Mid / Fashion mapping. '+head+`PDSTR = ${(pdstr*100).toFixed(1)}%/day.)`;
      } else {
        const min=EMF_MIN[tag], minTxt=`the ${tag} minimum ${(min*100).toFixed(1)}%/day`;
        if(meets(pdstr,min)){ emf={emfTag:tag,emfMin:min,emfCheck:'ok',emfPdstr:pdstr}; reason=head+`PDSTR = ${(pdstr*100).toFixed(1)}%/day — top 25% (Q1) and at least ${minTxt}. Allow.`; }
        else { emf={emfTag:tag,emfMin:min,emfCheck:'below',emfPdstr:pdstr}; dec='Block'; reason=head+`PDSTR = ${(pdstr*100).toFixed(1)}%/day — Q1 but below ${minTxt} (first 3 weeks). Block. Do not raise PO.`; }
      }
    }
  } else {
    dec='Block'; reason=head+`PDSTR = ${(pdstr*100).toFixed(1)}%/day (peer avg ${(peerAvg*100).toFixed(1)}%/day). Only Q1 (top 25%) is allowed for multi concept — Block. Do not raise PO.`;
  }
  return {decision:dec,decisionReason:reason,...base,siblings:[],qualSibs:[],sibRanked:[],wof,pdstr,quartile_:q,percentile:pct,gapPct:Math.round(gap*10)/10,...emf};
}
/* eslint-enable no-undef */

const REPLACEMENTS = [
  // Tool 1 result card (renderResult)
  { fn: 'renderResult',
    find: "const pLabel=r.percentile<=25?'Q4 — Slow Seller':r.percentile<=50?'Q3 — Watch':r.percentile<=75?'Q2 — Healthy':'Q1 — Top Seller';",
    replace: "const pLabel=r.percentile<=25?'Q4 — Slow Seller (Block)':r.percentile<=50?'Q3 — Block':r.percentile<=75?'Q2 — Block':'Q1 — Top 25% (Allow)';" },
  { fn: 'renderResult',
    find: "if(r.pdstr!=null && r.peerAvg!=null && r.pdstr<r.peerAvg && r.quartile_&&(r.quartile_==='Q4'||r.quartile_==='Q3')){",
    replace: "if(r.borderTolUsed && r.pdstr!=null && r.peerAvg!=null && r.pdstr<r.peerAvg && r.quartile_&&(r.quartile_==='Q4'||r.quartile_==='Q3')){" },
  { fn: 'renderResult',
    find: '          <span style="flex:1;text-align:center">Q4 Slow</span><span style="flex:1;text-align:center">Q3 Watch</span><span style="flex:1;text-align:center">Q2 Healthy</span><span style="flex:1;text-align:center">Q1 Top</span>',
    replace: '          <span style="flex:1;text-align:center">Q4 Block</span><span style="flex:1;text-align:center">Q3 Block</span><span style="flex:1;text-align:center">Q2 Block</span><span style="flex:1;text-align:center">Q1 Allow</span>' },
  // Tool 2 batch (processChunk): Step 2 — an MC-MRP not in the Entry / Mid / Fashion mapping is Cannot Validate, not Cancel
  { fn: 'processChunk',
    find: "result={...rawA, decision:decMap[rawA.decision]||rawA.decision, artSTR:null, recQty:null};",
    replace: "result={...rawA, decision:rawA.emfCheck==='notMapped'?'Cannot Validate':(decMap[rawA.decision]||rawA.decision), artSTR:null, recQty:null};" },
  // Tool 2 line detail (buildDetailedReason)
  { fn: 'buildDetailedReason',
    find: "if(r.pdstr!=null&&r.peerAvg!=null&&r.pdstr<r.peerAvg&&(r.quartile_==='Q4'||r.quartile_==='Q3')){",
    replace: "if(r.borderTolUsed&&r.pdstr!=null&&r.peerAvg!=null&&r.pdstr<r.peerAvg&&(r.quartile_==='Q4'||r.quartile_==='Q3')){" },
  { fn: 'buildDetailedReason',
    find: '<span style="flex:1;text-align:center">Q4 Slow</span><span style="flex:1;text-align:center">Q3 Watch</span><span style="flex:1;text-align:center">Q2 Healthy</span><span style="flex:1;text-align:center">Q1 Top</span></div>\';',
    replace: '<span style="flex:1;text-align:center">Q4 Block</span><span style="flex:1;text-align:center">Q3 Block</span><span style="flex:1;text-align:center">Q2 Block</span><span style="flex:1;text-align:center">Q1 Allow</span></div>\';' },
];

/**
 * Applies the rule to an extracted page source: the REPLACEMENTS of the functions in `fns` (each must occur exactly once)
 * and (withEngine, default true) runEngineA appended at the end (the later definition wins).
 */
function apply(src, fns, withEngine = true) {
  let out = src;
  for (const r of REPLACEMENTS.filter(x => fns.includes(x.fn))) {
    const n = out.split(r.find).length - 1;
    if (n !== 1) throw new Error(`multi-concept rule: expected 1 match in ${r.fn}, found ${n}: ${r.find.slice(0, 80)}`);
    out = out.replace(r.find, () => r.replace);
  }
  if (!withEngine) return out;
  return out + '\n\n// multi concept rule, Step 1 (tests/parity/rules/multi-concept.js)\n' + runEngineA.toString() + '\n';
}

/**
 * The Entry / Mid / Fashion mapping of the parity fixtures (tool1 / tool2 make-fixture.js), rows as the Data Setup upload
 * stores them. Covers: mapped (Mid), the NON-BOGO = "Normal" alias (Fashion), and two keys left out on purpose
 * (NON-PL 4407 with a blank flag, MAVIE 5509 BOGO 499) so their Q1 options are a Hold "not maintained".
 */
const FIXTURE_EMF = [
  { 'Brand Type': 'NON-PL', 'Division': 'LADIES', 'MC CD': '2203', 'MC DESC': 'L-JEANS', 'Flag': 'BOGO', 'MRP': '599', 'EMF': 'Mid' },
  { 'Brand Type': 'MAVIE', 'Division': 'LADIES', 'MC CD': '3305', 'MC DESC': 'L-TOPS', 'Flag': 'Normal', 'MRP': '799', 'EMF': 'Fashion' },
  { 'Brand Type': 'NON-PL', 'Division': 'LADIES', 'MC CD': '2203', 'MC DESC': 'L-JEANS', 'Flag': 'NOOS', 'MRP': '599', 'EMF': 'Entry' }
];

module.exports = { apply, runEngineA, REPLACEMENTS, FIXTURE_EMF };
