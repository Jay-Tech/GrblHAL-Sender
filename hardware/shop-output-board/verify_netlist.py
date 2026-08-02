#!/usr/bin/env python3
"""Check the exported netlist against the intended design.

ERC proves the schematic is well formed; it says nothing about whether it is the circuit we
meant. This asserts the connectivity in netlist.md, so a placement mistake in the generator
cannot quietly produce a valid schematic of the wrong board.

    kicad-cli sch export netlist --output netlist.net shop-output-board.kicad_sch
    python verify_netlist.py
"""

import sys
from generate_schematic import tokenize, parse


def find(node, key):
    return [c for c in node if isinstance(c, list) and c and c[0] == key]


def load(path="netlist.net"):
    root, _ = parse(tokenize(open(path, encoding="utf-8").read()), 0)
    nets = {}
    for net in find(find(root, "nets")[0], "net"):
        name = find(net, "name")[0][1].strip('"').lstrip("/")
        nets[name] = {
            f'{find(nd, "ref")[0][1].strip(chr(34))}.{find(nd, "pin")[0][1].strip(chr(34))}'
            for nd in find(net, "node")
        }
    return nets


def main():
    nets = load()
    failures = []

    def expect(net, members, exact=True):
        actual = nets.get(net)
        if actual is None:
            failures.append(f"{net}: net missing entirely")
            return
        want = set(members)
        if exact and actual != want:
            missing, extra = want - actual, actual - want
            detail = []
            if missing:
                detail.append(f"missing {sorted(missing)}")
            if extra:
                detail.append(f"unexpected {sorted(extra)}")
            failures.append(f"{net}: " + "; ".join(detail))
        elif not exact and not want <= actual:
            failures.append(f"{net}: missing {sorted(want - actual)}")

    # --- The isolation barrier. Everything else is detail next to this. ------------------
    iso, pico = nets.get("ISO_GND", set()), nets.get("PICO_GND", set())
    if iso & pico:
        failures.append(f"ISOLATION BREACHED: {sorted(iso & pico)} on both grounds")
    if not iso or not pico:
        failures.append("one of the ground nets is missing")

    # Only the optos may straddle the barrier, and only via their own two sides.
    for n in range(1, 9):
        if f"U{n}.2" not in pico:
            failures.append(f"U{n}.2 (opto LED cathode) not on PICO_GND")
        if f"U{n}.3" not in iso:
            failures.append(f"U{n}.3 (opto emitter) not on ISO_GND")

    # --- Per channel --------------------------------------------------------------------
    for n in range(1, 9):
        expect(f"GATE{n}", {f"R{n}01.2", f"D{n}.2", f"Q{n}.G", f"R{n}02.1"})
        expect(f"OPTOC{n}", {f"R{n}02.2", f"U{n}.4"})
        expect(f"LEDA{n}", {f"R{n}05.2", f"U{n}.1"})
        expect(f"LEDK{n}", {f"R{n}04.2", f"LED{n}.1"})
        expect(f"GP{n + 1}", {f"R{n}05.1", "J4." + str(n + 3 if n <= 4 else n + 4)})

        terminal = "J2" if n <= 4 else "J3"
        pin = n if n <= 4 else n - 4
        expect(f"OUT{n}", {f"Q{n}.D", f"R{n}03.1", f"R{n}04.1", f"{terminal}.{pin}"})

        # Zener cathode and MOSFET source both to V+; anode and gate to GATEn. Reversed,
        # the clamp does nothing and the gate is shorted.
        expect("V+", {f"D{n}.1", f"Q{n}.S", f"R{n}01.1"}, exact=False)
        expect("ISO_GND", {f"R{n}03.2", f"LED{n}.2"}, exact=False)

    # --- Input protection ---------------------------------------------------------------
    expect("VIN_RAW", {"J1.1", "F1.1"})
    expect("VIN_F", {"F1.2", "Q9.D"})       # drain to supply side
    expect("Q9GATE", {"R6.1", "D9.2", "Q9.G"})
    expect("V+", {"Q9.S", "D9.1", "TVS1.1", "C1.1", "C2.1"}, exact=False)
    expect("ISO_GND", {"J1.2", "R6.2", "TVS1.2", "C1.2", "C2.2"}, exact=False)

    # --- Terminals ----------------------------------------------------------------------
    expect("ISO_GND", {"J2.5", "J2.6", "J3.5", "J3.6"}, exact=False)

    if failures:
        print(f"FAILED ({len(failures)} problems)\n")
        for f in failures:
            print(f"  {f}")
        return 1

    print(f"OK - {len(nets)} nets, all assertions passed")
    print(f"     isolation intact: {len(nets['ISO_GND'])} nodes on ISO_GND, "
          f"{len(nets['PICO_GND'])} on PICO_GND, 0 shared")
    return 0


if __name__ == "__main__":
    sys.exit(main())
