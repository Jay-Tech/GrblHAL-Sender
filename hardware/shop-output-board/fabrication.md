# Getting the board made

Written for someone doing this for the first time. Read the "where you are" section before
uploading anything anywhere.

## Where you are

| Step | Status |
|---|---|
| Schematic | done, ERC clean, netlist verified |
| Footprints assigned | done, checked |
| Layout plan and DRC rules | done |
| Board placed | done — 84 parts positioned |
| Board routed | **done** — 348 segments, 25 vias, DRC 0 violations, 0 unconnected |
| Gerbers | exported and tracked in `ShopOutput_Gerber/` |
| BOM | 84 parts, every line with an MPN |
| F1 voltage rating | **settled** — 1812, Bourns MF-MSMF110/24X-2, 1.1A / 24V |
| Assembly files | `production/` — BOM grouped by value, positions exclude through-hole |
| Part rotations for assembly | **not settled** — see the warning below |
| **Bench verification** | **none — this is what is left** |

The board is routed and the Gerbers are exported. What has *not* happened is any bench
verification: **breadboard one channel** before committing to a run. The design rests on a
gate clamp nobody has watched work, and finding out it wants a different Zener costs an
evening on a breadboard versus a board respin.

> **Rotations in `production/positions.csv` have not been translated for any assembler.**
> They are KiCad's native angles. Under JLCPCB's convention eighteen parts sit differently —
> C1 and Q1–Q9 by 180°, U1–U8 by 270° — and every one of them is polarized, so getting this
> wrong means a board of backwards MOSFETs and optocouplers. Confirm the convention with
> whoever does the placement and re-export to match. **Bare boards do not use this file**, so
> it does not block the next order.

## Step 1 — route the board in KiCad

Open the project, then in Pcbnew:

Open `shop-output-board.kicad_pcb`. Placement, board outline, barrier markings and both
ground zones are already there, so most of steps 1–4 of a normal first board are done.

1. **Set net classes** (Board Setup → Net Classes) as listed in [layout.md](layout.md), then
   confirm Board Setup → Design Rules → Custom Rules has picked up the `.kicad_dru` file.
2. **Route.** Interactive router, default hotkey `X`. Follow the ratsnest. Per channel that
   is two real traces — GATE and OUT — plus taps to the V+ rail; everything marked
   `ISO_GND` or `PICO_GND` is a via into a pour.
3. **DRC** (Inspect → Design Rules Checker) until it is clean. The custom rule means a trace
   bridging the two domains shows up here as an error rather than as a surprise later.

Routing is still real work — expect a few evenings for a first board, and that is normal.

## Step 2 — export

From this directory, once `shop-output-board.kicad_pcb` exists and DRC is clean:

```
kicad-cli pcb export gerbers --output ShopOutput_Gerber/ --no-protel-ext --layers "F.Cu,B.Cu,F.Mask,B.Mask,F.Paste,B.Paste,F.Silkscreen,B.Silkscreen,Edge.Cuts" shop-output-board.kicad_pcb
kicad-cli pcb export drill --output ShopOutput_Gerber/ --format excellon --excellon-separate-th shop-output-board.kicad_pcb
```

That is everything needed for a **bare board** — 12 files. Zip the contents and upload the zip.

Both flags matter. Without `--layers`, KiCad also plots Courtyard, Fab, Adhesive, Comments,
Drawings and Margin: internal documentation layers that have no business going to a fab and
that some vendors will silently try to make. Without `--no-protel-ext` the files come out as
`.gtl`/`.gbl`/`.gts` instead of `.gbr`, which — if the directory already holds a `.gbr` set —
leaves two generations of Gerbers side by side with nothing to say which is current.

For **SMT assembly**, two more files. Note `--exclude-fp-th`: it leaves the connectors out
of the position file, so the machine places only the parts you are paying it to place.

```
kicad-cli pcb export pos --output production/positions.csv --format csv --units mm --side both --exclude-fp-th shop-output-board.kicad_pcb
python generate_production_bom.py
```

The position file tells the machine where each part goes; the BOM says what each part is.
`--exclude-fp-th` leaves the five connectors out of the position file, and
`generate_production_bom.py` leaves the same five out of the BOM — the two must agree, or the
machine is either told to place a part it has no reel for, or handed a reel for a part it is
not placing.

## Step 3 — how much do you assemble?

The board is deliberately **79 SMD parts and 5 through-hole**. The through-hole parts are the
three screw terminals and the two Pico socket strips — everything a machine would find
awkward and you would find easy.

| Option | What you get | Cost shape |
|---|---|---|
| **Bare board** | PCB only, you solder all 84 parts | Cheapest board, most labour |
| **Partial assembly (SMT only)** | Machine places all 79 SMD parts; you solder the 5 connectors | Board + setup + per-part fees |
| **Full turnkey** | Everything placed, including through-hole | Most expensive; through-hole placement carries its own charge |

**Partial assembly is the right fit here** and is a standard service — PCBWay and JLCPCB both
call it SMT assembly, with through-hole left to the customer by default. Nothing about the
design needs changing for it. The optocouplers are gull-wing SOP-4 rather than DIP precisely
so that no SMD part is left stranded on a through-hole package.

What assembly needs beyond the Gerbers:

- **A position file with the connectors excluded**, so the machine does not try to place them:
  `kicad-cli pcb export pos --exclude-fp-th ...` (the flag exists for exactly this)
- **A BOM with real manufacturer part numbers.** "100k, 0805" is not orderable. Every line
  needs an actual MPN the assembler can buy
- **A BOM grouped by value, not by footprint.** This sounds pedantic and is not: grouping by
  footprint alone collapses C2, all eight LEDs and every resistor value into one "0805 × 50"
  row, which is either rejected or — worse — placed. `generate_production_bom.py` groups by
  (value, footprint) and refuses to emit a line without an MPN
- **Awareness that unique part types drive the price.** There is a setup charge per distinct
  component regardless of how many are placed, so this board's thirteen types cost about the
  same to set up whether it is one board or ten. That is why Q9 was made the same AO3401A as
  the channels rather than a different MOSFET

## Cost sequencing that avoids waste

Assembly is worth paying for once the circuit is proven, and wasteful before that. So:

1. **Breadboard one channel.** No PCB involved. Confirms the gate clamp works at 5 V and 24 V
2. **Order bare boards**, five of them, and hand-solder one channel plus the input protection.
   Confirms the *layout* — footprints, orientations, the barrier
3. **Then order the assembled run**, with confidence that neither the circuit nor the board is
   going to need a respin

Skipping to step 3 risks paying assembly charges on a board with a footprint error, and the
parts are not recoverable once placed.

## Step 4 — the vendors you mentioned

**PCBWay** — bare boards are their strength and the price is hard to beat. Upload the zip,
their site parses it and shows a render of each layer. **Look at that render.** It is your
last chance to catch something like a missing layer or a board outline that did not export.
They also run a free DFM check that flags manufacturing problems, which is worth waiting for.

**MacroFab** — oriented around turnkey assembly rather than bare boards. Their value is
handling sourcing and placement for you, which is the expensive path but removes the soldering
entirely. Better suited to a second run once the design is proven than to a first one.

Both accept the same Gerber + drill zip.

## Ordering options, in plain terms

The web forms ask a lot of questions. For this board the answers are unremarkable:

| Option | Answer | Why |
|---|---|---|
| Layers | 2 | What it is designed for |
| Dimensions | 150 × 115 mm | Board outline |
| Quantity | 5 | Usually the minimum anyway, and you want spares |
| Thickness | 1.6 mm | Standard |
| Copper weight | 1 oz | Standard, plenty here |
| Surface finish | HASL (lead-free) or ENIG | HASL is cheaper; ENIG is flatter and easier to hand-solder fine pitch |
| Solder mask / silkscreen | any colour | No effect on function |
| Castellated / impedance control | no | Not applicable |

## What to check before you pay

- The vendor's layer render matches what you expect — particularly that the board outline is
  there and the two ground pours are separate
- The DFM report came back clean
- **Five boards, not fifty.** The most likely fault in a first board is a footprint that does
  not match the part you actually bought, and that is cheap to discover and expensive to
  discover in bulk

## When the boards arrive

Populate **one channel only** — its opto, MOSFET, Zener, four resistors and LED, plus the
whole input protection section. Power it from 5 V, drive the Pico pin, confirm the output
switches and the LED lights. Then repeat at 24 V.

Only when that works should you populate the other seven. That single habit is what turns a
board error from eight desoldering jobs into one.
