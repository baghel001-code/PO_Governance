# v1.1.6: the result card looked for its Recommendation button with the literal text '#rbtn-${r.id}' (inside quotes,
# so not filled in) -> "is not a valid selector" error in the browser after every request. Results were not affected
# (openRec reads the card's own engine/info); this only removes the error.
rep("""  setTimeout(function(){ const btn=div.querySelector('#rbtn-${r.id}'); if(btn){btn._engine=_re;btn._info=_ri;} },0);""",
    """  setTimeout(function(){ const btn=div.querySelector('#rbtn-'+CSS.escape(String(r.id))); if(btn){btn._engine=_re;btn._info=_ri;} },0);""",
    label="v116 rbtn selector fix")
