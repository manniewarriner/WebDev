import math
# Lincoln Cathedral west front, drawn in a 0..680 x 0..400 "photo" space, placed with a transform.
import sys
C=340; P=[]; D=[]; R=[]
def r(x0,y0,x1,y1,L=P): L.append(f"M{x0} {y1}V{y0}H{x1}V{y1}Z")
def pin(x,w,base,top,tip,L=P):  # octagonal shaft + spirelet
    L.append(f"M{x} {base}V{top}L{x+w/2} {tip}L{x+w} {top}V{base}Z")
def arch(x,w,top,bot,L=D):      # pointed lancet
    m=x+w/2; L.append(f"M{x} {bot}V{top+w*.6}Q{x} {top+w*.15} {m} {top}Q{x+w} {top+w*.15} {x+w} {top+w*.6}V{bot}Z")
def rnd(x,w,top,bot,L=D):       # Norman round arch
    rr=w/2; L.append(f"M{x} {bot}V{top+rr}A{rr} {rr} 0 0 1 {x+w} {top+rr}V{bot}Z")
# --- lead roofs either side, behind the screen
R.append("M120 318L150 296H222V318Z"); R.append("M560 318L530 296H458V318Z")
# --- central crossing tower (behind, tallest)
r(282,150,398,320)
for x in (282,306,330,354,378): pin(x,20 if x in(282,378) else 14,160,138 if x in(282,378) else 146,96 if x in(282,378) else 116)
for x in (300,326,352,374): arch(x,10,172,226)
# --- twin west towers
for t0 in (214,386):
    t1=t0+80; r(t0,168,t1,320)
    P.append(f"M{t0-3} 168H{t1+3}V175H{t0-3}Z")              # parapet band
    pin(t0-3,13,180,150,108); pin(t1-10,13,180,150,108)        # corner pinnacles
    pin(t0+22,9,172,152,126); pin(t1-31,9,172,152,126)         # intermediate pinnacles
    for x in (t0+14,t0+30,t0+46,t0+58): arch(x,9,186,250)      # belfry lancets
    for x in (t0+12,t0+24,t0+36,t0+48,t0+60): arch(x,7,262,296)
    D.append(f"M{t0} 254H{t1}V257H{t0}Z")                      # string course
# --- the image screen
r(140,296,540,400)
r(136,398,544,470)                                             # foundation plinth, buried in the hill
for x in range(160,522,16): pin(x,6,298,292,282)              # cresting pinnacles
P.append("M262 300L340 236L418 300Z")                          # central gable
pin(334,12,244,236,214)                                        # gable cross-pinnacle
for x in (140,522): pin(x,18,400,282,246)                      # outer stair turrets
for x in (256,412): pin(x,14,400,276,244)                      # turrets flanking the gable
# arcading: two rows of blind arches across the screen, skipping the recesses
for y0,y1,w,g in ((304,326,8,12),(334,354,8,12),(362,380,8,12)):
    x=146
    while x<534:
        if not (172<x<212 or 270<x<410 or 468<x<508): arch(x,w,y0,y1)
        x+=g
# the three great Norman recesses + doors
rnd(168,48,324,400,R); rnd(464,48,324,400,R)                   # side recesses (lighter)
# (central recess now built as stone orders below)

rnd(312,56,328,400,[]) 
D.append("M314 400V352A26 26 0 0 1 366 352V400Z")              # great west door
rnd(180,24,350,400); rnd(476,24,350,400)                       # side doors
arch(318,44,248,300)                                           # great west window in the gable
D.append("M333 252a7 7 0 1 0 14 0a7 7 0 1 0 -14 0Z")
for x in (144,526): 
    for y in (310,340,370): D.append(f"M{x+4} {y}h3v12h-3z")    # turret slits


# ================= SOLID DETAIL: faces, reveals, bands (no outline drawing) =================
LIT=[]; SHD=[]; DEEP=[]; MID=[]; TOP=[]; TOPL=[]
def box(x0,y0,x1,y1,L): L.append(f"M{x0} {y0}H{x1}V{y1}H{x0}Z")
def band(x0,x1,y,h=3):                     # moulded course: lit top, shadow drip below
    box(x0,y,x1,y+1.2,LIT); box(x0,y+1.2,x1,y+h,SHD)
def pin_faces(x,w,base,top,tip):           # pinnacle: right half in shade
    m=x+w/2; SHD.append(f"M{m} {base}V{top}L{m} {tip}L{x+w} {top}V{base}Z"); LIT.append(f"M{x} {top}L{m} {tip}V{top+2}L{x+1} {top+1}Z")
def lancet_reveal(x,w,top,bot):            # opening set into the wall
    m=x+w/2
    DEEP.append(f"M{x} {bot}V{top+w*.6}Q{x} {top+w*.15} {m} {top}Q{x+w} {top+w*.15} {x+w} {top+w*.6}V{bot}Z")
    SHD.append(f"M{x} {bot}V{top+w*.6}Q{x} {top+w*.15} {m} {top}L{m} {top+2}Q{x+1.6} {top+w*.2} {x+1.6} {top+w*.6}V{bot}Z")
    box(x-1,bot,x+w+1,bot+1.6,LIT)          # sill
    LIT.append(f"M{x-1.6} {bot}V{top+w*.6}Q{x-1.6} {top+w*.1} {m} {top-1.6}L{m} {top}Q{x} {top+w*.15} {x} {top+w*.6}V{bot}Z")  # lit hood edge
def buttress(x,w,y0,y1,steps=(0,)):         # projecting buttress: lit front, shaded return
    box(x,y0,x+w,y1,MID); box(x+w,y0,x+w+2.4,y1,SHD); box(x,y0,x+1,y1,LIT)
    for y in steps: box(x-.5,y,x+w+2.9,y+1.2,LIT); box(x-.5,y+1.2,x+w+2.9,y+3,SHD)
# --- west towers
for t0 in (214,386):
    t1=t0+80
    box(t1-12,175,t1,320,SHD)                              # turning face in shade
    buttress(t0-2,7,175,320,(214,252,300)); buttress(t1-9,7,175,320,(214,252,300))
    for y in (182,214,252,300): band(t0,t1,y)
    for x in (t0+14,t0+30,t0+46,t0+58): lancet_reveal(x,9,186,250)
    for x in (t0+12,t0+24,t0+36,t0+48,t0+60): lancet_reveal(x,7,262,296)
    for x in (t0-3,t1-10): pin_faces(x,13,180,150,108)
    for x in (t0+22,t1-31): pin_faces(x,9,172,152,126)
    box(t0-3,168,t1+3,175,MID); band(t0-3,t1+3,168,2.4)    # parapet
# --- central tower
box(384,150,398,320,SHD)
buttress(280,7,150,320,(160,236)); buttress(391,7,150,320,(160,236))
for y in (160,236): band(282,398,y)
for x in (300,326,352,374): lancet_reveal(x,10,172,226)
for x in (282,378): pin_faces(x,20,160,138,96)
for x in (306,330,354): pin_faces(x,14,160,146,116)
# --- the screen: three tiers of arcading as recessed niches with shafts between
for y in (300,328,356,384): band(140,540,y,2.6)
SHD.append("M140 296H540V299H140Z")
for y0,y1,w,g in ((304,326,8,12),(334,354,8,12),(362,380,8,12)):
    x=146
    while x<534:
        if not (172<x<212 or 270<x<410 or 468<x<508):
            lancet_reveal(x,w,y0,y1); box(x+w+1,y0+4,x+w+3,y1,LIT)   # shaft
        x+=g
for x,w,t,tip in ((140,18,282,246),(522,18,282,246),(256,14,276,244),(412,14,276,244)):
    box(x+w*.5,t,x+w,400,SHD); box(x,t,x+1.2,400,LIT)
    for y in (300,330,360): band(x-1,x+w+1,y,2.4)
    pin_faces(x,w,t+2,t,tip)
# --- gable: coping as a solid band, niches with figures, rose window
SHD.append("M262 300L340 236L346 241L270 302Z"); LIT.append("M262 300L340 236L341 238.6L264 301.6Z")
SHD.append("M418 300L340 236L334 241L410 302Z"); LIT.append("M418 300L340 236L339 238.6L416 301.6Z")
for x in (290,302,374,386):
    lancet_reveal(x,8,276,296); MID.append(f"M{x+2.5} 296v-12a1.5 1.5 0 0 1 3 0v12z")
DEEP.append("M325 260a15 15 0 1 0 30 0a15 15 0 1 0 -30 0Z")
LIT.append("M323 260a17 17 0 1 0 34 0a17 17 0 1 0 -34 0ZM325 260a15 15 0 1 1 30 0a15 15 0 1 1 -30 0Z")
for k in range(8):
    a=math.radians(k*45); MID.append(f"M340 260L{340+14*math.cos(a-0.12):.1f} {260+14*math.sin(a-0.12):.1f}L{340+14*math.cos(a+0.12):.1f} {260+14*math.sin(a+0.12):.1f}Z")
MID.append("M336.5 260a3.5 3.5 0 1 0 7 0a3.5 3.5 0 1 0 -7 0Z")
# --- great recesses: stepped orders as solid concentric rings (alternating lit / shaded)
def ring(x,w,top,t,L):
    r=w/2; ri=r-t; L.append(f"M{x} 400V{top+r}A{r} {r} 0 0 1 {x+w} {top+r}V400H{x+w-t}V{top+r}A{ri} {ri} 0 0 0 {x+t} {top+r}V400Z")
# Great west recess: four Norman orders stepping inwards, each a carved stone ring
# (stone face, lit outer arris, shaded inner return) like the rest of the masonry.
orders=((282,116,272),(289,102,279),(296,88,286),(303,74,293))
for x,w,top in orders:
    ring(x,w,top,7,MID)                    # stone order
    ring(x,w,top,1.2,LIT)                  # lit arris
    ring(x+5.4,w-10.8,top+5.4,1.6,SHD)     # shadow where it steps back
    for yy in range(int(top+w/2)+8,400,10): box(x,yy,x+7,yy+.9,SHD)   # jamb courses
    for yy in range(int(top+w/2)+8,400,10): box(x+w-7,yy,x+w,yy+.9,SHD)
# tympanum and recess back wall, then the door
MID.append("M310 400V330A30 30 0 0 1 370 330V400Z")
box(310,330,370,400,SHD)
TOP.append("M316 400V352A24 24 0 0 1 364 352V400Z")
_d=""
for px in range(322,360,6):                       # evenly spaced planks, cut to the arch
    yt=352-math.sqrt(24**2-(px-340)**2); _d+=f"M{px-.5} {yt:.1f}h1v{400-yt:.1f}h-1z"
for sy in (362,374,386):                          # evenly spaced iron straps
    _d+=f"M316 {sy}h48v1.4h-48z"
TOPL.append(_d)
TOPL.append("M313 400V352A27 27 0 0 1 367 352V400H364V352A24 24 0 0 0 316 352V400Z")
for x,top in ((168,324),(464,324)):
    ring(x,48,top,5,LIT); ring(x+5,38,top+5,5,SHD); DEEP.append(f"M{x+10} 400V{top+24}A14 14 0 0 1 {x+38} {top+24}V400Z")
# door leaves with iron straps



s=float(sys.argv[1]); tx=float(sys.argv[2]); ty=float(sys.argv[3])
T=f"matrix({s} 0 0 {s} {tx} {ty})"
print(f'''        <!-- LINCOLN CATHEDRAL west front (from Castle Hill): image screen with blind arcading and three Norman recesses, gable with the great west window, twin west towers and the taller central tower behind -->
        <g class="hero__cathedral" transform="{T}">
          <path fill="#8F99A3" stroke="#6E7882" d="{''.join(R[:2])}"/>
          <g fill="url(#tex-stone)"><g id="cath-shape"><path d="{''.join(P)}"/></g></g><use href="#cath-shape" fill="url(#hero-shade)"/>
          <path fill="#B98A58" opacity=".38" d="{''.join(R[2:])}"/>
        </g>
        <g fill="#6A4026" opacity=".72" transform="{T}"><path d="{''.join(D)}"/></g>
        <g class="hero__detail" transform="{T}">
          <path fill="#3E2414" opacity=".88" d="{''.join(DEEP)}"/>
          <path fill="#C9A77A" d="{''.join(MID)}"/><path fill="url(#tex-stone)" opacity=".55" d="{''.join(MID)}"/>
          <path fill="#5A341C" opacity=".42" d="{''.join(SHD)}"/>
          <path fill="#FFF3DC" opacity=".55" d="{''.join(LIT)}"/>
          <path fill="#3B2415" d="{''.join(TOP)}"/>
          <path fill="#1F130B" opacity=".75" d="{''.join(TOPL[:1])}"/>
          <path fill="#FFF3DC" opacity=".5" d="{''.join(TOPL[1:])}"/>
        </g>''')
