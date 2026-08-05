#!/usr/bin/env python3
"""Generate the routed .kicad_pcb from the netlist and the placement in layout.md.

Why a board file rather than another drawing: KiCad can check this one. A picture of
suggested traces is only as good as my eyesight; a .kicad_pcb runs through DRC, which knows
about clearances, unconnected pads and the custom isolation rule.

    python generate_pcb.py
    kicad-cli pcb drc --output drc.rpt shop-output-board.kicad_pcb

Open the result in Pcbnew and move things — nothing here is precious. It exists so you start
from a board that is placed and connected rather than from a heap of footprints.
"""

import argparse
import math
import os
import sys

from generate_schematic import (tokenize, parse, dump, uid,
                                CHANNEL_GP, GP_TO_PICO_PIN)

FOOTPRINT_DIR = r"C:\Program Files\KiCad\10.0\share\kicad\footprints"
BOARD_W, BOARD_H = 150.0, 115.0
BARRIER_TOP, BARRIER_BOT = 38.0, 43.0
VPLUS_Y = 47.0
RISER_X = 71.0
CH_X0, CH_PITCH = 15.0, 16.0

TRACE_SIG, TRACE_PWR = 0.25, 0.5
VIA_SIZE, VIA_DRILL = 0.6, 0.3

# Channel part Y positions (layout.md)
CY = {"R5": 32.0, "U": 40.5, "R2": 50.0, "D": 56.0, "Q": 62.0,
      "R1": 68.0, "R3": 72.0, "R4": 76.0, "LED": 80.0}

# Input section (layout.md)
POWER = {"J1": (16.0, 103.0), "F1": (28.0, 92.0), "Q9": (38.0, 92.0),
         "R6": (34.0, 86.0), "D9": (46.0, 86.0), "TVS1": (50.0, 92.0),
         "C1": (62.0, 92.0), "C2": (72.0, 92.0)}

# Pico sockets run along X, so they are rotated. J4 carries GP2..GP9.
PICO_J4 = (50.87, 24.78, 90.0)
PICO_J5 = (50.87, 7.00, 90.0)

TERM_J2 = (95.0, 103.0, 0.0)
TERM_J3 = (125.0, 103.0, 0.0)


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
ap.add_argument("--route", action="store_true",
                help="Also emit traces. INCOMPLETE - DRC still reports shorts and crossings; "
                     "the routing here is a work in progress, not a finished board.")
ARGS = ap.parse_args()

COMPS, NETS, PAD_NET = load_netlist()

# --- Placement -------------------------------------------------------------------------

PLACE = {}
for n in range(1, 9):
    cx = CH_X0 + (n - 1) * CH_PITCH
    PLACE[f"R{n}05"] = (cx, CY["R5"], 0.0)
    # 90 degrees: the SOP-4's 9.38mm isolation span is along X in the footprint, so
    # unrotated it would straddle a *vertical* barrier. At 270 the pins 1/2 land at
    # y=35.81 (logic) and 3/4 at y=45.19 (isolated), matching layout.md.
    PLACE[f"U{n}"] = (cx, CY["U"], 270.0)
    # 180: puts pad 1 (GATE) on the right, where the gate rail runs.
    PLACE[f"R{n}02"] = (cx, CY["R2"], 180.0)
    PLACE[f"D{n}"] = (cx, CY["D"], 0.0)
    # 270: source to top-left (V+ rail), gate to top-right (gate rail), drain to
    # bottom-centre so OUT leaves straight down towards its terminal.
    PLACE[f"Q{n}"] = (cx, CY["Q"], 270.0)
    PLACE[f"R{n}01"] = (cx, CY["R1"], 0.0)
    PLACE[f"R{n}03"] = (cx, CY["R3"], 0.0)
    PLACE[f"R{n}04"] = (cx, CY["R4"], 0.0)
    PLACE[f"LED{n}"] = (cx, CY["LED"], 0.0)
for ref, (x, y) in POWER.items():
    PLACE[ref] = (x, y, 0.0)
PLACE["J4"], PLACE["J5"] = PICO_J4, PICO_J5
PLACE["J2"], PLACE["J3"] = TERM_J2, TERM_J3

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

if ARGS.route:
    # V+ rail across the board, on the back layer so it never fights the channel columns.
    rail_l, rail_r = (8.0, VPLUS_Y), (142.0, VPLUS_Y)
    route([rail_l, rail_r], "V+", "B.Cu", TRACE_PWR)

    for n in range(1, 9):
        cx = CH_X0 + (n - 1) * CH_PITCH
        gp = f"GP{CHANNEL_GP[n - 1]}"

        # Pico pin -> R5. Straight to the column X, then down. Monotonic pin-to-channel
        # assignment means these eight fan out without crossing each other.
        pico_pin = P[("J4", str(GP_TO_PICO_PIN[CHANNEL_GP[n - 1]]))]
        r5a, r5b = P[(f"R{n}05", "1")], P[(f"R{n}05", "2")]
        route([pico_pin, (pico_pin[0], 28.5), (cx, 28.5), r5a], gp)

        # R5 -> opto LED anode
        route([r5b, (cx, 34.0), P[(f"U{n}", "1")]], f"LEDA{n}")
        # opto LED cathode -> logic ground pour
        via(P[(f"U{n}", "2")], "PICO_GND")

        # opto collector -> R2
        route([P[(f"U{n}", "4")], P[(f"R{n}02", "2")]], f"OPTOC{n}")
        # opto emitter -> isolated ground pour
        via(P[(f"U{n}", "3")], "ISO_GND")

        # GATE rail on the right, V+ on the left. That is set by the parts, not by taste:
        # the Zener's cathode and R1's V+ pad are both on the left, and the rotated MOSFET
        # presents its source top-left and gate top-right.
        gate = f"GATE{n}"
        gx = cx + 3.0
        route([P[(f"R{n}02", "1")], (gx, CY["R2"]), (gx, CY["R1"]), P[(f"R{n}01", "2")]], gate)
        route([(gx, CY["D"]), P[(f"D{n}", "2")]], gate)
        route([(gx, CY["Q"] - 1.0), P[(f"Q{n}", "1")]], gate)

        vx = cx - 3.0
        route([P[(f"D{n}", "1")], (vx, CY["D"]), (vx, CY["R1"]), P[(f"R{n}01", "1")]], "V+", "F.Cu", TRACE_PWR)
        route([(vx, CY["Q"] - 1.0), P[(f"Q{n}", "2")]], "V+", "F.Cu", TRACE_PWR)
        route([(vx, CY["D"]), (vx, VPLUS_Y + 2.0)], "V+", "F.Cu", TRACE_PWR)
        via((vx, VPLUS_Y + 2.0), "V+")
        route([(vx, VPLUS_Y + 2.0), (vx, VPLUS_Y)], "V+", "B.Cu", TRACE_PWR)

        # OUT leaves the drain straight down the column centre.
        out = f"OUT{n}"
        term = "J2" if n <= 4 else "J3"
        tpin = P[(term, str(n if n <= 4 else n - 4))]
        route([P[(f"Q{n}", "3")], (cx, CY["R3"]), P[(f"R{n}03", "1")]], out, "F.Cu", TRACE_PWR)
        route([(cx, CY["R3"]), (cx, CY["R4"]), P[(f"R{n}04", "1")]], out, "F.Cu", TRACE_PWR)
        route([(cx, CY["R4"]), (cx, 84.0)], out, "F.Cu", TRACE_PWR)
        via((cx, 84.0), out)
        route([(cx, 84.0), (cx, 99.0), (tpin[0], 99.0), tpin], out, "B.Cu", TRACE_PWR)

        # Ground drops
        via(P[(f"R{n}03", "2")], "ISO_GND")
        via(P[(f"LED{n}", "2")], "ISO_GND")
        route([P[(f"R{n}04", "2")], P[(f"LED{n}", "1")]], f"LEDK{n}")

    # Pico grounds
    for pin in ("3", "8", "13", "18"):
        via(P[("J4", pin)], "PICO_GND")
        via(P[("J5", pin)], "PICO_GND")

    # --- Input section ---------------------------------------------------------------------
    route([P[("J1", "1")], (P[("J1", "1")][0], 96.0), (P[("F1", "1")][0], 96.0), P[("F1", "1")]],
          "VIN_RAW", "F.Cu", TRACE_PWR)
    route([P[("F1", "2")], P[("Q9", "3")]], "VIN_F", "F.Cu", TRACE_PWR)
    route([P[("Q9", "1")], (38.0, 87.0), P[("R6", "1")]], "Q9GATE")
    route([(38.0, 87.0), P[("D9", "2")]], "Q9GATE")
    route([P[("Q9", "2")], P[("TVS1", "1")], P[("C1", "1")], P[("C2", "1")]], "V+", "F.Cu", TRACE_PWR)
    route([P[("C2", "1")], (RISER_X, 92.0)], "V+", "F.Cu", TRACE_PWR)
    via((RISER_X, 92.0), "V+")
    route([(RISER_X, 92.0), (RISER_X, VPLUS_Y)], "V+", "B.Cu", TRACE_PWR)
    route([P[("D9", "1")], (46.0, 88.0), (50.0, 88.0), P[("TVS1", "1")]], "V+", "F.Cu", TRACE_PWR)
    for ref, pad in [("J1", "2"), ("R6", "2"), ("TVS1", "2"), ("C1", "2"), ("C2", "2")]:
        via(P[(ref, pad)], "ISO_GND")
    for pin in ("5", "6"):
        via(P[("J2", pin)], "ISO_GND")
        via(P[("J3", pin)], "ISO_GND")


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
\t(generator "shop-output-board-generator")
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

path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "shop-output-board.kicad_pcb")

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
    if not ARGS.route:
        print("placement only - route interactively in Pcbnew, the ratsnest is already correct")
