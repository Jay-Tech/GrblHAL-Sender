# KiCad cheat sheet

Written from the shop output board build — the shortcuts worth knowing, the workflow that
works, and the things that actually cost time. See `hardware/shop-output-board/` for the
board this came out of.

> **Before trusting any hotkey below: press `Ctrl+F1`.** That opens KiCad's own list of the
> hotkeys *currently* bound in *your* install. Hotkeys are user-configurable and change
> between versions, so the built-in list always beats a cheat sheet. Everything here is the
> KiCad 10 default.

---

## Pcbnew (board editor)

| Key | Does |
|---|---|
| `X` | Route a track |
| `V` | Drop a via — **while routing**. There is no standalone "place via" tool |
| `E` | Properties of whatever is selected. Also how you type exact coordinates |
| `M` | Move · `D` drag (keeps connections) · `R` rotate · `F` flip to other side |
| `B` | Fill all zones. Do this before believing any DRC result |
| `Delete` | Delete selected |
| `Esc` | Cancel the current operation |
| `PgUp` / `PgDn` | Jump to F.Cu / B.Cu |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `Ctrl+S` | Save **board** — see the note about the project file below |

**Selection Filter panel (bottom right)** is the tool nobody mentions and everybody needs.
When items overlap — five ground zones stacked on each other, say — untick everything except
the type you want and clicks can only pick that. It turns an impossible click into a trivial
one.

Where outlines overlap exactly, KiCad shows a **clarification menu** listing candidates.
Hover each one and it highlights, so you can tell which is which before committing.

---

## Eeschema (schematic editor)

| Key | Does |
|---|---|
| `A` | Add symbol |
| `W` | Draw a wire |
| `L` | Net label — connects by name, no wire needed |
| `E` | Properties · `M` move · `R` rotate · `G` drag |
| `Ctrl+S` | Save |

**Net labels beat long wires.** Two pins with the same label are connected, full stop. On a
repetitive board that is far less error-prone than routing wires across the sheet, and the
netlist comes out identical.

---

## The workflow, in order

Each step gates the next. Skipping ahead wastes the most time.

1. **Schematic** → run **ERC** (Inspect → Electrical Rules Checker)
2. **Assign footprints** — every symbol needs one
3. **Update PCB from Schematic** (`F8` in Pcbnew) — pulls footprints in with the netlist
4. **Board outline** on the `Edge.Cuts` layer. No outline, no board
5. **Net classes** (Board Setup → Net Classes) — *then save the project*
6. **Place** parts
7. **Route**
8. **Draw ground zones**, then `B` to fill
9. **Add stitching vias** (see below)
10. **DRC** until clean
11. **Export** Gerbers + drill

---

## Things that cost real time on this board

### Net classes live in the project file, not the board

`Ctrl+S` saves the **board**. Net classes are stored in `<name>.kicad_pro`. Set them up, hit
Ctrl+S, close, reopen — and they're gone, because you never saved the project.

This matters beyond tidiness: custom DRC rules match on net class. Until the classes exist,
a `.kicad_dru` file sits there doing nothing and DRC passes boards it should be rejecting.

#### How to actually set them

In Pcbnew: **File → Board Setup → Design Rules → Net Classes.** The dialog has two panes.

**Top pane — define the classes.** Click **+** to add a row and name it. Set track width,
clearance and via size for that class if you want them to differ from Default; leave blank to
inherit. For the shop output board, add two: `Logic` and `Isolated`.

**Bottom pane — assign nets to them.** This is the part that's easy to miss: creating a class
does nothing until nets are assigned. Click **+** to add a row, enter a **pattern** and pick
the class. Patterns use `*` as a wildcard and match the full net name.

For this board:

| Pattern | Class |
|---|---|
| `PICO_GND` | Logic |
| `GP*` | Logic |
| `LEDA*` | Logic |
| `ISO_GND` | Isolated |
| `V+` | Isolated |
| `VIN_*` | Isolated |
| `Q9GATE` | Isolated |
| `GATE*` | Isolated |
| `OPTOC*` | Isolated |
| `OUT*` | Isolated |
| `LEDK*` | Isolated |

That covers 54 of the 78 nets. The other 24 are `unconnected-(J4-Pin_…)` — Pico socket pins
deliberately left unconnected — and need no class.

Watch that `LEDA*` (logic, opto LED anodes) and `LEDK*` (isolated, indicator cathodes) are
different prefixes. `GATE*` catches `GATE1`–`GATE8` but not `Q9GATE`, which is why that one is
listed separately.

**Then: File → Save Project.** Not Ctrl+S.

#### Confirming it took

Three checks, cheapest first:

1. Reopen Board Setup → Net Classes. The classes and assignments should still be listed
2. Board Setup → Design Rules → **Custom Rules** — the parsed rules should appear. Empty pane
   means the `.kicad_dru` isn't being read
3. Look in `<name>.kicad_pro` — under `net_settings` you should see your class names and a
   populated `netclass_patterns` array. Only `Default` and an empty patterns list means it
   didn't save

### Custom DRC rules need three things to fire

1. A `<projectname>.kicad_dru` file **beside the board**, name matching the project
2. Net classes defined and **assigned to nets**
3. The rule conditions referring to those class names

Check it loaded: Board Setup → Design Rules → Custom Rules should show it parsed. If the
panel is empty, the file isn't being read.

### Layers are not zones

A **layer** is a physical side of the board — 2-layer means `F.Cu` and `B.Cu`. Set in Board
Setup → Board Editor Layers. You cannot delete one by accident with the Delete key.

A **zone** (copper pour) is an object *drawn on* layers. When something "disappears", it is
almost always a zone, not a layer.

### A zone on two layers is two separate sheets of copper

Setting a zone to "F.Cu and B.Cu" produces **two independent pours**, one per side, with
1.6 mm of fibreglass between them. Giving them the same net name says what they *should* be.
It does not join them.

**Stitching vias** are what join them — vias whose only job is tying the two sheets together.
Without them the back pour is floating copper doing nothing, and DRC reports the net as
unconnected to itself.

### Every fragment of a pour needs its own via

Traces slice a pour into islands. On this board 348 segments chopped the front ground pour
into **16 separate islands**. Six vias only reached four of them; the other twelve were
orphaned — each holding ground pads that connected to nothing.

Rule of thumb: after routing, every enclosed region of pour needs at least one via. The gaps
between columns of repeated circuitry are where they hide.

### Zone priority decides who wins where they overlap

Higher number wins. A zone at priority 1 covering the whole board will override a priority 0
zone underneath it — which is how a ground pour ends up flooding a region it was never meant
to touch, silently.

If pads that should be on one net suddenly read unconnected, check whether a higher-priority
zone of a *different* net has taken that area.

### Refill zones before believing DRC

`kicad-cli pcb drc --refill-zones`, or `B` in the GUI. Stale fills produce phantom unconnected
errors — on this board it was the difference between 63 and 24.

### Do not edit the board file externally while Pcbnew has it open

Pcbnew holds the board in memory from the moment it opened. Edit the file on disk underneath
it and your changes are invisible to that session — then the next `Ctrl+S` writes the
in-memory version straight over them.

Cost us a set of ground zones. Close the editor, or make the change in the editor.

### Symbol pin numbers must match footprint pad names

`Device:Q_PMOS` numbers its pins `G`/`D`/`S`. A SOT-23 footprint names its pads `1`/`2`/`3`.
They will not map, and the board imports with every transistor unconnected.

Use the **real part symbol** (`Transistor_FET:AO3401A`) rather than a generic one — it carries
the correct numbering and often the right footprint too. Same trap with a 2-pin Zener symbol
on a 3-pad SOT-23 land pattern: use SOD-123.

### KiCad's rotation is not the textbook rotation matrix

KiCad rotates counter-clockwise on screen and screen Y grows *downward*, so:

```
x' =  px·cos(a) + py·sin(a)
y' = -px·sin(a) + py·cos(a)
```

Getting the signs backwards mirrors every rotated part. On this board it put the
optocouplers' isolated pins in the logic zone — and it looked completely fine, because the
script checking it was using the same wrong maths.

**Check rotations against KiCad's own output**, not your own arithmetic:
`kicad-cli pcb export ipcd356` gives resolved pad coordinates straight from KiCad.

---

## Reading DRC output

| What it says | What it means |
|---|---|
| `unconnected_items` between a **pad** and a **track** | A trace stops short. Look at both coordinates — usually a fraction of a millimetre |
| `unconnected_items` between a **zone and itself** | The pour is fragmented, or its two layers aren't stitched. Add vias |
| `shorting_items` | Two nets touching. Almost always a trace crossing a pad it shouldn't |
| `tracks_crossing` | Two traces on the same layer intersecting |
| `courtyards_overlap` | Two parts physically too close to assemble |
| `starved_thermal` | Pad reaches the pour with fewer spokes than required. Cosmetic-ish |
| `silk_overlap` | Reference designators colliding. Cosmetic |
| `endpoint_off_grid` | Endpoints off the 1.27 mm grid. Harmless, but it buries real errors in noise |

"Found 0 violations" on an **empty** board is also zero. Always sanity-check the counts —
footprints, segments, vias, zones — alongside the DRC result.

---

## Command line

`kicad-cli` lives at `C:\Program Files\KiCad\10.0\bin\kicad-cli.exe` and is not on PATH by
default.

```
kicad-cli sch erc --output erc.rpt --severity-error board.kicad_sch
kicad-cli sch export netlist --output netlist.net board.kicad_sch
kicad-cli sch export pdf --output board.pdf board.kicad_sch
kicad-cli sch export bom --output bom.csv --fields "Reference,Value,Footprint" --group-by "Value" board.kicad_sch

kicad-cli pcb drc --refill-zones --output drc.rpt --severity-error --severity-warning board.kicad_pcb
kicad-cli pcb export ipcd356 --output pads.d356 board.kicad_pcb
kicad-cli pcb export svg --output board.svg --layers "F.Cu,B.Cu,F.SilkS,Edge.Cuts" board.kicad_pcb

kicad-cli pcb export gerbers --output fab/ board.kicad_pcb
kicad-cli pcb export drill --output fab/ --format excellon --excellon-separate-th board.kicad_pcb
kicad-cli pcb export pos --output fab/positions.csv --format csv --units mm --side both --exclude-fp-th board.kicad_pcb
```

`--exclude-fp-th` on the position file leaves through-hole parts out, so an assembly house
places only the SMD parts you're paying it to place.

DRC and ERC exit non-zero when anything is reported, including expected unconnected items on
a part-routed board. Read the output rather than the exit code.

---

## Before ordering

- Run DRC with the custom rules present, and confirm zero
- Check the vendor's layer render — particularly that the board outline exported
- Take the free DFM check
- **Order 5, not 50.** The likeliest fault on a first board is a footprint that doesn't match
  the part you actually bought
- Breadboard the circuit before it becomes copper. ERC, DRC and netlist checks verify
  topology and geometry. None of them verify that the design works
