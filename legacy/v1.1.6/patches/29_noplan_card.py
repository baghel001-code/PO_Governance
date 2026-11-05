# v1.1.6 D1: a "No Plan Available" Hold card shows no stock / mix / STR tiles and no Step-by-Step (nothing was
# worked out - the plan is missing); a short note says why. Display only.
rep("""  if(r.engine==='B'){
    if(r.mixFailed){""", """  if(r.engine==='B'){
    if(r.noPlan){
      html+=`<div style="background:#FEF2F2;border:1px solid #FECACA;border-left:4px solid #DC2626;border-radius:0 8px 8px 0;padding:10px 14px;font-size:12px;color:#7F1D1D;margin-bottom:14px">
        No F4 plan for this group in the requirement window, so no stock, colour mix or STR was worked out.
      </div>`;
    } else if(r.mixFailed){""", label="v116 D1 no plan card")
