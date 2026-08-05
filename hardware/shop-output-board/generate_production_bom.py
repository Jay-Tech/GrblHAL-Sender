#!/usr/bin/env python3
"""Write production/bom.csv — the BOM an assembly house can actually buy from.

The KiCad Fabrication Toolkit plugin writes this file grouped by *footprint*, which is
wrong for assembly: it collapses C2, all eight LEDs and every resistor value into a single
"0805 x50" row, and leaves the value and part-number columns empty. An assembler receiving
that either places fifty of one part or sends it back.

Grouping has to be by (Value, Footprint) — two parts are interchangeable only if they are
the same value *and* the same package.

Through-hole parts are excluded. The plan in fabrication.md is SMT-only assembly with the
five connectors hand-soldered, and this matches the `--exclude-fp-th` position file that
goes with it: what the machine places, and what it places them from, must agree.

    python generate_production_bom.py
"""
import csv
import os
import re
import subprocess
import sys
import tempfile
from collections import OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
BOARD = os.path.join(HERE, "shop-output-board.kicad_pcb")
SCH = os.path.join(HERE, "shop-output-board.kicad_sch")
OUT = os.path.join(HERE, "production", "bom.csv")

KICAD_CLI = os.environ.get("KICAD_CLI", r"C:\Program Files\KiCad\10.0\bin\kicad-cli.exe")


def board_mount_types():
    """ref -> 'smd' | 'through_hole', read from the board rather than guessed from names."""
    src = open(BOARD, encoding="utf-8").read()
    out = {}
    for m in re.finditer(r'\(footprint "', src):
        s = m.start()
        d = 0
        for j in range(s, len(src)):
            if src[j] == "(":
                d += 1
            elif src[j] == ")":
                d -= 1
                if d == 0:
                    blk = src[s:j + 1]
                    break
        ref = re.search(r'\(property "Reference" "([^"]+)"', blk)
        attr = re.search(r"\(attr ([a-z_]+)", blk)
        if ref:
            out[ref.group(1)] = attr.group(1) if attr else "unknown"
    return out


def schematic_fields():
    """One row per reference, straight from the schematic via kicad-cli."""
    fd, tmp = tempfile.mkstemp(suffix=".csv")
    os.close(fd)
    try:
        subprocess.run(
            [KICAD_CLI, "sch", "export", "bom", "--output", tmp,
             "--fields", "Reference,Value,MPN,Manufacturer,Footprint",
             "--labels", "Reference,Value,MPN,Manufacturer,Footprint", SCH],
            check=True, capture_output=True)
        with open(tmp, encoding="utf-8-sig", newline="") as fh:
            return list(csv.DictReader(fh))
    finally:
        os.unlink(tmp)


def natural_key(ref):
    m = re.match(r"([A-Za-z]+)(\d*)", ref)
    return (m.group(1), int(m.group(2) or 0))


def main():
    mounts = board_mount_types()
    rows = schematic_fields()

    groups = OrderedDict()
    skipped = []
    for r in rows:
        ref = r["Reference"]
        if mounts.get(ref) != "smd":
            skipped.append(ref)
            continue
        key = (r["Value"], r["Footprint"])
        g = groups.setdefault(key, {"refs": [], "mpn": r["MPN"], "mfr": r["Manufacturer"]})
        g["refs"].append(ref)
        # An MPN that varies within a value/footprint group means the group is not really
        # one part. Better to fail than to ship a BOM that quietly picks one.
        if g["mpn"] != r["MPN"]:
            sys.exit("%s: MPN differs within group %s (%r vs %r)"
                     % (ref, key, g["mpn"], r["MPN"]))

    missing = [k for k, g in groups.items() if not g["mpn"].strip()]
    if missing:
        sys.exit("no MPN for: %s" % ", ".join("%s / %s" % k for k in missing))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(["Designator", "Quantity", "Value", "Footprint", "MPN", "Manufacturer"])
        for (value, footprint), g in sorted(
                groups.items(), key=lambda kv: natural_key(sorted(kv[1]["refs"], key=natural_key)[0])):
            refs = sorted(g["refs"], key=natural_key)
            w.writerow([",".join(refs), len(refs), value,
                        footprint.split(":", 1)[-1], g["mpn"], g["mfr"]])

    print("wrote %s" % OUT)
    print("%d SMD parts in %d distinct part types" %
          (sum(len(g["refs"]) for g in groups.values()), len(groups)))
    print("excluded %d through-hole parts (hand-soldered): %s"
          % (len(skipped), ", ".join(sorted(skipped, key=natural_key))))


if __name__ == "__main__":
    main()
