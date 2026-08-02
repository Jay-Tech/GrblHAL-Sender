#!/usr/bin/env python3
"""Generate the shop output board schematic as a KiCad .kicad_sch.

The board is eight identical channels, so the placement is computed rather than drawn.
Regenerate after changing any component value and the whole sheet stays consistent.

Connectivity is carried by net labels on short pin stubs rather than by long wires between
components. That is deliberate: it keeps the geometry trivial, makes the netlist exactly
what hardware/shop-output-board/netlist.md specifies, and leaves nothing to go subtly wrong
in wire routing. It reads as a "label soup" schematic rather than a drawn one — rearrange in
Eeschema if you want it prettier, the connectivity will not change.

Usage:  python generate_schematic.py [--out DIR]
"""

import argparse
import hashlib
import os
import sys

SYMBOL_DIR = r"C:\Program Files\KiCad\10.0\share\kicad\symbols"

# --- Symbols pulled from the stock libraries -------------------------------------------

NEEDED = {
    "Device:R": ("Device", "R"),
    "Device:C": ("Device", "C"),
    "Device:C_Polarized": ("Device", "C_Polarized"),
    "Device:LED": ("Device", "LED"),
    "Device:D_Zener": ("Device", "D_Zener"),
    "Device:D_TVS": ("Device", "D_TVS"),
    "Device:Polyfuse": ("Device", "Polyfuse"),
    # The real part rather than Device:Q_PMOS. The generic symbol numbers its pins G/D/S,
    # which cannot map to a SOT-23 footprint's numeric pads — the board would not import.
    # AO3401A inherits 1=G, 2=S, 3=D from its parent, matching the physical package.
    "Transistor_FET:AO3401A": ("Transistor_FET", "AO3401A"),
    "Isolator:PC817": ("Isolator", "PC817"),
    "Connector_Generic:Conn_01x02": ("Connector_Generic", "Conn_01x02"),
    "Connector_Generic:Conn_01x06": ("Connector_Generic", "Conn_01x06"),
    "Connector_Generic:Conn_01x20": ("Connector_Generic", "Conn_01x20"),
}

# Every symbol needs one, or the netlist cannot become a board. Pin numbers and pad names
# must agree, which is why the Zener is a 2-pin SOD-123 rather than a 3-pad SOT-23 part.
FOOTPRINTS = {
    "Device:R": "Resistor_SMD:R_0805_2012Metric",
    "Device:C": "Capacitor_SMD:C_0805_2012Metric",
    "Device:C_Polarized": "Capacitor_SMD:CP_Elec_8x10.5",
    "Device:LED": "LED_SMD:LED_0805_2012Metric",
    "Device:D_Zener": "Diode_SMD:D_SOD-123",
    "Device:D_TVS": "Diode_SMD:D_SMA",
    "Device:Polyfuse": "Fuse:Fuse_1206_3216Metric",
    "Transistor_FET:AO3401A": "Package_TO_SOT_SMD:SOT-23",
    # DIP-4 rather than the SMD option: the wider body buys creepage across the isolation
    # barrier, which is the one place on this board where spacing is load-bearing.
    "Isolator:PC817": "Package_DIP:DIP-4_W7.62mm",
    "Connector_Generic:Conn_01x02":
        "TerminalBlock_Phoenix:TerminalBlock_Phoenix_MKDS-1,5-2-5.08_1x02_P5.08mm_Horizontal",
    "Connector_Generic:Conn_01x06":
        "TerminalBlock_Phoenix:TerminalBlock_Phoenix_MKDS-1-6-3.81_1x06_P3.81mm_Horizontal",
    "Connector_Generic:Conn_01x20":
        "Connector_PinSocket_2.54mm:PinSocket_1x20_P2.54mm_Vertical",
}


# --- Minimal S-expression reader --------------------------------------------------------

def tokenize(text):
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c in "()":
            out.append(c)
            i += 1
        elif c == '"':
            j, buf = i + 1, []
            while j < n:
                if text[j] == "\\":
                    buf.append(text[j:j + 2])
                    j += 2
                    continue
                if text[j] == '"':
                    break
                buf.append(text[j])
                j += 1
            out.append('"' + "".join(buf) + '"')
            i = j + 1
        elif c.isspace():
            i += 1
        else:
            j = i
            while j < n and not text[j].isspace() and text[j] not in '()"':
                j += 1
            out.append(text[i:j])
            i = j
    return out


def parse(tokens, pos=0):
    """Returns (node, next_pos). A node is a list; atoms are strings."""
    assert tokens[pos] == "("
    node, pos = [], pos + 1
    while tokens[pos] != ")":
        if tokens[pos] == "(":
            child, pos = parse(tokens, pos)
            node.append(child)
        else:
            node.append(tokens[pos])
            pos += 1
    return node, pos + 1


def dump(node, indent=1):
    pad = "\t" * indent
    if isinstance(node, str):
        return node
    head = node[0] if node and isinstance(node[0], str) else ""
    simple = all(isinstance(c, str) for c in node)
    if simple:
        return "(" + " ".join(node) + ")"
    parts = [pad + "\t" + dump(c, indent + 1) if not isinstance(c, str) else c
             for c in node[1:]]
    return "(" + head + "\n" + "\n".join(
        p if p.startswith("\t") else pad + "\t" + p for p in parts) + "\n" + pad + ")"


def find_symbol(lib_text, name):
    tokens = tokenize(lib_text)
    root, _ = parse(tokens, 0)
    for node in root:
        if isinstance(node, list) and node and node[0] == "symbol" and node[1] == f'"{name}"':
            return node
    raise KeyError(name)


def collect_pins(node, acc=None, names=None):
    """Pin number -> (x, y, angle), in symbol-local coordinates.

    Also builds a pin-name -> pin-number map, so placement code can say "G" and "D" and
    still emit the numeric pins a footprint's pads are named after.
    """
    if acc is None:
        acc = {}
    if names is None:
        names = {}
    for child in node:
        if not isinstance(child, list):
            continue
        if child[0] == "pin":
            at = num = nm = None
            for g in child:
                if isinstance(g, list) and g[0] == "at":
                    at = (float(g[1]), float(g[2]), float(g[3]) if len(g) > 3 else 0.0)
                if isinstance(g, list) and g[0] == "number":
                    num = g[1].strip('"')
                if isinstance(g, list) and g[0] == "name":
                    nm = g[1].strip('"')
            if at and num is not None:
                acc[num] = at
                if nm:
                    names.setdefault(nm, num)
        else:
            collect_pins(child, acc, names)
    return acc, names


def flatten_extends(lib_text, node, lib_id):
    """Resolves an (extends "PARENT") symbol into a self-contained definition.

    Real part symbols usually inherit their graphics and pins from a generic parent and
    override only the properties. A schematic's lib_symbols has to stand alone, so the
    parent's body is merged in and its unit sub-symbols renamed to match the child.
    """
    parent_name = None
    for c in node:
        if isinstance(c, list) and c[0] == "extends":
            parent_name = c[1].strip('"')
    if parent_name is None:
        return node

    parent = find_symbol(lib_text, parent_name)
    child_props = {c[1] for c in node if isinstance(c, list) and c[0] == "property"}

    merged = [c for c in node if not (isinstance(c, list) and c[0] == "extends")]
    short = lib_id.split(":", 1)[1]
    for c in parent:
        if not isinstance(c, list):
            continue
        if c[0] == "property" and c[1] in child_props:
            continue                       # the child's own value wins
        if c[0] == "symbol":
            c = list(c)
            # Unit sub-symbols are named "<PARENT>_0_1"; rename so they belong to the child.
            c[1] = '"' + short + c[1].strip('"')[len(parent_name):] + '"'
        if c[0] in ("property", "symbol", "pin_numbers", "pin_names"):
            merged.append(c)
    return merged


# --- Deterministic UUIDs ---------------------------------------------------------------

_counter = [0]


def uid(seed=None):
    if seed is None:
        _counter[0] += 1
        seed = f"auto-{_counter[0]}"
    h = hashlib.sha1(f"shop-output-board::{seed}".encode()).hexdigest()
    return f"{h[0:8]}-{h[8:12]}-4{h[13:16]}-a{h[17:20]}-{h[20:32]}"


SHEET_UUID = uid("sheet")

# --- Emission --------------------------------------------------------------------------

body = []
placed = []          # (ref, lib_id, x, y, pins_abs)


GRID = 1.27


def snap(v):
    """KiCad's default grid is 1.27mm (50 mil).

    Stock symbols place their pins on that grid, so an origin on-grid keeps every pin and
    every wire endpoint on-grid too. Off-grid endpoints still connect, but Eeschema flags
    all of them and the real ones get lost in the noise.
    """
    return round(round(v / GRID) * GRID, 2)


def place(ref, lib_id, value, x, y, footprint=None):
    """Puts a symbol at (x, y) and records absolute pin positions."""
    x, y = snap(x), snap(y)
    if footprint is None:
        footprint = FOOTPRINTS[lib_id]
    pins = SYMBOL_PINS[lib_id]
    abs_pins = {}
    for numb, (px, py, ang) in pins.items():
        # Symbol space has +Y upward; the sheet has +Y downward.
        abs_pins[numb] = (round(x + px, 2), round(y - py, 2), ang)

    pin_entries = "\n".join(
        f'\t\t(pin "{numb}"\n\t\t\t(uuid "{uid(f"{ref}-pin-{numb}")}")\n\t\t)'
        for numb in pins)

    body.append(f'''\t(symbol
\t\t(lib_id "{lib_id}")
\t\t(at {x} {y} 0)
\t\t(unit 1)
\t\t(exclude_from_sim no)
\t\t(in_bom yes)
\t\t(on_board yes)
\t\t(dnp no)
\t\t(uuid "{uid(ref)}")
\t\t(property "Reference" "{ref}"
\t\t\t(at {x + 6.35} {y - 2.54} 0)
\t\t\t(effects
\t\t\t\t(font
\t\t\t\t\t(size 1.27 1.27)
\t\t\t\t)
\t\t\t\t(justify left)
\t\t\t)
\t\t)
\t\t(property "Value" "{value}"
\t\t\t(at {x + 6.35} {y} 0)
\t\t\t(effects
\t\t\t\t(font
\t\t\t\t\t(size 1.27 1.27)
\t\t\t\t)
\t\t\t\t(justify left)
\t\t\t)
\t\t)
\t\t(property "Footprint" "{footprint}"
\t\t\t(at {x} {y} 0)
\t\t\t(hide yes)
\t\t\t(effects
\t\t\t\t(font
\t\t\t\t\t(size 1.27 1.27)
\t\t\t\t)
\t\t\t)
\t\t)
\t\t(property "Datasheet" "~"
\t\t\t(at {x} {y} 0)
\t\t\t(hide yes)
\t\t\t(effects
\t\t\t\t(font
\t\t\t\t\t(size 1.27 1.27)
\t\t\t\t)
\t\t\t)
\t\t)
{pin_entries}
\t\t(instances
\t\t\t(project "shop-output-board"
\t\t\t\t(path "/{SHEET_UUID}"
\t\t\t\t\t(reference "{ref}")
\t\t\t\t\t(unit 1)
\t\t\t\t)
\t\t\t)
\t\t)
\t)''')
    placed.append((ref, lib_id, x, y, abs_pins))
    return abs_pins


def wire(x1, y1, x2, y2, seed):
    body.append(f'''\t(wire
\t\t(pts
\t\t\t(xy {x1} {y1}) (xy {x2} {y2})
\t\t)
\t\t(stroke
\t\t\t(width 0)
\t\t\t(type default)
\t\t)
\t\t(uuid "{uid(seed)}")
\t)''')


def label(text, x, y, angle, seed):
    body.append(f'''\t(label "{text}"
\t\t(at {x} {y} {angle})
\t\t(effects
\t\t\t(font
\t\t\t\t(size 1.27 1.27)
\t\t\t)
\t\t\t(justify left bottom)
\t\t)
\t\t(uuid "{uid(seed)}")
\t)''')


def no_connect(x, y, seed):
    body.append(f'\t(no_connect\n\t\t(at {x} {y})\n\t\t(uuid "{uid(seed)}")\n\t)')


STUB = 2.54


def net(pins, numb, name, ref, direction="up", lib_id=None):
    """Stubs a pin out and labels it. Direction picks which way the stub runs.

    `numb` may be a pin name ("G", "D") as well as a number; names are resolved so the
    placement code stays readable while the emitted schematic uses the numeric pins that
    footprint pads are named after.
    """
    if numb not in pins and lib_id:
        numb = SYMBOL_PIN_NAMES[lib_id].get(numb, numb)
    x, y, _ = pins[numb]
    dx, dy, ang = {
        "up": (0, -STUB, 90),
        "down": (0, STUB, 90),
        "left": (-STUB, 0, 0),
        "right": (STUB, 0, 0),
    }[direction]
    wire(x, y, x + dx, y + dy, f"{ref}-w-{numb}")
    label(name, x + dx, y + dy, ang, f"{ref}-l-{numb}")


# --- Load symbols ----------------------------------------------------------------------

SYMBOL_DEFS = {}
SYMBOL_PINS = {}
SYMBOL_PIN_NAMES = {}

for lib_id, (lib, name) in NEEDED.items():
    path = os.path.join(SYMBOL_DIR, f"{lib}.kicad_sym")
    with open(path, encoding="utf-8") as fh:
        text = fh.read()
    node = flatten_extends(text, find_symbol(text, name), lib_id)
    node = list(node)
    node[1] = f'"{lib_id}"'          # lib_symbols keys are fully qualified
    SYMBOL_DEFS[lib_id] = dump(node, indent=2)
    SYMBOL_PINS[lib_id], SYMBOL_PIN_NAMES[lib_id] = collect_pins(node)
    if not SYMBOL_PINS[lib_id]:
        raise SystemExit(f"{lib_id}: no pins resolved — check the extends chain")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.dirname(os.path.abspath(__file__)))
    args = ap.parse_args()

    # --- Eight channels, one per column -------------------------------------------------
    COL0, COLW, TOP = 30.0, 68.0, 35.0

    for n in range(1, 9):
        cx = COL0 + (n - 1) * COLW
        gp = f"GP{n + 1}"                     # channel 1 -> GP2 ... channel 8 -> GP9
        gate, optoc, out = f"GATE{n}", f"OPTOC{n}", f"OUT{n}"
        ledk = f"LEDK{n}"
        leda = f"LEDA{n}"

        p = place(f"R{n}01", "Device:R", "100k", cx, TOP)
        net(p, "1", "V+", f"R{n}01", "up")
        net(p, "2", gate, f"R{n}01", "down")

        p = place(f"D{n}", "Device:D_Zener", "MMSZ5237B 8V2", cx + 25, TOP)
        net(p, "1", "V+", f"D{n}", "up")       # pin 1 = K
        net(p, "2", gate, f"D{n}", "down")     # pin 2 = A

        p = place(f"R{n}02", "Device:R", "4k7", cx, TOP + 25)
        net(p, "1", gate, f"R{n}02", "up")
        net(p, "2", optoc, f"R{n}02", "down")

        p = place(f"U{n}", "Isolator:PC817", "PC817B", cx, TOP + 55)
        net(p, "1", leda, f"U{n}", "left")
        net(p, "2", "PICO_GND", f"U{n}", "left")
        net(p, "3", "ISO_GND", f"U{n}", "right")
        net(p, "4", optoc, f"U{n}", "right")

        p = place(f"R{n}05", "Device:R", "470R", cx + 30, TOP + 55)
        net(p, "1", gp, f"R{n}05", "up")
        net(p, "2", leda, f"R{n}05", "down")

        QFET = "Transistor_FET:AO3401A"
        p = place(f"Q{n}", QFET, "AO3401A", cx, TOP + 90)
        net(p, "S", "V+", f"Q{n}", "up", QFET)
        net(p, "G", gate, f"Q{n}", "left", QFET)
        net(p, "D", out, f"Q{n}", "down", QFET)

        p = place(f"R{n}03", "Device:R", "10k", cx, TOP + 120)
        net(p, "1", out, f"R{n}03", "up")
        net(p, "2", "ISO_GND", f"R{n}03", "down")

        p = place(f"R{n}04", "Device:R", "2k2", cx + 25, TOP + 120)
        net(p, "1", out, f"R{n}04", "up")
        net(p, "2", ledk, f"R{n}04", "down")

        p = place(f"LED{n}", "Device:LED", "GRN", cx + 25, TOP + 145)
        net(p, "1", ledk, f"LED{n}", "up")     # pin 1 = K
        net(p, "2", "ISO_GND", f"LED{n}", "down")

    # --- Input protection ---------------------------------------------------------------
    ix, iy = 30.0, 235.0

    p = place("J1", "Connector_Generic:Conn_01x02", "V+ IN 5-24V", ix, iy)
    net(p, "1", "VIN_RAW", "J1", "right")
    net(p, "2", "ISO_GND", "J1", "right")

    p = place("F1", "Device:Polyfuse", "1.1A", ix + 40, iy)
    net(p, "1", "VIN_RAW", "F1", "up")
    net(p, "2", "VIN_F", "F1", "down")

    # Same part as the channels. At 4A it has ample margin over the whole board's draw, and
    # it keeps the BOM to one MOSFET line.
    p = place("Q9", "Transistor_FET:AO3401A", "AO3401A", ix + 75, iy)
    net(p, "D", "VIN_F", "Q9", "up", "Transistor_FET:AO3401A")
    net(p, "S", "V+", "Q9", "down", "Transistor_FET:AO3401A")
    net(p, "G", "Q9GATE", "Q9", "left", "Transistor_FET:AO3401A")

    p = place("R6", "Device:R", "100k", ix + 110, iy)
    net(p, "1", "Q9GATE", "R6", "up")
    net(p, "2", "ISO_GND", "R6", "down")

    p = place("D9", "Device:D_Zener", "MMSZ5237B 8V2", ix + 140, iy)
    net(p, "1", "V+", "D9", "up")
    net(p, "2", "Q9GATE", "D9", "down")

    p = place("TVS1", "Device:D_TVS", "SMAJ30A", ix + 175, iy)
    net(p, "1", "V+", "TVS1", "up")
    net(p, "2", "ISO_GND", "TVS1", "down")

    p = place("C1", "Device:C_Polarized", "100uF/50V", ix + 210, iy)
    net(p, "1", "V+", "C1", "up")
    net(p, "2", "ISO_GND", "C1", "down")

    p = place("C2", "Device:C", "100nF", ix + 240, iy)
    net(p, "1", "V+", "C2", "up")
    net(p, "2", "ISO_GND", "C2", "down")

    # --- Output terminals ---------------------------------------------------------------
    p = place("J2", "Connector_Generic:Conn_01x06", "OUT 1-4", ix + 290, iy)
    for i, name in enumerate(["OUT1", "OUT2", "OUT3", "OUT4", "ISO_GND", "ISO_GND"], start=1):
        net(p, str(i), name, "J2", "right")

    p = place("J3", "Connector_Generic:Conn_01x06", "OUT 5-8", ix + 350, iy)
    for i, name in enumerate(["OUT5", "OUT6", "OUT7", "OUT8", "ISO_GND", "ISO_GND"], start=1):
        net(p, str(i), name, "J3", "right")

    # --- Pico socket --------------------------------------------------------------------
    # No Pico module symbol ships with KiCad, and the board really is two 20-way sockets,
    # so that is what this shows. Only the pins actually used are labelled.
    pico_left = {3: "PICO_GND", 4: "GP2", 5: "GP3", 6: "GP4", 7: "GP5",
                 8: "PICO_GND", 9: "GP6", 10: "GP7", 11: "GP8", 12: "GP9",
                 13: "PICO_GND", 18: "PICO_GND"}
    p = place("J4", "Connector_Generic:Conn_01x20", "Pico 1-20", ix + 420, iy)
    for i in range(1, 21):
        if i in pico_left:
            net(p, str(i), pico_left[i], "J4", "right")
        else:
            x, y, _ = p[str(i)]
            no_connect(x, y, f"J4-nc-{i}")

    pico_right = {23: "PICO_GND", 28: "PICO_GND", 33: "PICO_GND", 38: "PICO_GND"}
    p = place("J5", "Connector_Generic:Conn_01x20", "Pico 21-40", ix + 480, iy)
    for i in range(1, 21):
        pin40 = i + 20
        if pin40 in pico_right:
            net(p, str(i), pico_right[pin40], "J5", "right")
        else:
            x, y, _ = p[str(i)]
            no_connect(x, y, f"J5-nc-{i}")

    # --- Assemble -----------------------------------------------------------------------
    libs = "\n".join(f"\t\t{SYMBOL_DEFS[k]}" for k in NEEDED)
    sheet = f'''(kicad_sch
\t(version 20250610)
\t(generator "shop-output-board-generator")
\t(generator_version "9.99")
\t(uuid "{SHEET_UUID}")
\t(paper "A2")
\t(title_block
\t\t(title "Shop Output Board")
\t\t(company "GrblHAL Sender")
\t\t(comment 1 "8ch opto-isolated high-side switch, 5-24V")
\t\t(comment 2 "Generated by generate_schematic.py - do not hand-edit")
\t)
\t(lib_symbols
{libs}
\t)
{chr(10).join(body)}
\t(sheet_instances
\t\t(path "/"
\t\t\t(page "1")
\t\t)
\t)
\t(embedded_fonts no)
)
'''

    out_dir = args.out
    os.makedirs(out_dir, exist_ok=True)
    sch_path = os.path.join(out_dir, "shop-output-board.kicad_sch")
    with open(sch_path, "w", encoding="utf-8") as fh:
        fh.write(sheet)

    pro_path = os.path.join(out_dir, "shop-output-board.kicad_pro")
    if not os.path.exists(pro_path):
        with open(pro_path, "w", encoding="utf-8") as fh:
            fh.write('{\n  "board": {},\n  "meta": {"filename": "shop-output-board.kicad_pro", "version": 3},\n  "sheets": [["%s", "Root"]]\n}\n' % SHEET_UUID)

    print(f"wrote {sch_path}")
    print(f"{len(placed)} symbols placed")


if __name__ == "__main__":
    sys.exit(main())
