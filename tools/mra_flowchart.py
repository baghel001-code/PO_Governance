# Draws docs/sap/MRA-Data-Flow.svg (MRA data process flow chart). Usage: python3 tools/mra_flowchart.py docs/sap/MRA-Data-Flow.svg
import html, sys
W, H = 1680, 1880
out = []
def esc(s): return html.escape(s)
def text(x, y, lines, size=13, weight=400, color="#1A2B4A", anchor="middle"):
    dy = size * 1.3
    y0 = y - (len(lines) - 1) * dy / 2
    for i, l in enumerate(lines):
        out.append(f'<text x="{x}" y="{y0 + i*dy + size*0.35:.1f}" font-size="{size}" font-weight="{weight}" fill="{color}" text-anchor="{anchor}">{esc(l)}</text>')
STY = {
 "start": ("#13254A", "#13254A", "#FFFFFF"),
 "step":  ("#FFFFFF", "#9AAAC8", "#1A2B4A"),
 "db":    ("#EEF3FC", "#1A4FAF", "#13254A"),
 "ok":    ("#E7F6EC", "#00873E", "#0B5A2B"),
 "bad":   ("#FDECEC", "#DC2626", "#7F1D1D"),
 "warn":  ("#FFF4DE", "#B7791F", "#5A3A00"),
 "dec":   ("#FFF8E6", "#D97706", "#5A3A00"),
}
def box(cx, cy, w, h, lines, kind="step", size=13, bold=False):
    f, s, c = STY[kind]
    r = 22 if kind == "start" else 10
    out.append(f'<rect x="{cx-w/2}" y="{cy-h/2}" width="{w}" height="{h}" rx="{r}" fill="{f}" stroke="{s}" stroke-width="1.6"/>')
    text(cx, cy, lines, size, 700 if bold or kind == "start" else 400, c)
    return (cx, cy, w, h)
def diamond(cx, cy, w, h, lines, size=12.5):
    f, s, c = STY["dec"]
    out.append(f'<polygon points="{cx},{cy-h/2} {cx+w/2},{cy} {cx},{cy+h/2} {cx-w/2},{cy}" fill="{f}" stroke="{s}" stroke-width="1.6"/>')
    text(cx, cy, lines, size, 600, c)
    return (cx, cy, w, h)
def arrow(pts, label=None, lpos=None, color="#56637A"):
    d = "M " + " L ".join(f"{x},{y}" for x, y in pts)
    out.append(f'<path d="{d}" fill="none" stroke="{color}" stroke-width="1.7" marker-end="url(#ah)"/>')
    if label:
        lx, ly = lpos if lpos else ((pts[-2][0] + pts[-1][0]) / 2 + 8, (pts[-2][1] + pts[-1][1]) / 2)
        out.append(f'<text x="{lx}" y="{ly}" font-size="12" font-weight="700" fill="{color}">{esc(label)}</text>')
def bottom(b): return (b[0], b[1] + b[3] / 2)
def top(b): return (b[0], b[1] - b[3] / 2)
def left(b): return (b[0] - b[2] / 2, b[1])
def right(b): return (b[0] + b[2] / 2, b[1])
def down(a, b, label=None): arrow([bottom(a), top(b)], label)

# lanes
lanes = [(30, 540, "A · Morning MRA file (Planner, Data Setup)", "#1A4FAF"),
         (570, 540, "B · Live changes from SAP (all day)", "#E8006E"),
         (1110, 540, "C · How the tool uses the MRAs", "#00873E")]
for x, w, t, c in lanes:
    out.append(f'<rect x="{x}" y="90" width="{w}" height="1500" rx="14" fill="#F7F9FD" stroke="#DCE3F0"/>')
    out.append(f'<rect x="{x}" y="90" width="{w}" height="44" rx="14" fill="{c}"/>')
    out.append(f'<rect x="{x}" y="118" width="{w}" height="16" fill="{c}"/>')
    text(x + w/2, 112, [t], 15, 800, "#FFFFFF")

# ── lane A ──
ax = 300
a1 = box(ax, 180, 330, 52, ["Planner uploads the MRA workbook", "(.xlsx / .xlsb) in the Data Setup sidebar"], "start", 12.5)
a2 = diamond(ax, 285, 330, 100, ["Item-lines sheet and header", "sheet found by their headers?"])
a2n = box(ax + 0, 400, 300, 50, ["Refused – nothing changed", "(message names the missing columns)"], "bad", 12)
a3 = box(ax, 505, 380, 92, ["Read item lines: DOC.NO, Generic Article (last 10", "digits), Color, TOTAL PO QTY added up per MRA,", "DEL. DATE, LEVEL · header sheet: PO, status, created by"], "step", 12)
a4 = box(ax, 615, 380, 66, ["Normalise level / date / qty – rows that cannot be", "read are skipped and counted in the summary"], "step", 12)
a5 = box(ax, 710, 380, 52, ["Keep a decision SAP sent earlier when the", "file has none for that MRA"], "step", 12)
a6 = box(ax, 860, 440, 190, ["ONE transaction (PRO_SAP_MRA UPLOAD):", "1. SAP_MRA_FILE emptied and filled from the file", "2. Changes already applied today re-tagged to the new version", "3. Pending SAP changes moved into SAP_MRA_FEED", "    (arrival order, Data_Version = this Upload ID)", "4. SAP_MRA_FEED_PENDING emptied", "5. IsDailyDataUpload = 1, Data_Version = Upload ID"], "db", 12)
a7 = box(ax, 1030, 400, 70, ["Summary under the slot: MRAs per level, counted now,", "rows skipped, “N SAP changes received before this", "file were applied on top of it”"], "ok", 12)
down(a1, a2)
arrow([bottom(a2), top(a2n)], "No", (ax + 10, 345))
arrow([right(a2), (ax + 225, 285), (ax + 225, 445), (ax + 180, 445), top(a3)][0:2] + [(ax + 225, 445), (ax, 445), top(a3)], "Yes", (ax + 172, 278))
down(a3, a4); down(a4, a5); down(a5, a6); down(a6, a7)

# ── lane B ──
bx = 840
b1 = box(bx, 180, 380, 52, ["SAP sends POST /api/sap/v1/mra-events or /po-events", "(one message or {\"messages\": […]}, max 5,000)"], "start", 12)
b2 = box(bx, 270, 380, 52, ["Technical user (HTTP Basic), IP allow-list,", "interface on – else 401 / 503"], "step", 12)
b3 = box(bx, 360, 380, 66, ["Read each message: mra_no, level (1–9, X) – optional", "action_by, remark, po_no, po_status, occurred_at,", "msg_id, decision fields"], "step", 12)
b4 = diamond(bx - 70, 470, 300, 96, ["Message understood?"])
b4n = box(1035, 470, 120, 70, ["Stored as", "“parked” with", "the reason"], "bad", 12)
b5 = diamond(bx - 40, 610, 330, 110, ["Today's MRA file uploaded?", "(IsDailyDataUpload)"])
b6 = box(690, 780, 210, 120, ["NO → temporary table", "SAP_MRA_FEED_PENDING", "answer: “pending”", "(applied when the", "file is uploaded)"], "warn", 12)
b7 = box(910, 780, 200, 120, ["YES → SAP_MRA_FEED", "with the current", "Data_Version", "– applied at once"], "db", 12)
b8 = box(bx, 965, 420, 110, ["Replay file + messages of the current version:", "applied · no change · duplicate (same msg_id) ·", "older (occurred_at before the last change) · parked ·", "stored (PO message) · new MRA needs article, colour,", "qty, delivery date"], "step", 12)
b9 = box(bx, 1085, 420, 52, ["Answer to SAP: one result per message, same order", "(+ counts: applied, pending, parked …)"], "ok", 12)
down(b1, b2); down(b2, b3)
arrow([bottom(b3), (bx, 410), (bx - 70, 410), top(b4)])
arrow([right(b4), left(b4n)], "No", (900, 462))
arrow([bottom(b4), (bx - 70, 532), (bx - 40, 532), top(b5)], "Yes", (bx - 62, 530))
arrow([left(b5), (690, 610), top(b6)], "No", (650, 602))
arrow([right(b5), (910, 610), top(b7)], "Yes", (968, 602))
arrow([bottom(b7), (910, 880), (bx, 880), top(b8)])
arrow([bottom(b4n), (1035, 880), (bx + 20, 880)])
down(b8, b9)
# pending -> moved by upload (dashed to lane A)
out.append(f'<path d="M 585,800 L 578,800 L 578,1140 L 90,1140 L 90,955" fill="none" stroke="#B7791F" stroke-width="1.7" stroke-dasharray="6 5" marker-end="url(#ahw)"/>')
out.append(f'<text x="110" y="1160" font-size="12" font-weight="700" fill="#B7791F">at the upload: pending changes are moved onto the new file (step 3)</text>')

# ── lane C ──
cx = 1380
c1 = box(cx, 180, 420, 52, ["Every New PO request, SAP /decisions, single concept", "Recommendation, MRA Queues: current MRA position"], "start", 12)
c2 = box(cx, 270, 420, 52, ["Morning file + messages of the current data version", "replayed → level, qty, PO, history of every MRA"], "step", 12)
c3 = diamond(1320, 380, 320, 100, ["In Article Master and", "single concept?"])
c3n = box(1572, 380, 136, 60, ["Set aside –", "not counted"], "bad", 12)
c4 = diamond(1320, 510, 300, 92, ["Delivery date today", "or later?"])
c4n = box(1572, 510, 136, 60, ["Expired –", "not counted"], "bad", 12)
c5 = diamond(1320, 650, 330, 116, ["Level 1, 2, 4 – or level 5 reached", "by a live change today with the", "PO not unreleased?"], 12)
c5n = box(1572, 650, 136, 92, ["Not counted", "(3, 6, 7, 8, 9, X,", "final earlier, PO", "unreleased)"], "bad", 12)
c6 = box(1320, 790, 320, 70, ["Counted: qty added to the released", "POs of its colour and delivery month", "(a live PO change governs it, switch 2)"], "ok", 12)
c7 = box(cx, 915, 420, 92, ["Max Permissible = what is still free after the plan,", "the stock, released POs (F2), open MRAs and today's", "tool approvals – New PO request card shows “Open", "MRAs from SAP counted in this colour”"], "db", 12)
c8 = diamond(cx, 1060, 360, 96, ["Switch 1 “Tool requests", "use capacity” ON?"])
c9 = box(cx - 105, 1185, 200, 74, ["ON: request saved,", "approval counts for", "the day"], "ok", 12)
c10 = box(cx + 105, 1185, 200, 74, ["OFF: check only – raise", "the request in SAP; tool", "approvals not counted"], "warn", 12)
arrow([bottom(c1), top(c2)])
arrow([bottom(c2), (cx, 312), (1320, 312), top(c3)])
arrow([right(c3), left(c3n)], "No", (1484, 366))
arrow([bottom(c3), top(c4)], "Yes")
arrow([right(c4), left(c4n)], "No", (1474, 496))
arrow([bottom(c4), top(c5)], "Yes")
arrow([right(c5), left(c5n)], "No", (1488, 636))
arrow([bottom(c5), top(c6)], "Yes")
arrow([bottom(c6), (1320, 850), (cx, 850), top(c7)])
down(c7, c8)
arrow([left(c8), (cx - 105, 1060), top(c9)], "Yes", (cx - 175, 1052))
arrow([right(c8), (cx + 105, 1060), top(c10)], "No", (cx + 190, 1052))
# data flow from A and B into C
out.append(f'<path d="M {ax+220},860 L 600,860 L 600,1260 L 1130,1260 L 1130,270 L {cx-210},270" fill="none" stroke="#1A4FAF" stroke-width="1.5" stroke-dasharray="3 4" marker-end="url(#ahb)"/>')
out.append(f'<path d="M {bx+210},965 L 1150,965 L 1150,290 L {cx-210},290" fill="none" stroke="#1A4FAF" stroke-width="1.5" stroke-dasharray="3 4" marker-end="url(#ahb)"/>')
out.append(f'<text x="1160" y="1285" font-size="12" font-weight="700" fill="#1A4FAF">file + feed of the current data version → replay</text>')

# legend / day cycle
ly = 1330
out.append(f'<rect x="1110" y="{ly}" width="540" height="250" rx="12" fill="#FFFFFF" stroke="#DCE3F0"/>')
text(1380, ly + 22, ["A day with IsDailyDataUpload"], 14, 800, "#13254A")
rows = [("00:00", "New day: IsDailyDataUpload = No (until today's file)", "#B7791F"),
        ("Morning", "SAP changes → pending table (answer “pending”)", "#B7791F"),
        ("Upload", "File replaces the list; pending moved on top; flag = Yes", "#1A4FAF"),
        ("Day", "SAP changes applied at once (same data version)", "#00873E"),
        ("Re-upload", "Same-day changes kept on the new version", "#1A4FAF")]
for i, (t, d, c) in enumerate(rows):
    y = ly + 62 + i * 36
    out.append(f'<circle cx="1135" cy="{y}" r="6" fill="{c}"/>')
    out.append(f'<text x="1150" y="{y+4}" font-size="12.5" font-weight="700" fill="{c}">{esc(t)}</text>')
    out.append(f'<text x="1240" y="{y+4}" font-size="12.5" fill="#1A2B4A">{esc(d)}</text>')

# bottom: tables legend
out.append(f'<rect x="30" y="1330" width="1060" height="250" rx="12" fill="#FFFFFF" stroke="#DCE3F0"/>')
text(560, 1352, ["Where the data is kept (SQL Server, schema v7, procedure PRO_SAP_MRA)"], 14, 800, "#13254A")
tbl = [("SAP_MRA_FILE", "the morning file: one row per MRA (article, colour, qty, delivery date, level, POs)"),
       ("SAP_MRA_FILE_UPLOADS", "one row per upload: file, by, counts, how many pending changes were moved"),
       ("SAP_MRA_FEED", "every live change applied, in order, with its Data_Version (also bad ones, “parked”)"),
       ("SAP_MRA_FEED_PENDING", "temporary table: changes received before today's file – emptied by the upload"),
       ("SAP_MRA_CONTROL", "one row: Data_Day, IsDailyDataUpload, Data_Version (= Upload ID of the file in use)")]
for i, (n, d) in enumerate(tbl):
    y = 1392 + i * 36
    out.append(f'<rect x="50" y="{y-14}" width="230" height="26" rx="6" fill="#EEF3FC" stroke="#1A4FAF"/>')
    out.append(f'<text x="165" y="{y+4}" font-size="12.5" font-weight="700" fill="#13254A" text-anchor="middle">{n}</text>')
    out.append(f'<text x="295" y="{y+4}" font-size="12.5" fill="#1A2B4A">{esc(d)}</text>')

hdr = (f'<text x="30" y="40" font-size="24" font-weight="800" fill="#13254A">MRA data process – PO Governance</text>'
       f'<text x="30" y="66" font-size="13.5" fill="#56637A">SAP Merchandise Requests: morning file, live changes from SAP (pending until the day\'s file), and how open MRAs take capacity · v1.1.7 rules, schema v7 · 6 Nov 2026</text>')
defs = ('<defs>'
        '<marker id="ah" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="#56637A"/></marker>'
        '<marker id="ahw" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="#B7791F"/></marker>'
        '<marker id="ahb" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="#1A4FAF"/></marker>'
        '</defs>')
svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="1610" viewBox="0 0 {W} 1610" font-family="Inter, Segoe UI, Arial, sans-serif">'
       f'<rect width="100%" height="100%" fill="#FFFFFF"/>{defs}{hdr}' + "".join(out) + '</svg>')
open(sys.argv[1], "w").write(svg)
