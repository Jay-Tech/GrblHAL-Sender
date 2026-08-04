# Getting the board made

Written for someone doing this for the first time. Read the "where you are" section before
uploading anything anywhere.

## Where you are

| Step | Status |
|---|---|
| Schematic | done, ERC clean, netlist verified |
| Footprints assigned | done, checked |
| Layout plan and DRC rules | done |
| Board placed | done — 84 parts positioned, DRC clean, 0 violations |
| **Board routed** | **not started — this is the blocker** |
| Gerbers | cannot exist until routing is done |

A manufacturer needs a *routed* board: actual copper traces joining the pads. The board is
placed — every part is where it should be and the ratsnest is correct — but the traces are
not drawn. Nothing to order yet.

Also, before any of this: **breadboard one channel.** The whole design rests on a gate clamp
nobody has watched work. Finding out it needs a different Zener costs an evening on a
breadboard and a board respin if you skip it.

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
kicad-cli pcb export gerbers --output fab/ shop-output-board.kicad_pcb
kicad-cli pcb export drill --output fab/ --format excellon --excellon-separate-th shop-output-board.kicad_pcb
```

That is everything needed for a **bare board**. Zip the contents of `fab/` and upload the zip.

For **SMT assembly**, two more files. Note `--exclude-fp-th`: it leaves the connectors out
of the position file, so the machine places only the parts you are paying it to place.

```
kicad-cli pcb export pos --output fab/positions.csv --format csv --units mm --side both --exclude-fp-th shop-output-board.kicad_pcb
kicad-cli sch export bom --output fab/bom.csv --fields "Reference,Value,Footprint" --group-by "Value" shop-output-board.kicad_sch
```

The position file tells the machine where each part goes; the BOM says what each part is.
Both need real manufacturer part numbers added to the BOM — "100k, 0805" is not orderable, a
specific part number is.

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
- **Awareness that unique part types drive the price.** There is a setup charge per distinct
  component regardless of how many are placed, so this board's twelve types cost about the
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
