#!/usr/bin/env python3
"""Draw the input power section as schematic and as PCB placement, side by side.

The channels are a block repeated eight times; this section is a series chain with
satellites hanging off it, which reads quite differently and is where the flow gets hard to
follow. Same conventions as generate_channel_diagram.py — numbered badges match across the
two panels.

Output: power-layout.svg
"""

import os

# --- Placement, mm. These are the X positions layout.md was missing. --------------------
# The chain runs left to right in the order current actually travels, ending at the board
# centre so the V+ riser feeds the rail in both directions.
P = {
    "J1":   (16.0, 103.0),
    "F1":   (28.0, 92.0),
    "Q9":   (38.0, 92.0),
    "R6":   (34.0, 86.0),      # Q9's gate satellites, on their own row just above it
    "D9":   (46.0, 86.0),
    "TVS1": (50.0, 92.0),
    "C1":   (62.0, 92.0),      # 8 mm electrolytic, needs the room
    "C2":   (72.0, 92.0),
}
RISER_X = 71.0                 # lands in the gap between channel 4 (x=63) and channel 5 (x=79)
VPLUS_RAIL_Y = 47.0
BOARD_W, BOARD_H = 150.0, 115.0

SCALE = 4.1
PCB_X0, PCB_Y0 = 578.0, 96.0

C_LOGIC = "#2563eb"
C_ISO = "#b45309"
C_VPLUS = "#dc2626"
C_GND = "#475569"
C_GATE = "#7c3aed"
C_RAW = "#0891b2"

out = []


def px(x, y):
    return PCB_X0 + x * SCALE, PCB_Y0 + y * SCALE


def rect(x, y, w, h, fill="none", stroke="#333", sw=1, rx=0):
    out.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="{rx}" '
               f'fill="{fill}" stroke="{stroke}" stroke-width="{sw}"/>')


def line(x1, y1, x2, y2, stroke="#333", sw=2, dash=""):
    d = f'stroke-dasharray="{dash}"' if dash else ""
    out.append(f'<line x1="{x1:.1f}" y1="{y1:.1f}" x2="{x2:.1f}" y2="{y2:.1f}" stroke="{stroke}" '
               f'stroke-width="{sw}" stroke-linecap="round" {d}/>')


def poly(pts, stroke="#333", sw=2, fill="none"):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    out.append(f'<polyline points="{p}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" '
               f'stroke-linecap="round" stroke-linejoin="round"/>')


def text(x, y, s, size=12, fill="#0f172a", anchor="start", weight="normal"):
    out.append(f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}" fill="{fill}" '
               f'text-anchor="{anchor}" font-weight="{weight}" '
               f'font-family="ui-sans-serif, system-ui, sans-serif">{s}</text>')


def badge(x, y, n, colour="#b45309"):
    out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="9" fill="{colour}"/>')
    text(x, y + 4, str(n), size=11, fill="#fff", anchor="middle", weight="700")


W, H = 1240, 700
out.append(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">')
out.append('<rect width="100%" height="100%" fill="#f8fafc"/>')
text(30, 36, "Input power section — schematic and board", size=21, weight="700")
text(30, 58, "A series chain, not a repeating block. Place the parts in the order current travels through them.",
     size=13, fill="#475569")

# ========================================================================================
# LEFT — schematic, drawn as a left-to-right chain so it matches the board
# ========================================================================================
SX, SY = 60, 110
text(SX, SY - 14, "SCHEMATIC — the current path", size=14, weight="700")
rect(SX - 24, SY - 4, 490, 330, fill="#fff7ed", stroke="#fed7aa", rx=6)

cy = SY + 60
# J1 terminal
rect(SX - 10, cy - 26, 40, 52, fill="#fff", stroke=C_ISO, sw=2, rx=3)
text(SX + 10, cy - 4, "J1", size=11, anchor="middle", weight="700")
text(SX + 10, cy + 12, "IN", size=10, anchor="middle", fill="#64748b")
badge(SX + 10, cy - 40, 1)
text(SX - 14, cy + 44, "5–24 V from your supply", size=10, fill="#64748b")

# VIN_RAW -> F1
line(SX + 30, cy - 12, SX + 66, cy - 12, C_RAW, 2.5)
text(SX + 34, cy - 20, "VIN_RAW", size=9, fill=C_RAW)
rect(SX + 66, cy - 24, 42, 24, fill="#fff", stroke="#333", sw=2, rx=3)
text(SX + 87, cy - 8, "F1", size=10, anchor="middle")
text(SX + 87, cy - 34, "polyfuse", size=9, anchor="middle", fill="#64748b")
badge(SX + 87, cy - 52, 2)

# VIN_F -> Q9
line(SX + 108, cy - 12, SX + 148, cy - 12, C_RAW, 2.5)
text(SX + 110, cy - 20, "VIN_F", size=9, fill=C_RAW)
rect(SX + 148, cy - 30, 58, 36, fill="#fff", stroke="#333", sw=2, rx=4)
text(SX + 177, cy - 14, "Q9", size=10, anchor="middle", weight="700")
text(SX + 177, cy - 2, "AO3401A", size=9, anchor="middle", fill="#64748b")
badge(SX + 177, cy - 48, 3)
text(SX + 150, cy - 38, "drain", size=8, fill="#64748b")
text(SX + 186, cy - 38, "source", size=8, fill="#64748b")

# gate satellites
line(SX + 177, cy + 6, SX + 177, cy + 34, C_GATE, 2)
line(SX + 120, cy + 34, SX + 240, cy + 34, C_GATE, 2.5)
text(SX + 182, cy + 30, "Q9GATE", size=9, fill=C_GATE)
rect(SX + 108, cy + 34, 24, 40, fill="#fff", stroke="#333", sw=2, rx=3)
text(SX + 120, cy + 58, "R6", size=9, anchor="middle")
text(SX + 96, cy + 88, "100k", size=9, anchor="middle", fill="#64748b")
badge(SX + 90, cy + 54, 4)
line(SX + 120, cy + 74, SX + 120, cy + 108, C_GND, 2)
poly([(SX + 228, cy + 46), (SX + 252, cy + 46)], "#333", 2)
poly([(SX + 228, cy + 66), (SX + 240, cy + 46), (SX + 252, cy + 66), (SX + 228, cy + 66)], "#333", 2)
text(SX + 240, cy + 84, "D9  8V2", size=10, anchor="middle")
badge(SX + 214, cy + 56, 5)
line(SX + 240, cy + 34, SX + 240, cy + 46, C_GATE, 2)
line(SX + 240, cy + 66, SX + 240, cy - 12, C_VPLUS, 2)

# V+ rail
line(SX + 206, cy - 12, SX + 452, cy - 12, C_VPLUS, 3.5)
text(SX + 214, cy - 22, "V+", size=13, fill=C_VPLUS, weight="700")
poly([(SX + 452, cy - 20), (SX + 470, cy - 12), (SX + 452, cy - 4)], C_VPLUS, 2)
text(SX + 440, cy - 34, "to all 8 channels", size=10, fill=C_VPLUS, anchor="middle")

# shunt trio
for i, (dx, lbl, sub, n) in enumerate([(280, "TVS1", "SMAJ30A", 6),
                                       (340, "C1", "100µF", 7),
                                       (400, "C2", "100nF", 8)]):
    line(SX + dx, cy - 12, SX + dx, cy + 40, C_VPLUS, 2)
    rect(SX + dx - 16, cy + 40, 32, 34, fill="#fff", stroke="#333", sw=2, rx=3)
    text(SX + dx, cy + 56, lbl, size=10, anchor="middle", weight="600")
    text(SX + dx, cy + 68, sub, size=8, anchor="middle", fill="#64748b")
    badge(SX + dx, cy + 92, n)
    line(SX + dx, cy + 74, SX + dx, cy + 108, C_GND, 2)

# ISO_GND rail
line(SX + 10, cy + 108, SX + 452, cy + 108, C_GND, 3.5)
text(SX + 10, cy + 124, "ISO_GND", size=12, fill=C_GND, weight="700")
line(SX + 10, cy + 26, SX + 10, cy + 108, C_GND, 2)   # J1 pin 2

text(SX - 14, cy + 152, "These three are not in series — they hang off the rail, each one V+ to ground.",
     size=11, fill="#334155")
text(SX - 14, cy + 170, "Only J1→F1→Q9 carries the current in a line.", size=11, fill="#334155")

# ========================================================================================
# RIGHT — board placement
# ========================================================================================
text(PCB_X0, SY - 14, "BOARD — placement (to scale)", size=14, weight="700")

bx, by = px(0, 0)
rect(bx, by, BOARD_W * SCALE, BOARD_H * SCALE, fill="#fff", stroke="#cbd5e1", rx=4)

# barrier + channel region, for context
_, y38 = px(0, 38.0)
_, y43 = px(0, 43.0)
out.append(f'<rect x="{bx}" y="{by}" width="{BOARD_W*SCALE:.1f}" height="{y38-by:.1f}" fill="#eff6ff"/>')
out.append(f'<rect x="{bx}" y="{y38}" width="{BOARD_W*SCALE:.1f}" height="{y43-y38:.1f}" fill="#fee2e2"/>')
_, y80 = px(0, 80.0)
out.append(f'<rect x="{bx}" y="{y43}" width="{BOARD_W*SCALE:.1f}" height="{y80-y43:.1f}" fill="#fffbeb"/>')
text(bx + 8, (by + y38) / 2, "Pico + logic", size=9, fill="#64748b")
text(bx + 8, (y43 + y80) / 2, "8 channel columns", size=9, fill="#64748b")

# V+ rail across the board
_, vy = px(0, VPLUS_RAIL_Y)
line(bx + 6, vy, bx + BOARD_W * SCALE - 6, vy, C_VPLUS, 3)
text(bx + BOARD_W * SCALE - 8, vy - 6, "V+ rail", size=9, fill=C_VPLUS, anchor="end", weight="600")

# power section band
_, pby = px(0, 78.0)
_, pby2 = px(0, 98.0)
out.append(f'<rect x="{bx}" y="{pby}" width="{BOARD_W*SCALE:.1f}" height="{pby2-pby:.1f}" '
           f'fill="#fef3c7" stroke="#fcd34d" stroke-dasharray="4 3"/>')


def chip(ref, w_mm, h_mm, n, colour=C_ISO, label_below=False):
    x, y = P[ref]
    cx_, cy_ = px(x, y)
    w, h = w_mm * SCALE, h_mm * SCALE
    rect(cx_ - w / 2, cy_ - h / 2, w, h, fill="#fff", stroke=colour, sw=1.5, rx=2)
    badge(cx_, cy_ - h / 2 - 13, n, colour)
    text(cx_, cy_ + (h / 2 + 13 if label_below else -h / 2 - 26), ref, size=9,
         anchor="middle", weight="600")
    return cx_, cy_


j1x, j1y = px(*P["J1"])
rect(j1x - 5 * SCALE, j1y - 5 * SCALE, 10 * SCALE, 10 * SCALE, fill="#fff", stroke=C_ISO, sw=1.5, rx=2)
badge(j1x - 5 * SCALE - 12, j1y, 1)
text(j1x, j1y - 5 * SCALE - 6, "J1", size=9, anchor="middle", weight="600")

f1 = chip("F1", 3.2, 1.6, 2, label_below=True)
q9 = chip("Q9", 2.9, 1.3, 3, label_below=True)
r6 = chip("R6", 2.0, 1.25, 4)
d9 = chip("D9", 3.7, 1.6, 5)
tv = chip("TVS1", 5.0, 2.6, 6)
c1 = chip("C1", 8.0, 8.0, 7)
c2 = chip("C2", 2.0, 1.25, 8)

# the current path drawn on the board
poly([(j1x + 5 * SCALE, j1y - 2 * SCALE), (f1[0] - 2 * SCALE, j1y - 2 * SCALE),
      (f1[0] - 2 * SCALE, f1[1]), (f1[0] - 1.6 * SCALE, f1[1])], C_RAW, 2.4)
line(f1[0] + 1.6 * SCALE, f1[1], q9[0] - 1.45 * SCALE, q9[1], C_RAW, 2.4)
poly([(q9[0] + 1.45 * SCALE, q9[1]), (tv[0], tv[1]), (c1[0], c1[1]), (c2[0], c2[1])], C_VPLUS, 2.6)

# riser up to the rail
rx_, _ = px(RISER_X, 0)
poly([(c2[0], c2[1]), (rx_, c2[1]), (rx_, vy)], C_VPLUS, 2.6)
text(rx_ + 6, (c2[1] + vy) / 2, "V+ riser", size=9, fill=C_VPLUS, weight="600")
text(rx_ + 6, (c2[1] + vy) / 2 + 12, "board centre —", size=8, fill="#64748b")
text(rx_ + 6, (c2[1] + vy) / 2 + 22, "feeds both ways", size=8, fill="#64748b")

text(bx + BOARD_W * SCALE - 6, pby - 6, "input protection band, y = 78–98", size=9, fill="#92400e", anchor="end")

# ========================================================================================
# Footer
# ========================================================================================
fy = 590
rect(30, fy, 1180, 92, fill="#f1f5f9", stroke="#cbd5e1", rx=6)
text(46, fy + 24, "Three things that make this section click", size=13, weight="700")
text(46, fy + 46, "1.  Place in current order — J1, F1, Q9 — so the chain is a straight line you can follow with a finger.", size=12, fill="#334155")
text(46, fy + 64, "2.  Q9 is drain-to-input, source-to-output: the opposite way round to the eight channel MOSFETs. R6 and D9 are its satellites, keep them beside it.", size=12, fill="#334155")
text(46, fy + 82, "3.  TVS1, C1, C2 are not in the chain. They hang off V+ down to the ground pour — three vias, no traces.", size=12, fill="#334155")

out.append("</svg>")
path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "power-layout.svg")
open(path, "w", encoding="utf-8").write("\n".join(out))
print(f"wrote {path}")
