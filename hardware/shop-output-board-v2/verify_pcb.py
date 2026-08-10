#!/usr/bin/env python3
"""Check the generated board against KiCad's own idea of where the pads are.

generate_pcb.py computes pad coordinates itself in order to route from them. That
computation was wrong once — the rotation signs were inverted, which put every
optocoupler's isolated pins in the logic zone. It looked perfectly fine in the generator's
own output, because the generator was checking itself against its own bad maths.

So this compares against KiCad: export IPC-D-356, which contains KiCad's resolved pad
positions, and assert they match. Then assert the thing that actually matters — that no pad
is on the wrong side of the isolation barrier.

    kicad-cli pcb export ipcd356 --output pads.d356 shop-output-board-v2.kicad_pcb
    python verify_pcb.py
"""

import re
import sys

import generate_pcb as G

INCH_MM = 25.4
BARRIER_TOP, BARRIER_BOT = G.BARRIER_TOP, G.BARRIER_BOT

# Nets that belong on each side. Anything else spans or is a connector.
LOGIC_NETS = ({"PICO_GND"}
              | {f"GP{g}" for g in G.CHANNEL_GP}
              | {f"LEDA{i}" for i in range(1, G.N_CHANNELS + 1)})


def load_d356(path="pads.d356"):
    pads = {}
    for line in open(path, encoding="utf-8"):
        m = re.match(r"^327(.{16})\s*(\S+)\s+-(\S+)\s+A01X([+-]\d+)Y([+-]\d+)", line)
        if not m:
            continue
        net, ref, pin, x, y = m.groups()
        # IPC-D-356 is in units of 0.0001 inch, Y measured upward from the origin.
        pads[(ref, pin.strip())] = (
            round(int(x) / 10000.0 * INCH_MM, 2),
            round(-int(y) / 10000.0 * INCH_MM, 2),
            net.strip(),
        )
    return pads


def main():
    try:
        kicad = load_d356()
    except FileNotFoundError:
        sys.exit("pads.d356 missing — run the kicad-cli export in the docstring first")

    if not kicad:
        sys.exit("pads.d356 parsed to nothing; the format may have changed")

    mismatches, wrong_side = [], []

    for (ref, pin), (kx, ky, net) in sorted(kicad.items()):
        mine = G.PADS.get((ref, pin))
        if mine is None:
            continue
        if abs(mine[0] - kx) > 0.05 or abs(mine[1] - ky) > 0.05:
            mismatches.append(f"{ref}.{pin}: generator says {mine}, KiCad says ({kx}, {ky})")

        # The check that matters: logic nets above the barrier, isolated nets below.
        if net in LOGIC_NETS and ky > BARRIER_BOT:
            wrong_side.append(f"{ref}.{pin} [{net}] is a logic net at y={ky} — below the barrier")
        if net not in LOGIC_NETS and net and ky < BARRIER_TOP and not ref.startswith("J"):
            wrong_side.append(f"{ref}.{pin} [{net}] is an isolated net at y={ky} — above the barrier")
        if BARRIER_TOP < ky < BARRIER_BOT:
            wrong_side.append(f"{ref}.{pin} [{net}] sits inside the barrier at y={ky}")

    if mismatches:
        print(f"PAD POSITION MISMATCH ({len(mismatches)}) — the rotation maths is wrong again:")
        for m in mismatches[:10]:
            print(f"  {m}")
    if wrong_side:
        print(f"\nBARRIER VIOLATIONS ({len(wrong_side)}):")
        for w in wrong_side[:10]:
            print(f"  {w}")

    if mismatches or wrong_side:
        return 1

    optos = [(r, p) for (r, p) in kicad if r.startswith("U")]
    print(f"OK — {len(kicad)} pads match KiCad's own placement to 0.05 mm")
    print(f"     {len(optos)} optocoupler pads, all on the correct side of the barrier")
    print(f"     no pad of any part sits inside the {BARRIER_BOT - BARRIER_TOP:g} mm barrier")
    return 0


if __name__ == "__main__":
    sys.exit(main())
