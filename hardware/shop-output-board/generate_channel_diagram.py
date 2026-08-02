#!/usr/bin/env python3
"""Draw one channel as schematic and as PCB placement, side by side.

The generated schematic is label-based and the layout plan is a table of coordinates —
neither shows how one becomes the other. This does, for a single channel, using the same
constants as layout.md so the picture cannot drift from the plan.

Output: channel-layout.svg
"""

import os

# --- Geometry, matching layout.md ------------------------------------------------------
COL_X = 15.0            # channel 1 column centre, mm
BARRIER_TOP = 38.0
BARRIER_BOT = 43.0
OPTO_CENTRE = 40.5
# Gull-wing SOP-4. The leads splay well outside the body, so the pad rows are 9.38mm apart
# against the DIP-4's 7.62mm — the SMD part bridges the barrier better than the through-hole
# one does.
OPTO_PAD_SPAN = 9.38
OPTO_BODY_W = 7.5
OPTO_BODY_H = 4.1

# Part Y positions within the column (mm)
Y = {
    "R5": 32.0,
    "U_logic": OPTO_CENTRE - OPTO_PAD_SPAN / 2,      # 35.81
    "U_iso": OPTO_CENTRE + OPTO_PAD_SPAN / 2,        # 45.19
    "R2": 50.0,
    "D": 56.0,
    "Q": 62.0,
    "R1": 68.0,
    "R3": 72.0,
    "R4": 76.0,
    "LED": 80.0,
}

SCALE = 9.0             # px per mm in the PCB panel
# Far enough right that the mm ruler, which sits ~30 px left of the board edge, clears the
# schematic panel's right border at x=520.
PCB_X0, PCB_Y0 = 665.0, 86.0
PCB_ORIGIN_MM_Y = 26.0  # top of the drawn region

C_LOGIC = "#2563eb"
C_ISO = "#b45309"
C_VPLUS = "#dc2626"
C_GND = "#475569"
C_GATE = "#7c3aed"
C_OUT = "#059669"
C_BODY = "#94a3b8"
C_PAD = "#cbd5e1"

out = []


def px(mm_x, mm_y):
    return PCB_X0 + (mm_x - COL_X) * SCALE, PCB_Y0 + (mm_y - PCB_ORIGIN_MM_Y) * SCALE


def rect(x, y, w, h, fill="none", stroke="#333", sw=1, rx=0, extra=""):
    out.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" '
               f'rx="{rx}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" {extra}/>')


def line(x1, y1, x2, y2, stroke="#333", sw=2, dash=""):
    d = f'stroke-dasharray="{dash}"' if dash else ""
    out.append(f'<line x1="{x1:.1f}" y1="{y1:.1f}" x2="{x2:.1f}" y2="{y2:.1f}" '
               f'stroke="{stroke}" stroke-width="{sw}" stroke-linecap="round" {d}/>')


def poly(points, stroke="#333", sw=2):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in points)
    out.append(f'<polyline points="{p}" fill="none" stroke="{stroke}" '
               f'stroke-width="{sw}" stroke-linecap="round" stroke-linejoin="round"/>')


def text(x, y, s, size=12, fill="#0f172a", anchor="start", weight="normal", family="ui-sans-serif, system-ui, sans-serif"):
    out.append(f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}" fill="{fill}" '
               f'text-anchor="{anchor}" font-weight="{weight}" font-family="{family}">{s}</text>')


def badge(x, y, n, colour):
    out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="9" fill="{colour}"/>')
    text(x, y + 4, str(n), size=11, fill="#fff", anchor="middle", weight="700")


# --- SVG frame --------------------------------------------------------------------------
W, H = 1240, 840
out.append(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">')
out.append('<rect width="100%" height="100%" fill="#f8fafc"/>')

text(30, 38, "One channel — schematic and board, side by side", size=21, weight="700")
text(30, 60, "Numbers match between the two panels. Channel 1 shown; the other seven are identical, 16 mm apart.",
     size=13, fill="#475569")

# ========================================================================================
# LEFT: schematic flow
# ========================================================================================
SX, SY = 60, 100
text(SX, SY - 12, "SCHEMATIC — what connects", size=14, weight="700", fill="#0f172a")

# domain bands
rect(SX - 20, SY, 480, 118, fill="#eff6ff", stroke="#bfdbfe", rx=6)
text(SX + 445, SY + 18, "LOGIC", size=11, fill=C_LOGIC, anchor="end", weight="700")
rect(SX - 20, SY + 150, 480, 400, fill="#fff7ed", stroke="#fed7aa", rx=6)
text(SX + 445, SY + 168, "ISOLATED (5–24 V)", size=11, fill=C_ISO, anchor="end", weight="700")

# barrier
out.append(f'<rect x="{SX-20}" y="{SY+120}" width="480" height="28" fill="#fee2e2" stroke="#fca5a5" rx="4"/>')
text(SX + 220, SY + 139, "ISOLATION BARRIER — no copper", size=11, fill="#b91c1c", anchor="middle", weight="700")

# logic chain: GP2 -> R5 -> opto LED -> PICO_GND
gy = SY + 60
text(SX, gy - 14, "Pico GP2", size=12, fill=C_LOGIC, weight="600")
line(SX + 5, gy, SX + 70, gy, C_LOGIC)
rect(SX + 70, gy - 10, 46, 20, fill="#fff", stroke=C_LOGIC, sw=2, rx=3)
text(SX + 93, gy + 4, "470R", size=11, anchor="middle")
badge(SX + 93, gy - 26, 1, C_LOGIC)
line(SX + 116, gy, SX + 180, gy, C_LOGIC)
# opto LED symbol
poly([(SX + 180, gy - 12), (SX + 180, gy + 12)], C_LOGIC, 2)
poly([(SX + 180, gy - 10), (SX + 196, gy), (SX + 180, gy + 10), (SX + 180, gy - 10)], C_LOGIC, 2)
line(SX + 196, gy, SX + 250, gy, C_LOGIC)
text(SX + 188, gy + 34, "opto LED", size=11, fill=C_LOGIC, anchor="middle")
badge(SX + 188, gy - 30, 2, C_LOGIC)
line(SX + 250, gy, SX + 250, gy + 42, C_GND)
line(SX + 238, gy + 42, SX + 262, gy + 42, C_GND, 3)
text(SX + 268, gy + 46, "PICO_GND", size=11, fill=C_GND)

# arrow through the barrier
out.append(f'<defs><marker id="a" markerWidth="9" markerHeight="9" refX="7" refY="4.5" orient="auto">'
           f'<path d="M0,0 L9,4.5 L0,9 z" fill="#b91c1c"/></marker></defs>')
line(SX + 188, gy + 40, SX + 188, SY + 176, "#b91c1c", 2, "5 4")
out.append(f'<line x1="{SX+188}" y1="{SY+150}" x2="{SX+188}" y2="{SY+178}" stroke="#b91c1c" '
           f'stroke-width="2" marker-end="url(#a)"/>')
text(SX + 198, SY + 172, "light, not current", size=10, fill="#b91c1c")

# isolated side: V+ rail
vy = SY + 200
line(SX - 10, vy, SX + 450, vy, C_VPLUS, 3)
text(SX - 10, vy - 8, "V+  (5–24 V)", size=12, fill=C_VPLUS, weight="700")

# R1 pull-up
line(SX + 40, vy, SX + 40, vy + 34, C_VPLUS)
rect(SX + 27, vy + 34, 26, 44, fill="#fff", stroke="#333", sw=2, rx=3)
text(SX + 40, vy + 60, "100k", size=10, anchor="middle")
badge(SX + 12, vy + 56, 3, C_ISO)
line(SX + 40, vy + 78, SX + 40, vy + 110, C_GATE)

# Zener
line(SX + 110, vy, SX + 110, vy + 40, C_VPLUS)
poly([(SX + 98, vy + 40), (SX + 122, vy + 40)], "#333", 2)
poly([(SX + 98, vy + 62), (SX + 110, vy + 40), (SX + 122, vy + 62), (SX + 98, vy + 62)], "#333", 2)
text(SX + 130, vy + 50, "8V2 Zener", size=11)
text(SX + 130, vy + 64, "clamps Vgs", size=10, fill="#64748b")
badge(SX + 84, vy + 50, 4, C_ISO)
line(SX + 110, vy + 62, SX + 110, vy + 110, C_GATE)

# gate rail
line(SX + 20, vy + 110, SX + 250, vy + 110, C_GATE, 3)
text(SX + 20, vy + 126, "GATE", size=12, fill=C_GATE, weight="700")

# opto transistor -> R2 -> gate
line(SX + 250, vy + 110, SX + 250, vy + 150, C_GATE)
rect(SX + 237, vy + 150, 26, 40, fill="#fff", stroke="#333", sw=2, rx=3)
text(SX + 250, vy + 174, "4k7", size=10, anchor="middle")
badge(SX + 285, vy + 170, 5, C_ISO)
line(SX + 250, vy + 190, SX + 250, vy + 216, C_ISO)
rect(SX + 228, vy + 216, 44, 34, fill="#fff", stroke=C_ISO, sw=2, rx=3)
text(SX + 250, vy + 237, "opto", size=10, anchor="middle", fill=C_ISO)
line(SX + 250, vy + 250, SX + 250, vy + 276, C_GND)
line(SX + 238, vy + 276, SX + 262, vy + 276, C_GND, 3)
text(SX + 268, vy + 280, "ISO_GND", size=11, fill=C_GND)

# MOSFET
line(SX + 175, vy, SX + 175, vy + 96, C_VPLUS)
rect(SX + 150, vy + 96, 50, 50, fill="#fff", stroke="#333", sw=2, rx=4)
text(SX + 175, vy + 118, "AO3401A", size=10, anchor="middle")
text(SX + 175, vy + 132, "P-MOS", size=9, anchor="middle", fill="#64748b")
badge(SX + 137, vy + 100, 6, C_ISO)
line(SX + 150, vy + 121, SX + 130, vy + 121, C_GATE)   # gate stub to rail
line(SX + 130, vy + 121, SX + 130, vy + 110, C_GATE)
text(SX + 205, vy + 104, "source ← V+", size=9, fill="#64748b")
text(SX + 205, vy + 142, "drain → OUT", size=9, fill="#64748b")

# OUT rail
line(SX + 175, vy + 146, SX + 175, vy + 176, C_OUT)
line(SX + 60, vy + 176, SX + 420, vy + 176, C_OUT, 3)
text(SX + 300, vy + 168, "OUT1", size=12, fill=C_OUT, weight="700")
poly([(SX + 420, vy + 168), (SX + 440, vy + 176), (SX + 420, vy + 184)], C_OUT, 2)
text(SX + 400, vy + 202, "to screw terminal", size=10, fill=C_OUT, anchor="middle")

# R3 pull-down
line(SX + 90, vy + 176, SX + 90, vy + 206, C_OUT)
rect(SX + 77, vy + 206, 26, 40, fill="#fff", stroke="#333", sw=2, rx=3)
text(SX + 90, vy + 230, "10k", size=10, anchor="middle")
badge(SX + 60, vy + 226, 7, C_ISO)
line(SX + 90, vy + 246, SX + 90, vy + 276, C_GND)
line(SX + 78, vy + 276, SX + 102, vy + 276, C_GND, 3)

# R4 + LED
line(SX + 150, vy + 176, SX + 150, vy + 206, C_OUT)
rect(SX + 137, vy + 206, 26, 34, fill="#fff", stroke="#333", sw=2, rx=3)
text(SX + 150, vy + 227, "2k2", size=10, anchor="middle")
badge(SX + 120, vy + 222, 8, C_ISO)
line(SX + 150, vy + 240, SX + 150, vy + 252, "#333")
poly([(SX + 138, vy + 252), (SX + 162, vy + 252)], "#333", 2)
poly([(SX + 138, vy + 252), (SX + 150, vy + 270), (SX + 162, vy + 252)], "#333", 2)
badge(SX + 178, vy + 262, 9, C_ISO)
text(SX + 190, vy + 250, "LED", size=11)
line(SX + 150, vy + 270, SX + 150, vy + 276, C_GND)
line(SX + 138, vy + 276, SX + 162, vy + 276, C_GND, 3)
text(SX + 96, vy + 296, "ISO_GND", size=11, fill=C_GND, anchor="middle")

# ========================================================================================
# RIGHT: PCB placement, to scale
# ========================================================================================
text(PCB_X0 - 60, SY - 12, "BOARD — where it physically sits (to scale)", size=14, weight="700")

top_x, top_y = px(COL_X - 9, PCB_ORIGIN_MM_Y)
bot_x, bot_y = px(COL_X + 9, 86.0)
rect(top_x, top_y, bot_x - top_x, bot_y - top_y, fill="#ffffff", stroke="#cbd5e1", rx=4)

# domain shading
_, by_top = px(0, BARRIER_TOP)
_, by_bot = px(0, BARRIER_BOT)
out.append(f'<rect x="{top_x}" y="{top_y}" width="{bot_x-top_x:.1f}" height="{by_top-top_y:.1f}" fill="#eff6ff"/>')
out.append(f'<rect x="{top_x}" y="{by_bot}" width="{bot_x-top_x:.1f}" height="{bot_y-by_bot:.1f}" fill="#fff7ed"/>')
out.append(f'<rect x="{top_x}" y="{by_top}" width="{bot_x-top_x:.1f}" height="{by_bot-by_top:.1f}" '
           f'fill="#fee2e2" stroke="#fca5a5"/>')
# Labelled outside the board: the optocoupler body sits in the middle of the barrier, so
# there is no room for text there.
text(bot_x + 12, (by_top + by_bot) / 2 + 4, "5 mm — NO COPPER", size=10,
     fill="#b91c1c", weight="700")

# mm ruler
for mm in range(30, 90, 10):
    _, ry = px(0, mm)
    line(top_x - 26, ry, top_x - 20, ry, "#94a3b8", 1)
    text(top_x - 30, ry + 4, f"{mm}", size=9, fill="#94a3b8", anchor="end")
text(top_x - 30, top_y - 8, "mm", size=9, fill="#94a3b8", anchor="end")


def part(mm_y, w_mm, h_mm, label, colour, n=None, pads=2, label_dx=0):
    """label_dx pushes a label further right, so parts only 4 mm apart do not collide."""
    cx, cy = px(COL_X, mm_y)
    w, h = w_mm * SCALE, h_mm * SCALE
    rect(cx - w / 2, cy - h / 2, w, h, fill="#fff", stroke=colour, sw=1.6, rx=2)
    if pads == 2:
        rect(cx - w / 2 - 3, cy - h / 2, 3, h, fill=C_PAD, stroke="none")
        rect(cx + w / 2, cy - h / 2, 3, h, fill=C_PAD, stroke="none")
    lx = cx + w / 2 + 10 + label_dx
    if label_dx:
        line(cx + w / 2 + 4, cy, lx - 4, cy, "#cbd5e1", 1)
    text(lx, cy + 4, label, size=10, fill="#0f172a")
    if n:
        badge(cx - w / 2 - 18, cy, n, colour)
    return cx, cy


# logic side
part(Y["R5"], 2.0, 1.25, "R105  470R", C_LOGIC, 1)

# optocoupler straddling the barrier
ux, _ = px(COL_X, OPTO_CENTRE)
_, uy_top = px(0, Y["U_logic"])
_, uy_bot = px(0, Y["U_iso"])
body_w = OPTO_BODY_W * SCALE
body_h = OPTO_BODY_H * SCALE
_, ucy = px(0, OPTO_CENTRE)
# Body sits between the pad rows; the gull-wing leads reach out past it to either side.
rect(ux - body_w / 2, ucy - body_h / 2, body_w, body_h, fill="#fff", stroke="#334155", sw=1.8, rx=3)
for dx in (-1.27, 1.27):
    for yy, col in ((uy_top, C_LOGIC), (uy_bot, C_ISO)):
        line(ux + dx * SCALE, yy, ux + dx * SCALE,
             ucy - body_h / 2 if yy < ucy else ucy + body_h / 2, "#64748b", 1.6)
        out.append(f'<rect x="{ux + dx*SCALE - 4:.1f}" y="{yy-4:.1f}" width="8" height="8" fill="{col}" rx="1"/>')
# Right-hand annotations are stacked deliberately: opto above the barrier, barrier label at
# its centre, V+ rail below. They are only ~20 px apart at this scale and will collide if
# any of them moves.
text(bot_x + 12, by_top - 22, "U1  LTV-817S  (SOP-4)", size=10)
text(bot_x + 12, by_top - 9, "the only part crossing the barrier", size=9, fill="#b91c1c")
line(ux + body_w / 2 + 3, uy_top, bot_x + 8, by_top - 18, "#cbd5e1", 1)
badge(ux - body_w / 2 - 18, uy_top, 2, C_LOGIC)

# isolated side
part(Y["R2"], 2.0, 1.25, "R102  4k7", C_ISO, 5)
part(Y["D"], 3.7, 1.6, "D1  Zener  SOD-123", C_ISO, 4)
qx, qy = px(COL_X, Y["Q"])
rect(qx - 1.45 * SCALE, qy - 0.65 * SCALE, 2.9 * SCALE, 1.3 * SCALE, fill="#fff", stroke=C_ISO, sw=1.6, rx=2)
for dx, dy in ((-0.95, 0.9), (0.95, 0.9), (0, -0.9)):
    out.append(f'<rect x="{qx + dx*SCALE - 3:.1f}" y="{qy + dy*SCALE - 3:.1f}" width="6" height="6" fill="{C_PAD}" rx="1"/>')
text(qx + 1.45 * SCALE + 10, qy + 4, "Q1  AO3401A  SOT-23", size=10)
badge(qx - 1.45 * SCALE - 18, qy, 6, C_ISO)
# These four sit only 4 mm apart, so the labels are fanned out to stay legible.
part(Y["R1"], 2.0, 1.25, "R101  100k   (gate pull-up)", C_ISO, 3, label_dx=0)
part(Y["R3"], 2.0, 1.25, "R103  10k    (output pull-down)", C_ISO, 7, label_dx=26)
part(Y["R4"], 2.0, 1.25, "R104  2k2", C_ISO, 8, label_dx=52)
part(Y["LED"], 2.0, 1.25, "LED1", C_ISO, 9, label_dx=78)

# V+ rail across the top of the isolated zone
_, vplus_y = px(0, 47.0)
line(top_x + 6, vplus_y, bot_x - 6, vplus_y, C_VPLUS, 3)
text(bot_x + 10, vplus_y + 4, "V+ rail  →  all 8 channels", size=10, fill=C_VPLUS, weight="600")

# GATE trace (the one real point-to-point net in a channel)
gx = ux - 3.2 * SCALE
_, r2y = px(0, Y["R2"])
_, r1y = px(0, Y["R1"])
poly([(ux - 1.27 * SCALE, r2y), (gx, r2y), (gx, qy), (qx - 0.95 * SCALE, qy + 0.9 * SCALE)], C_GATE, 2.4)
text(gx - 46, (r2y + qy) / 2, "GATE", size=10, fill=C_GATE, weight="700")

# OUT trace down to the terminal
ox = ux + 3.4 * SCALE
_, r3y = px(0, Y["R3"])
_, outy = px(0, 88.0)
poly([(qx + 0.95 * SCALE, qy + 0.9 * SCALE), (ox, qy + 0.9 * SCALE), (ox, outy)], C_OUT, 2.4)
text(ox + 6, outy - 6, "OUT1  →  J2 pin 1", size=10, fill=C_OUT, weight="700")

text((top_x + bot_x) / 2, bot_y + 22, "16 mm channel pitch", size=10, fill="#64748b", anchor="middle")

# ========================================================================================
# Footer note
# ========================================================================================
ny = 740
rect(30, ny, 1180, 78, fill="#f1f5f9", stroke="#cbd5e1", rx=6)
text(46, ny + 24, "Why routing is less work than the ratsnest suggests", size=13, weight="700")
text(46, ny + 46,
     "Only GATE and OUT are point-to-point traces. V+ is one rail shared by all eight channels, and every part marked ISO_GND or PICO_GND",
     size=12, fill="#334155")
text(46, ny + 64,
     "just drops a via into its ground pour — no trace to draw. Per channel that is two real traces, not eleven.",
     size=12, fill="#334155")

out.append("</svg>")

path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "channel-layout.svg")
with open(path, "w", encoding="utf-8") as fh:
    fh.write("\n".join(out))
print(f"wrote {path}")
