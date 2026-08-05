# Shop Output Board v2

Four-channel opto-isolated high-side switch board. **70 × 92 mm against v1's 150 × 115 —
64% less area**, same circuit per channel.

**Generated and placed, not routed.** DRC is clean on the placement; the routing is the work
left to do, exactly as v1 was at this stage.

## What changed from v1, and what did not

**Unchanged: the entire channel circuit.** Same optocoupler, same AO3401A, same 8.2 V gate
clamp, same resistor values. That is the point — [v1's bench-test procedure](../shop-output-board/bench-test.md)
validates this board too, and the one part of the design nobody has watched work does not get
disturbed by a packaging exercise.

| | v1 | v2 |
|---|---|---|
| Channels | 8 | 4 |
| Board | 150 × 115 mm | **70 × 92 mm** |
| Parts | 84 | 47 |
| Output terminals | two 6-way | one 6-way (4 out + 2 gnd) |
| Bulk cap C1 | 100 µF, 8 × 10.5 mm | 47 µF, 6.3 × 7.7 mm |
| MCU | socketed Pico | socketed Pico — *unchanged* |
| F1 | 1812, 1.1 A / 24 V | same |

**The Pico stays.** Swapping it for a thumb-sized RP2040 module was the original motivation
for v2 and it turned out to be worth almost nothing: at four channels the channel bank is
51.4 mm wide and a Pico's pad span is 48.3 mm, so the Pico already fits inside the width the
channels demand. On height it buys 3.78 mm. See [v2-layout.md](../shop-output-board/v2-layout.md)
for the full working. Keeping it means no new footprint, no pin map, no firmware change.

## The channel, rearranged

This is where the height saving comes from. v1 stacks all seven isolated-side parts in one
column 35 mm tall; v2 splits them into two sub-columns grouped by **net**:

```
                 cx-4              cx+4
  y=41.19          ▀▀▀ opto pins 3,4 ▀▀▀      (centreline)
  y=44      ═══════════ V+ rail ═══════════
  y=47             Q                 R2       Q source → rail; R2 → opto above
  y=51             R3                D
  y=55             R4                R1
  y=59             LED
```

**Left column is everything touching `OUT`, right column is everything touching `GATE`.**
They meet only at Q, which is why Q sits at the top of the left column — its source reaches
the rail in 2 mm, its gate faces right toward the gate column, and its drain has a clear run
straight down its own column to the terminal. R2 sits opposite it, directly under the
optocoupler it feeds.

Channel height: **19 mm, against v1's 35 mm.**

## Regenerating

```
python generate_schematic.py
kicad-cli sch erc --output erc.rpt --severity-error shop-output-board-v2.kicad_sch
kicad-cli sch export netlist --output netlist.net shop-output-board-v2.kicad_sch
python verify_netlist.py

python generate_pcb.py
kicad-cli pcb export ipcd356 --output pads.d356 shop-output-board-v2.kicad_pcb
python verify_pcb.py
kicad-cli pcb drc --refill-zones --output drc.rpt --severity-error shop-output-board-v2.kicad_pcb
```

Channel count comes from `CHANNEL_GP` in `generate_schematic.py` — shortening that list is
the whole change. Everything else derives from it.

`generate_pcb.py` has **no `--route` flag**. v1 carried one that never got past 113 DRC
violations; a half-routed board is worse to inherit than an unrouted one, so it is not
carried forward.

## State

| | |
|---|---|
| Schematic | generated, ERC clean (0 violations) |
| Netlist | 58 nets, 47 parts, all assertions pass |
| Placement | generated, 99 pads match KiCad's own coordinates to 0.05 mm |
| Barrier | 16 opto pads correct side; **0 traces, 0 vias, 0 zone vertices** in y=34–39 |
| Routing | **done by hand** — 198 segments, 11 vias, 0 unconnected |
| Mounting | 3 × Ø3.2 mm NPTH at (4,4), (4,88), (66,4), each with a Ø7.6 copper keepout |
| DRC | 15 violations, **all `output net spacing`** — see below |
| Bench verification | none — inherited from v1, which has not been tested either |

## Mounting holes and the isolation barrier

The holes straddle both domains: (4,4) and (66,4) sit in the `PICO_GND` pour, (4,88) in
`ISO_GND`. Mounted on a metal panel with metal standoffs, the chassis ties all three together
— so if any screw head reaches the pour it is sitting on, **the two ground domains are
bridged and the isolation this board exists for is gone.**

As first routed, copper came within 0.34–0.50 mm of the hole edges, which every standard M3
head overlaps:

| Hardware | Head reach | Was | Now |
|---|---|---|---|
| M3 socket cap Ø5.5 | 2.75 mm | contact | clears 1.04 mm |
| M3 pan head Ø5.6 | 2.80 mm | contact | clears 0.99 mm |
| M3 countersunk Ø6.0 | 3.00 mm | contact | clears 0.79 mm |
| M3 + flat washer Ø7.0 | 3.50 mm | contact | clears 0.29 mm |

Fixed with a **Ø7.6 mm copper keepout** (48-gon, R3.8) on both layers at each hole, blocking
pour, tracks and vias. Copper now sits 3.79 mm from each hole centre, measured on a
zone-refilled Gerber plot rather than the stored fill.

**DRC cannot catch this class of fault** — it has no concept of a screw head. If you move a
mounting hole, re-check it by hand.

The holes were also Ø3.00, which is zero clearance for an M3 (its major diameter *is* 3.0 mm,
so the screw will not pass). Now Ø3.2, the standard close fit.

## Net classes: set them, or DRC is lying to you

`shop-output-board-v2.kicad_pro` carries `Logic` and `Isolated` as `netclass_patterns`. The
custom rules in `shop-output-board-v2.kicad_dru` key on exactly those names.

Until they were set, **DRC reported 0 violations on a board with 19 of them**, because every
custom rule silently matched nothing. The barrier happened to be clean — verified by hand —
but nothing was checking it. Confirmed armed by injecting a deliberate barrier-crossing trace
into a scratch copy: caught as `rule 'isolation barrier' clearance 5.0000 mm; actual 0.1250 mm`.

If you ever edit net classes in Pcbnew, remember **File → Save Project**, not Ctrl+S.

## The 15 remaining violations

All are the `output net spacing` rule: 0.20 mm actual against the 0.40 mm the rule asks for,
across four pairs where the outputs fan into J2.

| Pair | Region |
|---|---|
| OUT1 ↔ OUT2 | x 19–60, y 63–78 |
| OUT1 ↔ V+ | x 4–50, y 54–63 |
| OUT2 ↔ OUT3 | x 35–60, y 54–78 |
| OUT3 ↔ OUT4 | x 52–61, y 55–71 |

This is a **margin** rule, not a correctness one — it exists to give the output nets room
against inductive kick from relay coils at the far end of the wiring. The board is
electrically fine at 0.20 mm. Either re-route that fan-in with 0.4 mm spacing (there is room:
0.5 mm tracks on a 3.81 mm terminal pitch need only 0.9 mm), or relax the rule deliberately
and write down why. Do not leave it failing silently.

## Before this goes anywhere

**v2 inherits v1's entire channel circuit, so a v1 bench failure is a v2 failure.** There is
no sense routing this, let alone fabbing it, until one channel has been proven on a
breadboard. That procedure is [bench-test.md](../shop-output-board/bench-test.md).

The indicator LED polarity bug found in v1 on 2026-08-05 is fixed here from the start — the
node from R4 lands on pin 2 (anode), and `verify_netlist.py` asserts it in that direction.
