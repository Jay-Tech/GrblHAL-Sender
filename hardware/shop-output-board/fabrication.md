# Getting the board made

Written for someone doing this for the first time. Read the "where you are" section before
uploading anything anywhere.

## Where you are

| Step | Status |
|---|---|
| Schematic | done, ERC clean, netlist verified |
| Footprints assigned | done, checked |
| Layout plan and DRC rules | done |
| **Board routed** | **not started — this is the blocker** |
| Gerbers | cannot exist until routing is done |

A manufacturer needs a *routed* board: actual copper traces joining the pads. Right now there
is a schematic that says what should connect and a plan for where things go. Nothing to order
yet.

Also, before any of this: **breadboard one channel.** The whole design rests on a gate clamp
nobody has watched work. Finding out it needs a different Zener costs an evening on a
breadboard and a board respin if you skip it.

## Step 1 — route the board in KiCad

Open the project, then in Pcbnew:

1. **Update PCB from Schematic** (F8). All 84 footprints appear in a heap beside the board
   area, with thin "ratsnest" lines showing what must connect.
2. **Draw the board outline** on the `Edge.Cuts` layer — a 150 × 115 mm rectangle.
3. **Set net classes** (Board Setup → Net Classes) as listed in [layout.md](layout.md), then
   confirm Board Setup → Design Rules → Custom Rules has picked up the `.kicad_dru` file.
4. **Place the parts** following the plan. Terminals first, then the Pico socket, then the
   eight optos on the barrier centreline.
5. **Route.** Interactive router, default hotkey `X`. Follow the ratsnest.
6. **Add the two ground pours**, one per domain, neither entering the barrier.
7. **DRC** (Inspect → Design Rules Checker) until it is clean. The custom rule means a trace
   bridging the two domains shows up here as an error rather than as a surprise later.

This is real work — expect a few evenings for a first board, and that is normal.

## Step 2 — export

From this directory, once `shop-output-board.kicad_pcb` exists and DRC is clean:

```
kicad-cli pcb export gerbers --output fab/ shop-output-board.kicad_pcb
kicad-cli pcb export drill --output fab/ --format excellon --excellon-separate-th shop-output-board.kicad_pcb
```

That is everything needed for a **bare board**. Zip the contents of `fab/` and upload the zip.

For **assembly**, two more files:

```
kicad-cli pcb export pos --output fab/positions.csv --format csv --units mm --side both shop-output-board.kicad_pcb
kicad-cli sch export bom --output fab/bom.csv --fields "Reference,Value,Footprint" --group-by "Value" shop-output-board.kicad_sch
```

The position file tells the machine where each part goes; the BOM says what each part is.
Both need real manufacturer part numbers added to the BOM — "100k, 0805" is not orderable, a
specific part number is.

## Step 3 — bare board or assembled?

This is the decision that matters most, and it is worth understanding before you pick a vendor.

**Bare board** — they make the PCB, you solder the parts. Roughly $5–30 for five boards plus
shipping. Needs only Gerbers and drill files.

**Assembled (PCBA)** — they source and place everything. Hundreds of dollars for a small run,
because there is a setup cost per part type regardless of quantity, and this board has twelve.

For a first board I would order **bare** and hand-solder, for two reasons beyond cost. You can
populate one channel, test it, and only then do the other seven — so a design mistake costs
one channel instead of eight. And when something does not work, a board you soldered yourself
is one you can probe and rework.

The parts are hand-solderable: 0805 is comfortable with a fine tip and flux, SOT-23 and
SOD-123 are small but fine, and the optos and terminals are through-hole. 41 resistors is
tedious, not difficult.

Order the parts separately from Digi-Key, Mouser or LCSC using the BOM.

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
