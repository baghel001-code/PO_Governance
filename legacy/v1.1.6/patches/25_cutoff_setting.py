# v1.1.6 S2: the late PO cut-off day (prevMonthPendingCutoffDay, default 5) is editable in the Parameters panel,
# under the same panel as the other parameters (planners / admins). Whole days 1-28; anything else keeps the old value.
# Saved and shared like every other parameter (app/shared.js PARAM_INPUTS, server audit list).

rep("""        <input class="p-inp" id="pp-borderTolerance" value="10" onchange="syncParams()" style="background:#FFFBEB;color:#D97706;width:56px;font-weight:700">
      </div>""", """        <input class="p-inp" id="pp-borderTolerance" value="10" onchange="syncParams()" style="background:#FFFBEB;color:#D97706;width:56px;font-weight:700">
      </div>
      <div class="p-row" style="border-top:1px solid var(--brd);margin-top:6px;padding-top:6px" title="From the 1st to this day of the month, a pending PO of last month still counts (PO Assessment: as stock at the window start). Whole days 1–28."><span class="p-lbl" style="color:#1A4FAF;font-weight:600">Late PO counts until day <span style="font-size:9px;font-weight:400;color:var(--ink3)">(Single Concept MRP)</span></span>
        <input class="p-inp" id="pp-prevMonthPendingCutoffDay" value="5" onchange="syncParams()" style="background:#EAF0FB;color:#1A4FAF;width:56px;font-weight:700">
      </div>""", label="v116 S2 cut-off field")

rep("""  ['T1','T2','D','mix','N','wof','peer','strBuffer','borderTolerance'].forEach(k=>{
    const v=parseFloat(document.getElementById('pp-'+k)?.value);
    if(!isNaN(v)) S.params[k]=v;
  });""", """  ['T1','T2','D','mix','N','wof','peer','strBuffer','borderTolerance'].forEach(k=>{
    const v=parseFloat(document.getElementById('pp-'+k)?.value);
    if(!isNaN(v)) S.params[k]=v;
  });
  // v1.1.6 S2: late PO cut-off day, whole days 1-28 only
  (function(){
    const inp=document.getElementById('pp-prevMonthPendingCutoffDay'); if(!inp) return;
    const v=Number(inp.value);
    if(Number.isInteger(v)&&v>=1&&v<=28) S.params.prevMonthPendingCutoffDay=v;
    else { inp.value=S.params.prevMonthPendingCutoffDay!=null?S.params.prevMonthPendingCutoffDay:5; if(typeof toast==='function') toast('Late PO cut-off day must be a whole day from 1 to 28.'); }
  })();""", label="v116 S2 cut-off read")
