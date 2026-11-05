# v1.1.6 D2 (agreed 4 Nov 2026): single concept, the days left in the current month COUNT TODAY
# (F1 stock is the position at yesterday's close, so today's sale is still to come).
# 4 Nov, 30-day month: 27 days left (was 26). Applies to: New PO request (runEngineB and its Step 3 table),
# PO Assessment single concept (runEngineB2Fast; runEngineB2 is not used but kept the same),
# Recommendation single concept (_coreColourSTRFast called with countToday=true).
# Multi concept Recommendation calls _coreColourSTRFast without the flag -> unchanged.

rep("""function daysInMonth(y,m) { return new Date(y,m,0).getDate(); }""",
    """function daysInMonth(y,m) { return new Date(y,m,0).getDate(); }
// v1.1.6 D2: days left in the current month, today included
function _daysLeft(totalDays,todayDate){ return Math.max(0,totalDays-todayDate+1); }""", label="v116 D2 helper")

rep("""    const remainingDays=i===0?(totalDays-todayDate):totalDays;""",
    """    const remainingDays=i===0?_daysLeft(totalDays,todayDate):totalDays;""", count=3, label="v116 D2 pre-roll (single)")
rep("""    const demandDays=isCurrMonth?(totalDays-todayDate):totalDays;""",
    """    const demandDays=isCurrMonth?_daysLeft(totalDays,todayDate):totalDays;""", count=3, label="v116 D2 chain (single)")
rep("""                      const remD=i===0?(totalD-todayD2):totalD;""",
    """                      const remD=i===0?_daysLeft(totalD,todayD2):totalD;""", label="v116 D2 Step 3 table")

rep("""function _coreColourSTRFast(opStock,mixPct,forecastByMon,f2RawMap,anchorMonth){""",
    """function _coreColourSTRFast(opStock,mixPct,forecastByMon,f2RawMap,anchorMonth,countToday){""", label="v116 D2 core signature")
rep("""    var remainingDays=i===0?(totalDays-todayDate):totalDays;
    var cp=fc*mixPct*(remainingDays/totalDays);""",
    """    var remainingDays=i===0?(countToday?_daysLeft(totalDays,todayDate):(totalDays-todayDate)):totalDays;
    var cp=fc*mixPct*(remainingDays/totalDays);""", label="v116 D2 core pre-roll")
rep("""    var demandDays=isCurrMonth?(totalDays2-todayDate):totalDays2;""",
    """    var demandDays=isCurrMonth?(countToday?_daysLeft(totalDays2,todayDate):(totalDays2-todayDate)):totalDays2;""", label="v116 D2 core chain")
rep("""      var cs=_coreColourSTRFast(stk,mixRatio,forecastByMon,f2Raw,mon);""",
    """      var cs=_coreColourSTRFast(stk,mixRatio,forecastByMon,f2Raw,mon,true);""", label="v116 D2 single recommendation")
