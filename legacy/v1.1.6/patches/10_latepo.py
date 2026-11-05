# v1.2.7 item 1 (agreed 29 Oct 2026): PO Assessment, single concept. From the 1st to the cut-off day
# (Settings prevMonthPendingCutoffDay, default 5) a pending PO whose delivery month has just passed still counts.
# Its window starts on the 1st of the CURRENT month, so it must be in the stock at the window start - not added
# as a PO landing during the window (which hid it from its own check: e.g. Retain 8,400 instead of Reduce).
# Only the window-start stock of that case changes; every later month of the chain is the same as before.
# Same rule and text as the server (engines/assessment.run_engine_b2_fast + engines/single.late_carry_note).

rep("""function _ePOWithCarry(existingPOMap, mkey, carry){""",
    """// v1.2.7: the late PO (previous month, until the cut-off day) counted in the stock at the window start
function _lateCarryNote(carry){
  if(!carry || carry.qty<=0) return '';
  return ` [Includes ${Math.round(carry.qty).toLocaleString('en-IN')} units of previous month's (${carry.prevKey}) pending PO, counted in the stock at the window start (${carry.currKey}) — Day-of-month cut-off = ${carry.cutoff}.]`;
}
function _ePOWithCarry(existingPOMap, mkey, carry){""", label="v127 late carry note")

rep("""    const ePO=_ePOWithCarry(existingPO, mkey, prevMonthCarry);
    const fc=forecastByMon[mkey]||0;
    const totalDays=daysInMonth(cy,cm);
    // Edge case: if window start happens to equal current month, use remaining days
    const isCurrMonth=(mkey===currKey2);
    const demandDays=isCurrMonth?(totalDays-todayDate):totalDays;
    const colPlan=effectiveMixPct!==null?fc*effectiveMixPct*(demandDays/totalDays):0;
    const openStock=i===0?runStock:(chain[i-1]?.closeStock||0);""",
    """    // v1.2.7: window starts this month (a late PO of last month) -> the carried qty is stock at the window start
    const lateHere=(i===0 && prevMonthCarry && prevMonthCarry.qty>0 && mkey===prevMonthCarry.currKey);
    const ePO=lateHere?((existingPO&&existingPO[mkey])||0):_ePOWithCarry(existingPO, mkey, prevMonthCarry);
    const fc=forecastByMon[mkey]||0;
    const totalDays=daysInMonth(cy,cm);
    // Edge case: if window start happens to equal current month, use remaining days
    const isCurrMonth=(mkey===currKey2);
    const demandDays=isCurrMonth?(totalDays-todayDate):totalDays;
    const colPlan=effectiveMixPct!==null?fc*effectiveMixPct*(demandDays/totalDays):0;
    const openStock=i===0?(runStock+(lateHere?prevMonthCarry.qty:0)):(chain[i-1]?.closeStock||0);""",
    label="v127 late PO at window start (PA single)")

rep("""  decisionReason += prevMonthCarryNote;

  return {decision,decisionReason,artSTR,recQty,maxRetainQty,mixPct,opStock,colAvg,mcAvg,windowDailyRate,winStockStart,chain,prevMonthCarry};""",
    """  decisionReason += (prevMonthCarry && prevMonthCarry.qty>0 && winKey===prevMonthCarry.currKey) ? _lateCarryNote(prevMonthCarry) : prevMonthCarryNote;

  return {decision,decisionReason,artSTR,recQty,maxRetainQty,mixPct,opStock,colAvg,mcAvg,windowDailyRate,winStockStart,chain,prevMonthCarry};""",
    label="v127 late PO note (PA single)")
