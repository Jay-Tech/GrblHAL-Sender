#!/usr/bin/env python3
"""Write JLCPCB assembly files into JlcLab/ — BOM and CPL.

Two things make this more than a CSV dump.

**Rotation.** JLCPCB's placement convention is not KiCad's. A part's angle only means
something relative to how that package sits in the assembler's own library, and for three
package families here it differs. Getting it wrong gives you a board of backwards MOSFETs
and optocouplers that passes every check you can run at home, because nothing about the
*board* is wrong. The corrections below were derived empirically by diffing the KiCad
Fabrication Toolkit's AUTO TRANSLATE output against plain `kicad-cli pcb export pos`, on both
v1 and v2 — they agreed on every part, which is why they are trusted here.

**Grouping.** The Fabrication Toolkit groups its BOM by *footprint*, which collapses C2, all
four LEDs and every resistor value into one "0805 x26" row with an empty value column. An
assembler receiving that either rejects it or — worse — places 26 of one part. Grouping has
to be by (value, footprint): two parts are interchangeable only if they are the same value
*and* the same package.

    python generate_jlcpcb.py

Through-hole parts are excluded from both files. The five connectors are hand-soldered, and
the BOM and CPL must agree on that or the machine is told to place a part it has no reel for.
"""
import csv
import os
import re
import subprocess
import sys
import tempfile
from collections import OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
BOARD = os.path.join(HERE, "shop-output-board-v2.kicad_pcb")
SCH = os.path.join(HERE, "shop-output-board-v2.kicad_sch")
OUTDIR = os.path.join(HERE, "JlcLab")
KICAD_CLI = os.environ.get("KICAD_CLI", r"C:\Program Files\KiCad\10.0\bin\kicad-cli.exe")

# Candidate LCSC part numbers, keyed by the schematic Value.
#
# C-codes are stable identifiers, but stock and Basic/Extended status are not — check every
# line in JLCPCB's own parts search before ordering. Where the code is for an equivalent
# rather than the exact MPN in the schematic, that is called out.
#
# Blank means no candidate was found and it needs a decision. See JlcLab/README.md.
LCSC = {
    "100k":              "C17407",    # UNI-ROYAL 0805W8F1003T5E, Basic
    "4k7":               "C17673",    # UNI-ROYAL 0805W8F4701T5E, Basic
    "10k":               "C17414",    # UNI-ROYAL 0805W8F1002T5E, Basic
    "2k2":               "C17520",    # UNI-ROYAL 0805W8F2201T5E, Basic
    "470R":              "C17710",    # UNI-ROYAL 0805W8F4700T5E, Basic
    "100nF":             "C49678",    # YAGEO CC0805KRX7R9BB104, Basic
    "47uF/50V":          "C970679",   # DMBJ RVT1H470M0607, exact 6.3x7.7 body
    "AO3401A":           "C15127",    # exact MPN, Basic
    "SMAJ30A":           "C134979",   # Diodes SMAJ30A-13-F

    # EQUIVALENT, not the specified MMSZ5237B: BZT52C8V2-7-F is the same 8.2V 500mW SOD-123
    # part and is what LCSC actually stocks. Electrically interchangeable here.
    "MMSZ5237B 8V2":     "C500790",

    # RANK C, not the specified rank B — LCSC does not stock B. This is an UPGRADE, not a
    # compromise: the design needs CTR >= 75% and rank C is 200-400% against rank B's
    # 130-260%. More CTR is pure margin here because R2 caps the collector current at
    # 3.36 mA regardless. Do NOT accept rank A (80-160%) — its floor is too close to 75%.
    "LTV-817S (CTR B)":  "C109227",

    # Generic 0805 green. The specified LTST-C170KGKT is high-efficiency for a reason: at a
    # 5V supply this LED only gets ~1.4 mA. Check the mcd rating before accepting.
    "GRN":               "C2297",

    # NO CANDIDATE. The Bourns MF-MSMF110/24X-2 does not appear in LCSC's catalogue.
    # Littelfuse 1812L110/24 is the same specification (1.1A hold, 24V, 1812) if JLCPCB has
    # it. Do not accept a lower-voltage substitute — 24V is the whole reason this is an 1812.
    "1.1A":              "",
}

# Added to KiCad's angle to get JLCPCB's. Anything not listed is 0.
# Verified against the Fabrication Toolkit's own output — see the module docstring.
ROTATION_FIX = {
    "Package_TO_SOT_SMD:SOT-23": 180,
    "Package_SO:SOP-4_7.5x4.1mm_P2.54mm": 270,
    "Capacitor_SMD:CP_Elec_6.3x7.7": 180,
    "Capacitor_SMD:CP_Elec_8x10.5": 180,
}


def footprint_blocks(src):
    for m in re.finditer(r'\(footprint "([^"]+)"', src):
        s = m.start()
        d = 0
        for j in range(s, len(src)):
            if src[j] == "(":
                d += 1
            elif src[j] == ")":
                d -= 1
                if d == 0:
                    yield m.group(1), src[s:j + 1]
                    break


def read_board():
    """ref -> (footprint, x, y, kicad_rotation, mount_type, layer)"""
    src = open(BOARD, encoding="utf-8").read()
    out = {}
    for fp, blk in footprint_blocks(src):
        ref = re.search(r'\(property "Reference" "([^"]+)"', blk)
        at = re.search(r"\(at ([-\d.]+) ([-\d.]+)(?: ([-\d.]+))?\)", blk)
        attr = re.search(r"\(attr ([a-z_]+)", blk)
        layer = re.search(r'\(layer "([FB])\.Cu"\)', blk)
        if not (ref and at):
            continue
        out[ref.group(1)] = (fp, float(at.group(1)), float(at.group(2)),
                             float(at.group(3) or 0),
                             attr.group(1) if attr else "unknown",
                             "top" if not layer or layer.group(1) == "F" else "bottom")
    return out


def read_schematic():
    """ref -> {Value, MPN, Manufacturer, Footprint}, straight from the schematic."""
    fd, tmp = tempfile.mkstemp(suffix=".csv")
    os.close(fd)
    try:
        subprocess.run(
            [KICAD_CLI, "sch", "export", "bom", "--output", tmp,
             "--fields", "Reference,Value,MPN,Manufacturer,Footprint",
             "--labels", "Reference,Value,MPN,Manufacturer,Footprint", SCH],
            check=True, capture_output=True)
        with open(tmp, encoding="utf-8-sig", newline="") as fh:
            return {r["Reference"]: r for r in csv.DictReader(fh)}
    finally:
        os.unlink(tmp)


def natural_key(ref):
    m = re.match(r"([A-Za-z]+)(\d*)", ref)
    return (m.group(1), int(m.group(2) or 0))


def main():
    board = read_board()
    sch = read_schematic()
    os.makedirs(OUTDIR, exist_ok=True)

    smd = {r: v for r, v in board.items() if v[4] == "smd"}
    skipped = sorted(set(board) - set(smd), key=natural_key)

    # --- CPL ---------------------------------------------------------------------------
    cpl_path = os.path.join(OUTDIR, "cpl.csv")
    applied = {}
    with open(cpl_path, "w", encoding="utf-8", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(["Designator", "Mid X", "Mid Y", "Layer", "Rotation"])
        for ref in sorted(smd, key=natural_key):
            fp, x, y, rot, _, layer = smd[ref]
            fix = ROTATION_FIX.get(fp, 0)
            if fix:
                applied.setdefault(fp, []).append(ref)
            # KiCad's Y grows downward; JLCPCB wants the board origin with Y up.
            w.writerow([ref, "%g" % x, "%g" % -y, layer, "%g" % ((rot + fix) % 360)])

    # --- BOM ---------------------------------------------------------------------------
    groups = OrderedDict()
    for ref in sorted(smd, key=natural_key):
        row = sch.get(ref)
        if row is None:
            sys.exit("%s is on the board but not in the schematic" % ref)
        key = (row["Value"], row["Footprint"].split(":", 1)[-1])
        g = groups.setdefault(key, {"refs": [], "mpn": row["MPN"], "mfr": row["Manufacturer"]})
        g["refs"].append(ref)
        if g["mpn"] != row["MPN"]:
            sys.exit("%s: MPN differs inside group %s" % (ref, key))

    bom_path = os.path.join(OUTDIR, "bom.csv")
    with open(bom_path, "w", encoding="utf-8", newline="") as fh:
        w = csv.writer(fh)
        # The first four are JLCPCB's required columns. MPN/Manufacturer are extra and give
        # their parts matcher something to work with while LCSC Part # is still blank.
        w.writerow(["Comment", "Designator", "Footprint", "LCSC Part #",
                    "MPN", "Manufacturer", "Quantity"])
        for (value, fp), g in sorted(groups.items(),
                                     key=lambda kv: natural_key(sorted(kv[1]["refs"],
                                                                      key=natural_key)[0])):
            refs = sorted(g["refs"], key=natural_key)
            w.writerow([value, ",".join(refs), fp, LCSC.get(value, ""),
                        g["mpn"], g["mfr"], len(refs)])

    print("wrote %s  (%d parts)" % (cpl_path, len(smd)))
    print("wrote %s  (%d distinct part types)" % (bom_path, len(groups)))
    print()
    print("rotation corrections applied (KiCad -> JLCPCB):")
    for fp, refs in sorted(applied.items()):
        print("   %-45s %+4d deg  %s" % (fp, ROTATION_FIX[fp], ",".join(sorted(refs, key=natural_key))))
    print()
    print("excluded %d through-hole parts (hand-soldered): %s"
          % (len(skipped), ", ".join(skipped)))
    missing = [k for k, g in groups.items() if not g["mpn"].strip()]
    if missing:
        print("\nWARNING - no MPN for: %s" % ", ".join("%s/%s" % k for k in missing))
    blank = sorted({v for (v, _fp) in groups if not LCSC.get(v)})
    if blank:
        print("\nNO LCSC CANDIDATE for: %s" % ", ".join(blank))
        print("   -> needs a decision before ordering; see JlcLab/README.md")
    subs = [v for (v, _fp) in groups
            if LCSC.get(v) and v in ("MMSZ5237B 8V2", "LTV-817S (CTR B)", "GRN")]
    if subs:
        print("\nEQUIVALENT rather than the exact MPN: %s" % ", ".join(sorted(subs)))
        print("   -> read the notes in the LCSC table before accepting")
    print("\nEvery LCSC code is a CANDIDATE. C-codes are stable but stock and")
    print("Basic/Extended status are not - confirm each in JLCPCB's parts search.")


if __name__ == "__main__":
    main()
