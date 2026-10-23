import math, html, sys
W,H=3550,2380
GREEN="#108a2e"; LINE="#6b6f75"; BG="#202124"
# entity: name -> (x,y, [(attr, angle_deg)], radius); key attrs start with '*'; 0 deg = right, 90 = up
E={
 "APP_USER":        (900, 1100, [("*email",75),("name",105),("roles",160),("active",15),("data",-15)], 235),
 "APP_SESSION":     (400, 450, [("*token",90),("email",150),("data",200),("updated_at",20)], 185),
 "AUDIT_LOG":       (250, 1100, [("*id",90),("at",135),("who",180),("action",225),("detail",270)], 190),
 "FEEDBACK_LOG":    (350, 1850, [("*id",120),("fb_id",175),("rec",225),("created_at",300)], 185),
 "REQUEST_EVENTS":  (900, 1980, [("*id",150),("req_date",195),("seq",240),("event_type",295),("req_id",340),("rec",30)], 210),
 "RUN_INDEX":       (1450, 1550, [("*id",60),("run_id",15),("rec",-30),("created_at",200)], 195),
 "RUN_FILES":       (1450, 2120, [("*run_id",170),("data",215),("bytes",0),("deleted",-40)], 175),
 "UPLOAD_BATCH":    (1350, 550, [("*id",90),("upload_id",55),("rec",15),("created_at",170)], 200),
 "UPLOAD_LOG":      (950, 150, [("*id",180),("rec",225),("created_at",5)], 185),
 "DATASET_VERSION": (1950, 800, [("*dkey",200),("*version",235),("data",270),("bytes",15),("created_at",50)], 205),
 "APP_SETTINGS":    (1950, 200, [("*name",150),("value",30),("updated_at",90)], 160),
 "API_MASTER":      (2050, 1960, [("*Api_Id",90),("Api_Name",140),("Task_Name",190),("Api_Url",240),("Api_Http_Method",300),("Api_Authorization",350),("Active_Status",40)], 215),
 "SAP_TECH_USERS":  (2650, 350, [("*Id",90),("User_Name",130),("Source_System",170),("Password_Hash",210),("Active",55),("Locked_Until",20)], 205),
 "SAP_DECISION_LOG":(2650, 1020, [("*Id",125),("Decision_Id",155),("Request_Id",185),("Po_Number",215),("Mode",245),("Overall_Verdict",-35),("Route_To",-70)], 225),
 "SAP_DECISION_ITEMS":(2650,1720,[("*Id",118),("Decision_Log_Id",183),("Po_Item",213),("Generic_Article",240),("Colour",273),("Order_Qty",305),("Verdict",28),("Max_Qty",58)], 275),
 "SAP_EVENTS":      (3200, 700, [("*Id",80),("Msg_Id",45),("Event_Type",15),("Po_Number",-15),("Decision_Id",-45),("Status",-80)], 200),
 "SAP_REASON_CODES":(3220, 1850,[("*Code_Type",90),("*Code",45),("Text",0),("Verdict",-45),("Active",-90)], 190),
}
R=[
 ("Logs in","APP_USER","APP_SESSION","1","M",None),
 ("Writes","APP_USER","AUDIT_LOG","1","M",None),
 ("Gives","APP_USER","FEEDBACK_LOG","1","M",None),
 ("Raises","APP_USER","REQUEST_EVENTS","1","M",None),
 ("Runs","APP_USER","RUN_INDEX","1","M",None),
 ("Stores","RUN_INDEX","RUN_FILES","1","1",None),
 ("Uploads","APP_USER","UPLOAD_BATCH","1","M",None),
 ("Logged in","UPLOAD_BATCH","UPLOAD_LOG","1","M",None),
 ("Contains","UPLOAD_BATCH","DATASET_VERSION","1","M",None),
 ("Points to","APP_SETTINGS","DATASET_VERSION","1","M",None),
 ("Requests","SAP_TECH_USERS","SAP_DECISION_LOG","1","M",None),
 ("Has","SAP_DECISION_LOG","SAP_DECISION_ITEMS","1","M",None),
 ("Sends","SAP_TECH_USERS","SAP_EVENTS","1","M",None),
 ("Outcome of","SAP_DECISION_LOG","SAP_EVENTS","1","M",None),
 ("Explained by","SAP_DECISION_ITEMS","SAP_REASON_CODES","M","1",None),
 ("Decided on","SAP_DECISION_ITEMS","DATASET_VERSION","M","M",(2250,1330)),
]
def tw(s,size): return len(s)*size*0.58
out=[]; lines=[]; shapes=[]
ebox={}
for n,(x,y,attrs,r) in E.items():
    label=n.replace("_"," ")
    w=max(170,tw(label,22)+40); h=62
    ebox[n]=(x,y,w,h)
def edge(n,tx,ty):
    x,y,w,h=ebox[n]; dx,dy=tx-x,ty-y
    if dx==0 and dy==0: return x,y
    s=min((w/2)/abs(dx) if dx else 1e9,(h/2)/abs(dy) if dy else 1e9)
    return x+dx*s,y+dy*s
# attributes
for n,(x,y,attrs,r) in E.items():
    for a,deg in attrs:
        ang=math.radians(deg)
        key=a.startswith("*"); t=a.lstrip("*")
        ax=x+r*math.cos(ang); ay=y-r*math.sin(ang)
        ow=max(110,tw(t,18)+36); oh=44
        lines.append(f'<line x1="{x:.0f}" y1="{y:.0f}" x2="{ax:.0f}" y2="{ay:.0f}" stroke="{LINE}" stroke-width="1.4"/>')
        deco=' text-decoration="underline"' if key else ''
        shapes.append(f'<ellipse cx="{ax:.0f}" cy="{ay:.0f}" rx="{ow/2:.0f}" ry="{oh/2}" fill="#fff" stroke="{GREEN}" stroke-width="2"/>'
                      f'<text x="{ax:.0f}" y="{ay+6:.0f}" font-size="18"{deco}>{html.escape(t)}</text>')
# relationships
for (lab,a,b,ca,cb,pos) in R:
    ax,ay=E[a][0],E[a][1]; bx,by=E[b][0],E[b][1]
    mx,my=pos if pos else ((ax+bx)/2,(ay+by)/2)
    dw=max(150,tw(lab,19)+70); dh=74
    lines.append(f'<line x1="{ax}" y1="{ay}" x2="{mx:.0f}" y2="{my:.0f}" stroke="{LINE}" stroke-width="1.6"/>')
    lines.append(f'<line x1="{mx:.0f}" y1="{my:.0f}" x2="{bx}" y2="{by}" stroke="{LINE}" stroke-width="1.6"/>')
    for (n,c,tx,ty) in ((a,ca,mx,my),(b,cb,mx,my)):
        ex,ey=edge(n,tx,ty); lx=ex+(tx-ex)*0.18; ly=ey+(ty-ey)*0.18
        shapes.append(f'<text x="{lx+10:.0f}" y="{ly-6:.0f}" font-size="17" fill="#3ddc6a" font-weight="700">{c}</text>')
    shapes.append(f'<polygon points="{mx-dw/2:.0f},{my:.0f} {mx:.0f},{my-dh/2:.0f} {mx+dw/2:.0f},{my:.0f} {mx:.0f},{my+dh/2:.0f}" fill="#fff" stroke="{GREEN}" stroke-width="2"/>'
                  f'<text x="{mx:.0f}" y="{my+7:.0f}" font-size="19">{html.escape(lab)}</text>')
for n,(x,y,w,h) in ebox.items():
    shapes.append(f'<rect x="{x-w/2:.0f}" y="{y-h/2:.0f}" width="{w:.0f}" height="{h}" fill="#fff" stroke="{GREEN}" stroke-width="2.5"/>'
                  f'<text x="{x}" y="{y+8}" font-size="22">{html.escape(n.replace("_"," "))}</text>')
title=('<text x="40" y="2355" font-size="20" fill="#9aa0a6" font-weight="400" text-anchor="start">VMM PO Governance — database entity diagram (SQL Server). '
       'Left: portal tables (01_tables.sql, 04_api_master.sql). Right: SAP interface tables (05_sap_interface.sql). '
       'Underlined = key. Links are logical (by e-mail / IDs / JSON), not FOREIGN KEY constraints.</text>')
svg=(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">'
     f'<rect width="100%" height="100%" fill="{BG}"/>'
     f'<g font-family="Arial, Helvetica, sans-serif" font-weight="700" fill="{GREEN}" text-anchor="middle">'
     +"".join(lines)+"".join(shapes)+title+'</g></svg>')
open(sys.argv[1],"w").write(svg)
