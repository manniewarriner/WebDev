"""Generate parts/bailgate.html: an illustrated panorama of Castle Square, Lincoln.

West to east (left to right), as seen from Castle Square:
  Lucy Tower on its mound, the Observatory Tower (square, with the round turret
  on top), the castle curtain wall and East Gate (Norman round arch with two
  semicircular tourelles above), Leigh-Pemberton House (1543 Tudor, triple
  gabled, two jettied floors), a Georgian brick house on Castle Hill, and the
  Exchequer Gate with Lincoln Cathedral's west front and towers behind.

Usage: python tools/build_bailgate.py
"""
import pathlib
import random

random.seed(11)
W, H, GROUND = 1600, 700, 600
out = []
add = out.append

def crenels(x0, x1, y, merlon=14, gap=10, h=12, fill="currentColor"):
    """A row of merlons sitting on y (their tops at y-h)."""
    d = []
    x = x0
    while x + merlon <= x1:
        d.append(f"M{x} {y}v{-h}h{merlon}v{h}z")
        x += merlon + gap
    return f'<path d="{"".join(d)}" fill="{fill}"/>'

def coursing(x0, x1, y0, y1, step=18, color="#000", opacity=.06):
    lines = "".join(f"M{x0} {y}H{x1}" for y in range(int(y0) + step, int(y1), step))
    return f'<path d="{lines}" stroke="{color}" stroke-opacity="{opacity}" stroke-width="1.5"/>'

def lancet(x, y, w, h, fill="#4A3F31"):
    r = w / 2
    return f'<path d="M{x} {y + h}V{y + r}a{r} {r} 0 0 1 {w} 0V{y + h}z" fill="{fill}"/>'

def leaded(x, y, w, h, cls="bg-win"):
    """A leaded casement window with a glint overlay."""
    return (
        f'<g class="{cls}">'
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="url(#bg-glass)"/>'
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="url(#bg-lattice)"/>'
        f'<path d="M{x + w / 2} {y}v{h}" stroke="#2A211A" stroke-width="3"/>'
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="none" stroke="#2A211A" stroke-width="4"/>'
        f'<path class="bg-glint" d="M{x} {y + h}L{x + w * .55} {y}h{w * .22}L{x + w * .22} {y + h}z" fill="#fff" opacity="0"/>'
        f'</g>'
    )

def sash(x, y, w, h):
    return (
        f'<g class="bg-win">'
        f'<rect x="{x - 4}" y="{y - 4}" width="{w + 8}" height="{h + 8}" fill="#F3EEE2"/>'
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="url(#bg-glass)"/>'
        f'<path d="M{x} {y + h / 2}h{w}M{x + w / 2} {y}v{h}M{x} {y + h / 4}h{w}M{x} {y + 3 * h / 4}h{w}" stroke="#F3EEE2" stroke-width="2.5"/>'
        f'<path class="bg-glint" d="M{x} {y + h}L{x + w * .6} {y}h{w * .25}L{x + w * .25} {y + h}z" fill="#fff" opacity="0"/>'
        f'</g>'
    )

# ---------------------------------------------------------------- defs
add(f'''<svg class="bailgate__svg" viewBox="0 0 {W} {H}" role="img" aria-labelledby="bg-title bg-desc">
  <title id="bg-title">Illustration of Castle Square in uphill Lincoln</title>
  <desc id="bg-desc">Lincoln Castle's East Gate and Observatory Tower on the left, the Tudor Leigh-Pemberton House in the middle, and the Exchequer Gate with Lincoln Cathedral's towers on the right, with a window cleaner using a water-fed pole.</desc>
  <defs>
    <linearGradient id="bg-sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#BFE1FB"/><stop offset=".7" stop-color="#E6F4FF"/><stop offset="1" stop-color="#F6FBFF"/></linearGradient>
    <linearGradient id="bg-stone" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#E4D3AE"/><stop offset="1" stop-color="#C9B386"/></linearGradient>
    <linearGradient id="bg-stone-dark" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#D3BE92"/><stop offset="1" stop-color="#B49E72"/></linearGradient>
    <linearGradient id="bg-cath" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#D9D3C4"/><stop offset="1" stop-color="#C3BAA4"/></linearGradient>
    <linearGradient id="bg-glass" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#DFF3FF"/><stop offset=".5" stop-color="#8FCBEF"/><stop offset="1" stop-color="#4E98C9"/></linearGradient>
    <linearGradient id="bg-grass" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#8DB273"/><stop offset="1" stop-color="#6A9256"/></linearGradient>
    <linearGradient id="bg-cobble" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#CFC4AE"/><stop offset="1" stop-color="#B3A68C"/></linearGradient>
    <pattern id="bg-lattice" width="10" height="12" patternUnits="userSpaceOnUse"><path d="M0 0l10 12M10 0L0 12" stroke="#2A211A" stroke-opacity=".45" stroke-width="1"/></pattern>
    <radialGradient id="bg-sun" cx="50%" cy="50%" r="50%"><stop offset="0" stop-color="#fff" stop-opacity=".95"/><stop offset=".35" stop-color="#FFF4D6" stop-opacity=".5"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient>
  </defs>
  <rect width="{W}" height="{H}" fill="url(#bg-sky)"/>
  <circle cx="1460" cy="90" r="190" fill="url(#bg-sun)"/>
''')

# ---------------------------------------------------------------- clouds (slowest layer)
add('<g class="bg-layer" data-depth="0.15" fill="#fff">')
for cx, cy, s in [(220, 90, 1), (760, 60, .8), (1180, 120, .7), (520, 170, .55)]:
    add(f'<g opacity=".8" transform="translate({cx} {cy}) scale({s})"><ellipse cx="0" cy="0" rx="90" ry="26"/><ellipse cx="-40" cy="-12" rx="46" ry="28"/><ellipse cx="30" cy="-20" rx="54" ry="34"/></g>')
add('</g>')

# ---------------------------------------------------------------- cathedral west front (background)
c = []
c.append('<g class="bg-layer bg-cathedral" data-depth="0.3">')
# central tower, furthest back, hazier
c.append('<g opacity=".75"><rect x="1236" y="70" width="62" height="330" fill="url(#bg-cath)"/>')
c.append('<path d="M1236 70l5-26 5 26zM1293 70l5-26 5 26zM1262 70l5-18 5 18z" fill="#CFC8B6"/>')
c.append(lancet(1250, 110, 12, 70, "#9C927E") + lancet(1272, 110, 12, 70, "#9C927E") + '</g>')
# west towers with corner pinnacles
for x in (1128, 1338):
    c.append(f'<rect x="{x}" y="118" width="74" height="300" fill="url(#bg-cath)"/>')
    c.append(f'<path d="M{x} 118l6-34 6 34zM{x + 62} 118l6-34 6 34zM{x + 30} 118l7-22 7 22z" fill="#D9D3C4"/>')
    c.append(crenels(x, x + 74, 118, merlon=8, gap=6, h=8, fill="#D9D3C4"))
    c.append(lancet(x + 14, 160, 14, 96, "#8E8572") + lancet(x + 46, 160, 14, 96, "#8E8572"))
    c.append(f'<path d="M{x} 280h74" stroke="#A9A08A" stroke-width="3"/>')
# west screen
c.append('<rect x="1078" y="288" width="372" height="140" fill="url(#bg-cath)"/>')
c.append('<path d="M1078 300h372" stroke="#A9A08A" stroke-width="2"/>')
c.append("".join(lancet(x, 316, 10, 40, "#A39985") for x in range(1090, 1440, 22)))
c.append('</g>')
add("".join(c))

# ---------------------------------------------------------------- castle (left)
k = []
k.append('<g class="bg-layer bg-castle" data-depth="0.5">')
# mounds
k.append('<path d="M-20 600C10 470 60 420 110 410s110 40 150 190z" fill="url(#bg-grass)"/>')
k.append('<path d="M180 600c30-150 80-200 120-205s95 40 135 205z" fill="url(#bg-grass)"/>')
# Lucy Tower: polygonal shell keep on its mound (roofless, buttressed)
k.append('<path d="M40 428V300l18-12h76l18 12v128z" fill="url(#bg-stone-dark)"/>')
k.append(crenels(40, 152, 300, merlon=12, gap=8, h=11, fill="#C6AF82"))
k.append('<path d="M58 288v140M96 288v140M134 288v140" stroke="#A88F62" stroke-width="4" stroke-opacity=".6"/>')
k.append(coursing(40, 152, 300, 428))
# Observatory Tower with its round turret
k.append('<rect x="250" y="190" width="96" height="220" fill="url(#bg-stone)"/>')
k.append('<rect x="244" y="178" width="108" height="16" fill="#D7C49D"/>')
k.append(crenels(244, 352, 178, merlon=12, gap=8, h=13, fill="#E4D3AE"))
k.append('<rect x="282" y="120" width="34" height="58" fill="url(#bg-stone)"/>')
k.append('<rect x="278" y="114" width="42" height="9" fill="#D7C49D"/>')
k.append(crenels(278, 320, 114, merlon=8, gap=6, h=9, fill="#E4D3AE"))
k.append(lancet(293, 136, 10, 26) + lancet(266, 220, 10, 34) + lancet(320, 220, 10, 34) + lancet(293, 300, 10, 34))
k.append(coursing(250, 346, 194, 410))
# curtain wall
k.append('<path d="M150 600V346h300v254z" fill="url(#bg-stone-dark)"/>')
k.append(crenels(150, 450, 346, merlon=16, gap=10, h=13, fill="#C6AF82"))
k.append(coursing(150, 450, 346, 600, step=20, opacity=.08))
# East Gate: Norman round arch, two semicircular tourelles above, crenellated
k.append('<path d="M430 600V318h190v282z" fill="url(#bg-stone)"/>')
k.append(crenels(424, 626, 318, merlon=16, gap=10, h=14, fill="#E4D3AE"))
k.append('<rect x="424" y="318" width="202" height="10" fill="#D7C49D"/>')
for tx in (450, 600):  # tourelles corbelled out above the arch
    k.append(f'<path d="M{tx - 22} 340h44v66c0 12-10 20-22 26-12-6-22-14-22-26z" fill="#E9DAB8"/>')
    k.append(f'<path d="M{tx - 22} 406h44M{tx - 18} 414h36M{tx - 12} 422h24" stroke="#B9A274" stroke-width="2"/>')
    k.append(lancet(tx - 5, 356, 10, 30))
    k.append(crenels(tx - 22, tx + 22, 340, merlon=8, gap=4, h=8, fill="#E9DAB8"))
k.append('<path d="M478 600V500a47 47 0 0 1 94 0v100z" fill="#3A3226"/>')
k.append('<path d="M470 600V500a55 55 0 0 1 110 0v100" fill="none" stroke="#D2BE95" stroke-width="8"/>')
k.append('<path d="M462 600V500a63 63 0 0 1 126 0v100" fill="none" stroke="#C3AC7E" stroke-width="5"/>')
k.append('<path d="M490 600V512a35 35 0 0 1 70 0v88" fill="#4F4536"/>')
k.append(coursing(430, 620, 328, 600, step=20, opacity=.07))
# flag on the gate
k.append('<path d="M525 318V262" stroke="#2A211A" stroke-width="3"/>')
k.append('<path class="bg-flag" d="M527 264h34l-8 10 8 10h-34z" fill="#C9A35B"/>')
k.append('</g>')
add("".join(k))

# ---------------------------------------------------------------- Castle Square buildings (mid)
b = []
b.append('<g class="bg-layer bg-square" data-depth="0.7">')
# tree between castle and the Tudor house
b.append('<path d="M660 600v-80" stroke="#5B4632" stroke-width="10"/>')
b.append('<g fill="#6E9A5A"><circle cx="660" cy="500" r="42"/><circle cx="628" cy="520" r="30"/><circle cx="692" cy="522" r="30"/><circle cx="660" cy="468" r="28"/></g>')

# Leigh-Pemberton House (1543): rubble plinth, two jettied floors, triple gables
LX0, LX1 = 720, 990
b.append(f'<rect x="{LX0 + 10}" y="500" width="{LX1 - LX0 - 20}" height="100" fill="#F3EEE2"/>')
b.append(f'<rect x="{LX0 + 10}" y="578" width="{LX1 - LX0 - 20}" height="22" fill="#B8A887"/>')
b.append(f'<rect x="{LX0}" y="420" width="{LX1 - LX0}" height="80" fill="#F3EEE2"/>')
b.append(f'<rect x="{LX0 - 10}" y="338" width="{LX1 - LX0 + 20}" height="82" fill="#F3EEE2"/>')
gw = (LX1 - LX0 + 20) / 3
for i in range(3):
    gx = LX0 - 10 + i * gw
    b.append(f'<path d="M{gx} 338L{gx + gw / 2} 262L{gx + gw} 338z" fill="#F3EEE2" stroke="#2A211A" stroke-width="6" stroke-linejoin="round"/>')
    b.append(f'<path d="M{gx + gw / 2} 270V338M{gx + gw * .25} 300L{gx + gw / 2} 330L{gx + gw * .75} 300" stroke="#2A211A" stroke-width="4" fill="none"/>')
b.append(f'<path d="M{LX0 - 14} 338h{LX1 - LX0 + 28}M{LX0 - 4} 420h{LX1 - LX0 + 8}M{LX0 + 6} 500h{LX1 - LX0 - 12}" stroke="#2A211A" stroke-width="7"/>')
# brackets under the jetties
for x in range(LX0 + 20, LX1 - 10, 60):
    b.append(f'<path d="M{x} 424l-10 0 10 18zM{x + 4} 504l-10 0 10 16z" fill="#2A211A"/>')
# studs and arch braces on the upper floors
for x in range(LX0, LX1 + 1, 30):
    b.append(f'<path d="M{x} 342v76" stroke="#2A211A" stroke-width="4"/>')
for x in range(LX0 + 10, LX1, 30):
    b.append(f'<path d="M{x} 424v74" stroke="#2A211A" stroke-width="4"/>')
for x in range(LX0, LX1, 90):
    b.append(f'<path d="M{x + 2} 498q28-40 56-74" stroke="#2A211A" stroke-width="4" fill="none"/>')
# windows
for i in range(3):
    gx = LX0 - 10 + i * gw
    b.append(leaded(gx + gw / 2 - 30, 352, 60, 52))
for x in (LX0 + 30, LX0 + 150):
    b.append(leaded(x, 434, 74, 52))
b.append(leaded(LX0 + 40, 516, 110, 56))
b.append(f'<path d="M{LX1 - 70} 600v-74h40v74z" fill="#2A211A"/><circle cx="{LX1 - 38}" cy="566" r="2.5" fill="#C9A35B"/>')
# roof and chimney behind the gables
b.append(f'<path d="M{LX1 - 40} 262h22v-40h-22z" fill="#9A4E37"/>')

# Georgian brick house on Castle Hill
GX0, GX1 = 1000, 1100
b.append(f'<rect x="{GX0}" y="388" width="{GX1 - GX0}" height="212" fill="#B0603F"/>')
b.append(f'<rect x="{GX0 - 4}" y="380" width="{GX1 - GX0 + 8}" height="12" fill="#E8DCC6"/>')
for row in (408, 478):
    for x in (GX0 + 14, GX0 + 58):
        b.append(sash(x, row, 28, 48))
b.append(f'<path d="M{GX0 + 36} 600v-56h28v56z" fill="#1B4268"/><path d="M{GX0 + 32} 544h36" stroke="#E8DCC6" stroke-width="4"/>')

# Exchequer Gate: crenellated, big central arch and two side passages
EX0, EX1 = 1100, 1470
b.append(f'<rect x="{EX0}" y="400" width="{EX1 - EX0}" height="200" fill="url(#bg-stone)"/>')
b.append(f'<rect x="{EX0 - 6}" y="392" width="{EX1 - EX0 + 12}" height="12" fill="#D7C49D"/>')
b.append(crenels(EX0 - 6, EX1 + 6, 392, merlon=16, gap=10, h=14, fill="#E4D3AE"))
b.append(coursing(EX0, EX1, 404, 600, step=20, opacity=.07))
b.append(f'<path d="M1238 600V516a47 47 0 0 1 94 0v84z" fill="#3A3226"/>')
b.append(f'<path d="M1230 600V516a55 55 0 0 1 110 0v84" fill="none" stroke="#D2BE95" stroke-width="7"/>')
for ax in (1150, 1390):
    b.append(f'<path d="M{ax} 600v-46a18 18 0 0 1 36 0v46z" fill="#3A3226"/>')
for x in (1150, 1210, 1340, 1400):
    b.append(sash(x, 424, 26, 40))
b.append('</g>')
add("".join(b))

# ---------------------------------------------------------------- foreground: cobbles, lamps, window cleaner
f = []
f.append('<g class="bg-layer bg-front" data-depth="1">')
f.append(f'<rect x="0" y="{GROUND}" width="{W}" height="{H - GROUND}" fill="url(#bg-cobble)"/>')
cob = []
for row, y in enumerate(range(GROUND + 8, H, 14)):
    rx = 9 + row * 1.6
    off = (row % 2) * rx
    x = -off
    while x < W:
        cob.append(f"M{x:.0f} {y}a{rx:.1f} 5 0 1 0 {2 * rx:.1f} 0a{rx:.1f} 5 0 1 0 {-2 * rx:.1f} 0z")
        x += 2 * rx + 3
f.append(f'<path d="{"".join(cob)}" fill="none" stroke="#8F8268" stroke-opacity=".35" stroke-width="1.2"/>')
f.append(f'<rect x="0" y="{GROUND}" width="{W}" height="6" fill="#A99C82"/>')
for lx in (700, 1110):
    f.append(f'<g fill="#1E242B"><rect x="{lx - 3}" y="470" width="6" height="134"/><rect x="{lx - 9}" y="596" width="18" height="8" rx="2"/>'
             f'<path d="M{lx - 13} 470h26l-4-34h-18z"/><path d="M{lx - 10} 436l10-12 10 12z"/></g>'
             f'<path d="M{lx - 9} 467h18l-3-28h-12z" fill="#FFE7A8" opacity=".9"/>')
# the window cleaner with a water-fed pole, cleaning the first-floor windows
f.append('<g class="bg-cleaner">')
f.append('<ellipse cx="900" cy="662" rx="26" ry="6" fill="#000" opacity=".15"/>')
f.append('<path d="M890 660v-44h8v44zM902 660v-44h8v44z" fill="#1E242B"/>')
f.append('<rect x="884" y="574" width="32" height="46" rx="10" fill="#1B4268"/>')
f.append('<path d="M889 590l-4 22M911 590l4 22" stroke="#1B4268" stroke-width="8" stroke-linecap="round"/>')
f.append('<circle cx="900" cy="560" r="12" fill="#E8C2A0"/><path d="M887 556a13 13 0 0 1 26 0z" fill="#22C3EE"/>')
f.append('<g class="bg-pole"><path d="M906 596L822 416" stroke="#C5CDD5" stroke-width="5" stroke-linecap="round"/>'
         '<path d="M906 596L870 520" stroke="#22C3EE" stroke-width="5" stroke-linecap="round"/>'
         '<rect x="800" y="400" width="44" height="14" rx="4" fill="#1E242B" transform="rotate(-25 822 407)"/>'
         '<g class="bg-spray" fill="#BDE8FA"><circle cx="812" cy="424" r="3"/><circle cx="826" cy="430" r="2.4"/><circle cx="804" cy="436" r="2"/><circle cx="836" cy="420" r="2"/></g></g>')
f.append('</g>')
f.append('</g>')
add("".join(f))
add('</svg>')

svg = "\n".join(out)

html = f'''<section id="lincoln" class="section bailgate" aria-labelledby="bailgate-title">
  <div class="container bailgate__head">
    <div class="section-head">
      <p class="eyebrow">Proudly local</p>
      <h2 id="bailgate-title" class="h2" data-split>From the <em>Bailgate</em> to your doorstep</h2>
      <p class="lead" data-reveal>Lincoln is home turf. From the castle walls and the Tudor timbers of Castle Square to terraces and new-builds right across the city, I keep Lincoln's windows as bright as its history.</p>
    </div>
    <a class="btn btn--brass bailgate__cta" href="#quote" data-reveal>Book a clean near you
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M5 12h14M13 6l6 6-6 6"/></svg>
    </a>
  </div>

  <div class="bailgate__scroller" tabindex="0" aria-label="Castle Square panorama, scroll sideways to explore">
    <div class="bailgate__scene">
      {svg}
      <span class="bailgate__tag" style="--x: 32.8%; --y: 41%">Lincoln Castle · East Gate</span>
      <span class="bailgate__tag" style="--x: 18.6%; --y: 13%">Observatory Tower</span>
      <span class="bailgate__tag" style="--x: 53.4%; --y: 33%">Leigh-Pemberton House, 1543</span>
      <span class="bailgate__tag" style="--x: 80.3%; --y: 52%">Exchequer Gate</span>
      <span class="bailgate__tag" style="--x: 80.3%; --y: 8%">Lincoln Cathedral</span>
    </div>
  </div>
  <p class="bailgate__swipe" aria-hidden="true">← Swipe to explore Castle Square →</p>
</section>
'''

root = pathlib.Path(__file__).resolve().parent.parent
(root / "parts" / "bailgate.html").write_text(html, encoding="utf-8")
print(f"wrote parts/bailgate.html ({len(html) // 1024} KB)")
