#!/usr/bin/env python3
"""Generate the routed .kicad_pcb from the netlist and the placement in layout.md.

Why a board file rather than another drawing: KiCad can check this one. A picture of
suggested traces is only as good as my eyesight; a .kicad_pcb runs through DRC, which knows
about clearances, unconnected pads and the custom isolation rule.

    python generate_pcb.py
    kicad-cli pcb drc --output drc.rpt shop-output-board-v2.kicad_pcb

Open the result in Pcbnew and move things — nothing here is precious. It exists so you start
from a board that is placed and connected rather than from a heap of footprints.
"""

import argparse
import math
import os
import sys

from generate_schematic import (tokenize, parse, dump, uid,
                                CHANNEL_GP, GP_TO_PICO_PIN, N_CHANNELS)

FOOTPRINT_DIR = r"C:\Program Files\KiCad\10.0\share\kicad\footprints"
BOARD_W, BOARD_H = 70.0, 92.0
BARRIER_TOP, BARRIER_BOT = 34.0, 39.0
VPLUS_Y = 44.0
RISER_X = 50.5
CH_X0, CH_PITCH = 13.0, 15.0

TRACE_SIG, TRACE_PWR = 0.25, 0.5
VIA_SIZE, VIA_DRILL = 0.6, 0.3

# Channel part placement, (dx, y) relative to the channel centre.
#
# v2 splits each channel into two sub-columns grouped by NET rather than by schematic order:
# everything touching OUT on the left, everything touching GATE on the right. That halves the
# channel height (19 mm against v1's 35 mm) and is the single biggest reason this board is
# 92 mm tall instead of 115.
#
# R2 sits at the top of the gate column so it lands directly under the optocoupler it feeds,
# and Q sits at the top of the output column so its source reaches the V+ rail in 2 mm and
# its drain has a clear run down the left side to the terminal.
CH_DX = 4.0
CY = {
    "R5":  (0.0, 27.0),          # logic side, on the centreline
    "U":   (0.0, 36.5),          # straddles the barrier, on the centreline

    "Q":   (-CH_DX, 47.0),       # output column
    "R3":  (-CH_DX, 51.0),
    "R4":  (-CH_DX, 55.0),
    "LED": (-CH_DX, 59.0),

    "R2":  (CH_DX, 47.0),        # gate column
    "D":   (CH_DX, 51.0),
    "R1":  (CH_DX, 55.0),
}

# Rotations, chosen so every pin faces the net it has to reach. Getting one wrong produces a
# board that looks plausible and is wired to the wrong places, so each is justified.
#
# KiCad rotates counter-clockwise with Y growing downward, so 90 puts pad 1 at the BOTTOM.
CH_ROT = {
    "R5":  0.0,
    "U":   270.0,   # SOP-4's 9.38 mm isolation span runs along X unrotated; 270 stands it up
                    # across the horizontal barrier, pins 1/2 logic side and 3/4 isolated
    "R2":  90.0,    # pin 2 (OPTOC) up to the opto, pin 1 (GATE) down to D and R1
    "D":   180.0,   # pin 1 (K, V+) right to the rail stub, pin 2 (A, GATE) left
    "R1":  180.0,   # same reasoning as D
    "Q":   180.0,   # source top-left to the V+ rail, gate top-right to the gate column,
                    # drain bottom-centre so OUT leaves straight down its own column
    "R3":  270.0,   # pin 1 (OUT) up to Q's drain
    "R4":  270.0,   # pin 1 (OUT) up, pin 2 (IND) down to the LED
    "LED": 90.0,    # pin 2 (A) up to R4 — anode faces OUT, or the indicator never lights
}

# Input section. Same series chain as v1, tightened, with a smaller bulk cap.
POWER = {"J1": (10.0, 84.0), "F1": (14.0, 72.0), "Q9": (24.0, 72.0),
         "R6": (20.0, 66.0), "D9": (30.0, 66.0), "TVS1": (34.0, 72.0),
         "C1": (46.0, 72.0), "C2": (56.0, 72.0)}

# Pico sockets run along X, so they are rotated. J4 carries GP2..GP9.
# 1x20 spans 48.26 mm, centred on a 70 mm board. J4 carries GP2..GP5 and sits on the
# lower row so those four traces run straight down to the channels.
PICO_J4 = (10.87, 22.78, 90.0)
PICO_J5 = (10.87, 5.00, 90.0)

TERM_J2 = (45.0, 84.0, 0.0)   # single 6-way: 4 outputs + 2 grounds


def rotate(px, py, deg):
    """KiCad footprint pad transform for a footprint placed at angle `deg`.

    Note the signs: KiCad rotates counter-clockwise on screen, and screen Y grows
    downward, so this is *not* the textbook rotation matrix. Getting it backwards puts
    every rotated part's pads on the wrong side, which on this board means the optos'
    isolated pins land in the logic zone. Verified against KiCad's own IPC-D-356 export
    by verify_pcb.py — do not "fix" these signs without re-running it.
    """
    a = math.radians(deg)
    return (px * math.cos(a) + py * math.sin(a),
            -px * math.sin(a) + py * math.cos(a))


# --- Load the netlist ------------------------------------------------------------------

def find(node, key):
    return [c for c in node if isinstance(c, list) and c and c[0] == key]


def load_netlist(path="netlist.net"):
    root, _ = parse(tokenize(open(path, encoding="utf-8").read()), 0)
    comps = {}
    for c in find(find(root, "components")[0], "comp"):
        ref = find(c, "ref")[0][1].strip('"')
        comps[ref] = find(c, "footprint")[0][1].strip('"')
    nets, pad_net = {}, {}
    for i, net in enumerate(find(find(root, "nets")[0], "net")):
        name = find(net, "name")[0][1].strip('"').lstrip("/")
        nets[name] = i + 1
        for nd in find(net, "node"):
            ref = find(nd, "ref")[0][1].strip('"')
            pin = find(nd, "pin")[0][1].strip('"')
            pad_net[(ref, pin)] = (i + 1, name)
    return comps, nets, pad_net


ap = argparse.ArgumentParser()
ap.add_argument("--force", action="store_true",
                help="Overwrite an existing board. Refused by default: once you have routed "
                     "in Pcbnew the board file is yours, and regenerating discards that work.")
ARGS = ap.parse_args()

COMPS, NETS, PAD_NET = load_netlist()

# --- Placement -------------------------------------------------------------------------

PLACE = {}
for n in range(1, N_CHANNELS + 1):
    cx = CH_X0 + (n - 1) * CH_PITCH
    for key, ref in (("R5", f"R{n}05"), ("U", f"U{n}"), ("R2", f"R{n}02"),
                     ("D", f"D{n}"), ("Q", f"Q{n}"), ("R1", f"R{n}01"),
                     ("R3", f"R{n}03"), ("R4", f"R{n}04"), ("LED", f"LED{n}")):
        dx, y = CY[key]
        PLACE[ref] = (cx + dx, y, CH_ROT[key])
for ref, (x, y) in POWER.items():
    PLACE[ref] = (x, y, 0.0)
PLACE["J4"], PLACE["J5"] = PICO_J4, PICO_J5
PLACE["J2"] = TERM_J2

missing = set(COMPS) - set(PLACE)
if missing:
    sys.exit(f"no placement for: {sorted(missing)}")

# --- Footprints ------------------------------------------------------------------------

body = []
PADS = {}          # (ref, pad) -> (x, y)


def load_footprint(lib_id):
    lib, name = lib_id.split(":", 1)
    path = os.path.join(FOOTPRINT_DIR, lib + ".pretty", name + ".kicad_mod")
    root, _ = parse(tokenize(open(path, encoding="utf-8").read()), 0)
    return root


def emit_footprint(ref, lib_id, x, y, rot):
    node = load_footprint(lib_id)
    keep = []
    for c in node[1:]:
        if isinstance(c, list) and c[0] in ("version", "generator", "generator_version"):
            continue
        keep.append(c)

    for c in keep:
        if not (isinstance(c, list) and c[0] == "pad"):
            continue
        num = c[1].strip('"')
        at = next(g for g in c if isinstance(g, list) and g[0] == "at")
        lx, ly = float(at[1]), float(at[2])
        gx, gy = rotate(lx, ly, rot)
        PADS[(ref, num)] = (round(x + gx, 4), round(y + gy, 4))
        # Pads inherit the footprint rotation.
        if rot and len(at) > 3:
            at[3] = f"{(float(at[3]) + rot) % 360:g}"
        elif rot:
            at.append(f"{rot % 360:g}")
        if (ref, num) in PAD_NET:
            nid, nname = PAD_NET[(ref, num)]
            c.append(["net", str(nid), f'"{nname}"'])

    props = "\n".join([
        f'\t\t(property "Reference" "{ref}"\n\t\t\t(at 0 -2 0)\n\t\t\t(layer "F.SilkS")\n'
        f'\t\t\t(uuid "{uid(f"pcb-{ref}-ref")}")\n\t\t\t(effects\n\t\t\t\t(font\n'
        f'\t\t\t\t\t(size 0.8 0.8)\n\t\t\t\t\t(thickness 0.12)\n\t\t\t\t)\n\t\t\t)\n\t\t)',
    ])
    inner = "\n".join(f"\t\t{dump(c, indent=2)}" for c in keep
                      if isinstance(c, list) and c[0] not in ("property", "fp_text"))

    body.append(f'''\t(footprint "{lib_id}"
\t\t(layer "F.Cu")
\t\t(uuid "{uid(f"pcb-{ref}")}")
\t\t(at {x} {y}{f" {rot % 360:g}" if rot else ""})
{props}
{inner}
\t)''')


for ref, lib_id in sorted(COMPS.items()):
    x, y, rot = PLACE[ref]
    emit_footprint(ref, lib_id, x, y, rot)

# --- Sanity-check the rotation before anything is routed off these coordinates ----------
j4 = [PADS[("J4", str(i))] for i in range(1, 21)]
span_x = max(p[0] for p in j4) - min(p[0] for p in j4)
span_y = max(p[1] for p in j4) - min(p[1] for p in j4)
if span_x < 40 or span_y > 1:
    sys.exit(f"J4 rotation wrong: pads span x={span_x:.1f} y={span_y:.1f}, "
             f"expected ~48 x 0. Flip the sign of the Pico rotation.")

# --- Traces ----------------------------------------------------------------------------

tracks = []


def seg(p1, p2, net_name, layer="F.Cu", width=TRACE_SIG):
    if p1 == p2:
        return
    nid = NETS[net_name]
    tracks.append(f'''\t(segment
\t\t(start {p1[0]:.4f} {p1[1]:.4f})
\t\t(end {p2[0]:.4f} {p2[1]:.4f})
\t\t(width {width})
\t\t(layer "{layer}")
\t\t(net {nid})
\t\t(uuid "{uid(f"seg-{p1}-{p2}-{net_name}")}")
\t)''')


def via(p, net_name):
    nid = NETS[net_name]
    tracks.append(f'''\t(via
\t\t(at {p[0]:.4f} {p[1]:.4f})
\t\t(size {VIA_SIZE})
\t\t(drill {VIA_DRILL})
\t\t(layers "F.Cu" "B.Cu")
\t\t(net {nid})
\t\t(uuid "{uid(f"via-{p}-{net_name}")}")
\t)''')


def route(points, net_name, layer="F.Cu", width=TRACE_SIG):
    for a, b in zip(points, points[1:]):
        seg(a, b, net_name, layer, width)


P = PADS

# v1 shipped a --route path that emitted traces. It never got past 113 DRC violations,
# and a half-routed board is worse to inherit than an unrouted one, so v2 does not carry
# it forward. Placement, outline, barrier and zones are generated; routing is by hand in
# Pcbnew, following a ratsnest that is already correct.

# --- Board outline, barrier, zones -------------------------------------------------------
edges = []
for a, b in [((0, 0), (BOARD_W, 0)), ((BOARD_W, 0), (BOARD_W, BOARD_H)),
             ((BOARD_W, BOARD_H), (0, BOARD_H)), ((0, BOARD_H), (0, 0))]:
    edges.append(f'''\t(gr_line
\t\t(start {a[0]} {a[1]})
\t\t(end {b[0]} {b[1]})
\t\t(stroke (width 0.1) (type default))
\t\t(layer "Edge.Cuts")
\t\t(uuid "{uid(f"edge-{a}-{b}")}")
\t)''')
for y in (BARRIER_TOP, BARRIER_BOT):
    edges.append(f'''\t(gr_line
\t\t(start 0 {y})
\t\t(end {BOARD_W} {y})
\t\t(stroke (width 0.2) (type dash))
\t\t(layer "Cmts.User")
\t\t(uuid "{uid(f"barrier-{y}")}")
\t)''')


def zone(net_name, y0, y1, uid_key):
    nid = NETS[net_name]
    pts = f"(xy 1 {y0}) (xy {BOARD_W-1} {y0}) (xy {BOARD_W-1} {y1}) (xy 1 {y1})"
    return f'''\t(zone
\t\t(net {nid})
\t\t(net_name "{net_name}")
\t\t(layers "F.Cu" "B.Cu")
\t\t(uuid "{uid(uid_key)}")
\t\t(hatch edge 0.5)
\t\t(connect_pads
\t\t\t(clearance 0.5)
\t\t)
\t\t(min_thickness 0.25)
\t\t(fill
\t\t\t(thermal_gap 0.5)
\t\t\t(thermal_bridge_width 0.75)
\t\t)
\t\t(polygon
\t\t\t(pts
\t\t\t\t{pts}
\t\t\t)
\t\t)
\t)'''


zones = [zone("PICO_GND", 1.0, BARRIER_TOP, "zone-logic"),
         zone("ISO_GND", BARRIER_BOT, BOARD_H - 1.0, "zone-iso")]

net_decls = "\n".join(f'\t(net {i} "{n}")' for n, i in
                      sorted(NETS.items(), key=lambda kv: kv[1]))

pcb = f'''(kicad_pcb
\t(version 20250513)
\t(generator "shop-output-board-v2-generator")
\t(generator_version "9.99")
\t(general
\t\t(thickness 1.6)
\t\t(legacy_teardrops no)
\t)
\t(paper "A3")
\t(layers
\t\t(0 "F.Cu" signal)
\t\t(2 "B.Cu" signal)
\t\t(9 "F.Adhes" user "F.Adhesive")
\t\t(11 "F.Paste" user)
\t\t(13 "F.SilkS" user "F.Silkscreen")
\t\t(15 "F.Mask" user)
\t\t(17 "B.Mask" user)
\t\t(31 "B.SilkS" user "B.Silkscreen")
\t\t(33 "B.Paste" user)
\t\t(35 "B.Adhes" user "B.Adhesive")
\t\t(37 "Edge.Cuts" user)
\t\t(39 "Margin" user)
\t\t(41 "F.CrtYd" user "F.Courtyard")
\t\t(43 "B.CrtYd" user "B.Courtyard")
\t\t(45 "F.Fab" user)
\t\t(47 "B.Fab" user)
\t\t(49 "Cmts.User" user "Comments")
\t\t(51 "Dwgs.User" user "Drawings")
\t)
\t(setup
\t\t(pad_to_mask_clearance 0)
\t\t(allow_soldermask_bridges_in_footprints no)
\t)
\t(net 0 "")
{net_decls}
{chr(10).join(body)}
{chr(10).join(edges)}
{chr(10).join(tracks)}
{chr(10).join(zones)}
)
'''

path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "shop-output-board-v2.kicad_pcb")

# Only write when run as a script. verify_pcb.py imports this module for PADS and the
# barrier constants, and an import must not have side effects — before this guard, importing
# a routed board's generator hit the sys.exit below and took the verifier down with it.
if __name__ == "__main__":
    # Once the board has been opened and routed in Pcbnew it is no longer a generated
    # artefact — it holds work this script cannot reproduce. Regenerating over it would
    # discard that silently, so it has to be asked for.
    if os.path.exists(path) and not ARGS.force:
        existing = open(path, encoding="utf-8").read()
        routed = existing.count("(segment")
        if routed:
            sys.exit(
                f"{os.path.basename(path)} already has {routed} routed segments. "
                "Regenerating would discard them. Back it up, then pass --force if you "
                "really mean to start the layout again.")

    open(path, "w", encoding="utf-8").write(pcb)
    print(f"wrote {path}")
    print(f"{len(COMPS)} footprints, {len(tracks)} track/via elements, {len(NETS)} nets")
    print("placement only - route interactively in Pcbnew, the ratsnest is already correct")
