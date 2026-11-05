# v1.1.6 D1 (PO Assessment): the new status "No Plan – Review" is shown everywhere "Cannot Validate" is:
# own colour (grey-blue), not counted as qty / value at risk, own KPI tile, own Action filter button, own colour
# in the Excel export and the Tool 2 summary. No quantity is suggested (Rec Qty "—").
NP = "No Plan – Review"

# every "needs action" filter leaves it out, exactly like Cannot Validate
_old = "r.decision!=='Retain'&&r.decision!=='Cannot Validate'"
_n = html.count(_old)
if _n != 8:
    sys.exit("PATCH FAILED [v116 no plan filters]: expected 8, found %d" % _n)
html = html.replace(_old, _old + "&&r.decision!=='" + NP + "'")
log.append("v116 no plan filters x8")
rep("""    if(r.decision==='Retain'||r.decision==='Cannot Validate') return;""",
    """    if(r.decision==='Retain'||r.decision==='Cannot Validate'||r.decision==='""" + NP + """') return;""", label="v116 no plan div/mc table")

# colours
rep("""    'Watch List':'#D97706','Cannot Validate':'#6B7280'};
  return m[d]||'#6B7280';""", """    'Watch List':'#D97706','Cannot Validate':'#6B7280','""" + NP + """':'#3B5B92'};
  return m[d]||'#6B7280';""", label="v116 no plan decColor")
rep("""    'Watch List':'#FFFBEB','Cannot Validate':'#F5F5F5'};
  return m[d]||'#F5F5F5';""", """    'Watch List':'#FFFBEB','Cannot Validate':'#F5F5F5','""" + NP + """':'#E8EEF8'};
  return m[d]||'#F5F5F5';""", label="v116 no plan decBg")
rep("""            var dCol=r.decision==='Cancel'?'#DC2626':r.decision==='Reduce Qty'?'#D97706':r.decision==='Watch List'?'#92400E':'#00873E';""",
    """            var dCol=r.decision==='Cancel'?'#DC2626':r.decision==='Reduce Qty'?'#D97706':r.decision==='Watch List'?'#92400E':r.decision==='""" + NP + """'?'#3B5B92':'#00873E';""",
    label="v116 no plan tree colour")

# KPI tile
rep("""  const cantVal=byDec('Cannot Validate');""", """  const cantVal=byDec('Cannot Validate');
  const noPlanCt=byDec('""" + NP + """');""", label="v116 no plan count")
rep("""    {label:'Watch List', val:watch,      col:'#D97706', bg:'#FFF3CD'},
    {label:'Qty at Risk',val:fmt(atRisk),col:'#E8006E', bg:'#FCE7F3'},""",
    """    {label:'Watch List', val:watch,      col:'#D97706', bg:'#FFF3CD'},
    {label:'No Plan – Review', val:noPlanCt, col:'#3B5B92', bg:'#E8EEF8'},
    {label:'Qty at Risk',val:fmt(atRisk),col:'#E8006E', bg:'#FCE7F3'},""", label="v116 no plan KPI tile")

# Action filter button
rep("""                <button id="filt-btn-retain" onclick="setDecFilter('Retain')" """,
    """                <button id="filt-btn-noplan" onclick="setDecFilter('""" + NP + """')" style="padding:6px 13px;border-radius:6px;font-size:11px;font-weight:700;cursor:pointer;border:1.5px solid #3B5B92;background:#E8EEF8;color:#3B5B92;font-family:var(--font)">No Plan</button>
                <button id="filt-btn-retain" onclick="setDecFilter('Retain')" """, label="v116 no plan filter button")
rep("""  var btns={'Cancel':'filt-btn-cancel','Reduce Qty':'filt-btn-reduce','Watch List':'filt-btn-watch','Retain':'filt-btn-retain','':'filt-btn-all'};""",
    """  var btns={'Cancel':'filt-btn-cancel','Reduce Qty':'filt-btn-reduce','Watch List':'filt-btn-watch','""" + NP + """':'filt-btn-noplan','Retain':'filt-btn-retain','':'filt-btn-all'};""",
    label="v116 no plan filter map")

# single concept colour groups: own colour decision, POs not cut, no qty suggested
rep("""    if(g.rowDecision === 'Cannot Validate') {
      colourDecision = 'Cannot Validate';
    } else if(g.rowDecision === 'Watch List') {""", """    if(g.rowDecision === 'Cannot Validate') {
      colourDecision = 'Cannot Validate';
    } else if(g.rowDecision === '""" + NP + """') {
      colourDecision = '""" + NP + """';
    } else if(g.rowDecision === 'Watch List') {""", label="v116 no plan colour decision")
rep("""    var poActions = sorted.map(function(p) {
      if(remaining <= 0) {""", """    var poActions = sorted.map(function(p) {
      if(colourDecision === '""" + NP + """') return {poNum:p.poNum, gm:p.gm, relQty:p.relQty, action:'""" + NP + """', recQty:0, noPlan:true};
      if(remaining <= 0) {""", label="v116 no plan PO actions")
rep("""dec==='Watch List'?'#92400E':dec==='Cannot Validate'?'#6B7280':'#00873E';""",
    """dec==='Watch List'?'#92400E':dec==='Cannot Validate'?'#6B7280':dec==='""" + NP + """'?'#3B5B92':'#00873E';""", label="v116 no plan group colour")
rep("""dec==='Watch List'?'#FFF7ED':dec==='Cannot Validate'?'#F3F4F6':'#DCFCE7';""",
    """dec==='Watch List'?'#FFF7ED':dec==='Cannot Validate'?'#F3F4F6':dec==='""" + NP + """'?'#E8EEF8':'#DCFCE7';""", label="v116 no plan group bg")
rep("""        if(g.rowDecision==='Cannot Validate' || g.rowDecision==='Watch List' || (g.artSTR==null && g.rowDecision==='Retain')){""",
    """        if(g.rowDecision==='Cannot Validate' || g.rowDecision==='""" + NP + """' || g.rowDecision==='Watch List' || (g.artSTR==null && g.rowDecision==='Retain')){""",
    label="v116 no plan group reason")
rep("""          var paCol = pa.action==='Cancel'?'#DC2626':pa.action==='Reduce Qty'?'#D97706':'#00873E';""",
    """          var paCol = pa.action==='Cancel'?'#DC2626':pa.action==='Reduce Qty'?'#D97706':pa.noPlan?'#3B5B92':'#00873E';""", label="v116 no plan PO colour")
rep("""'+(pa.recQty>0?fmt(pa.recQty):'0')+'</td>'""", """'+(pa.noPlan?'—':(pa.recQty>0?fmt(pa.recQty):'0'))+'</td>'""", label="v116 no plan PO rec qty")

# Excel export and Tool 2 summary
rep("""  const decOrder2 = ['Retain','Watch List','Reduce Qty','Cancel','Cannot Validate'];""",
    """  const decOrder2 = ['Retain','Watch List','Reduce Qty','Cancel','Cannot Validate','""" + NP + """'];""", label="v116 no plan export order")
rep("""    'Cannot Validate': {font:'555555', fill:'EDEDED'}
  };""", """    'Cannot Validate': {font:'555555', fill:'EDEDED'},
    '""" + NP + """': {font:'3B5B92', fill:'E8EEF8'}
  };""", label="v116 no plan export style")
rep("""  const decColorsT2 = {'Retain':'#00873E','Watch List':'#B45309','Reduce Qty':'#D97706','Cancel':'#DC2626','Cannot Validate':'#6B7280'};
  const decOrder = ['Retain','Watch List','Reduce Qty','Cancel','Cannot Validate'];""",
    """  const decColorsT2 = {'Retain':'#00873E','Watch List':'#B45309','Reduce Qty':'#D97706','Cancel':'#DC2626','Cannot Validate':'#6B7280','""" + NP + """':'#3B5B92'};
  const decOrder = ['Retain','Watch List','Reduce Qty','Cancel','Cannot Validate','""" + NP + """'];""", label="v116 no plan T2 summary")

# review fix: a No Plan group shows no Max Permissible (was the full PO qty because STR is not worked out)
rep("""    // Colour-level decision.
    // IMPORTANT: artSTR==null does NOT by itself mean "Watch List" """,
    """    if(g.rowDecision === '""" + NP + """') { maxPermissible = 0; excessUnits = 0; }   // v1.1.6: no qty suggested
    // Colour-level decision.
    // IMPORTANT: artSTR==null does NOT by itself mean "Watch List" """, label="v116 no plan group no qty")
