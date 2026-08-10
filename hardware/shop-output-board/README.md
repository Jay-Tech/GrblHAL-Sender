# Shop Output Board

> ### On the shelf as of 2026-08-06 — not cancelled, just unnecessary
>
> Commercial Pico relay boards do this job for around £22, in an enclosure, on a DIN rail.
> This board switches a 5–24 V *signal* that still has to drive external relay modules, so the
> honest comparison was always "custom PCB + 8 relay modules + a field PSU + a case" against
> "one bought board". See [commercial-alternatives.md](../commercial-alternatives.md) for the
> comparison and what to verify on a bought board.
>
> **Nothing here is wrong or unfinished.** The design is complete, DRC-clean and documented,
> and it stays the answer if a requirement appears that dry relay contacts cannot meet — a
> switched 5–24 V output, or genuine galvanic isolation with the field ground leaving the
> board. It has still never been on a bench.

8-channel opto-isolated high-side switch board, driven by a Raspberry Pi Pico over USB-C.
Takes a 5–24 V field supply and sources it to screw terminals, so shop wiring carries a
robust signal instead of 3.3 V logic. Built for the sender's shop outputs — dust collection,
lighting — on hosts that have no GPIO header of their own.

**Nothing here has been near a bench.** Read [design.md](design.md) before ordering anything.

## Where to start

| If you want to… | Read |
|---|---|
| Understand the circuit and why the values are what they are | [design.md](design.md) |
| See how one channel becomes copper | [channel-layout.svg](channel-layout.svg) |
| See how the input power section becomes copper | [power-layout.svg](power-layout.svg) |
| Route the board | [layout.md](layout.md) |
| **Test it on a breadboard — do this first** | [bench-test.md](bench-test.md) |
| Order it | [fabrication.md](fabrication.md) |
| Check connectivity without any CAD tool | [netlist.md](netlist.md) |
| Write or flash device firmware | [../../docs/pico-gpio-protocol.md](../../docs/pico-gpio-protocol.md) |

## State

| | |
|---|---|
| Schematic | generated, ERC clean (0 violations) |
| Netlist | 78 nets, 84 parts, all assertions pass |
| Footprints | assigned; pad-to-pin mapping checked |
| Board placement | generated, DRC clean |
| Routing | done by hand in Pcbnew — 348 segments, 25 vias |
| DRC | **0 violations, 0 unconnected** |
| Gerbers | exported, validated, tracked in `ShopOutput_Gerber/` |
| BOM | 84 parts, every line with an MPN; F1 settled as an 1812 24V part |
| Assembly files | `production/` — BOM grouped by value, positions exclude through-hole |
| **Bench verification** | **none — this is what is left** |

> **Part rotations in `production/positions.csv` are in KiCad's native convention and have
> not been translated for any assembler.** Eighteen parts — C1, Q1–Q9, U1–U8, all of them
> polarized — sit at a different angle under JLCPCB's convention than under KiCad's. Settle
> this with whoever does the placement *before* an assembled run. It does not affect bare
> boards, which is the next order anyway.

## Regenerating

Everything is generated, so a value change propagates rather than needing to be chased. Run
from this directory, in this order:

```
python generate_schematic.py
kicad-cli sch erc --output erc.rpt --severity-error shop-output-board.kicad_sch
kicad-cli sch export netlist --output netlist.net shop-output-board.kicad_sch
python verify_netlist.py

python generate_pcb.py
kicad-cli pcb export ipcd356 --output pads.d356 shop-output-board.kicad_pcb
python verify_pcb.py
kicad-cli pcb drc --refill-zones --output drc.rpt --severity-error shop-output-board.kicad_pcb
```

Diagrams: `python generate_channel_diagram.py`, `python generate_power_diagram.py`.
Assembly BOM: `python generate_production_bom.py`.

> **`verify_pcb.py` reports D9's pads swapped, and that is expected.** D9 was rotated 180° by
> hand during routing. Rotation preserves pin-to-net mapping, so the circuit is unaffected —
> what it means is that the board is now hand-owned and `generate_pcb.py`'s `PADS` no longer
> mirrors it. The barrier checks in the same script still hold, and DRC is the authority on
> connectivity.

> **PDF export is flaky when KiCad has the project open.** `kicad-cli sch export pdf` will
> report `Plotted to ...` and `Done.`, exit non-zero, and leave a **0-byte file**. Close KiCad
> and Pcbnew first, or export somewhere else and move it in:
>
> ```
> kicad-cli sch export pdf --output %TEMP%\sob.pdf shop-output-board.kicad_sch
> move %TEMP%\sob.pdf shop-output-board.pdf
> ```
>
> Always check the file size afterwards. SVG export does not have this problem.

> **`generate_pcb.py` writes placement only.** `--route` also emits traces, but that routing
> is incomplete — down from 191 DRC violations to 113, still with shorts and crossings. A
> half-routed board is worse to inherit than an unrouted one, so it is not the default.

## The two verifiers, and why they exist

`verify_netlist.py` asserts the connectivity in [netlist.md](netlist.md) — every channel
individually, and that `PICO_GND` and `ISO_GND` share no node. ERC proves the schematic is
well formed; it says nothing about whether it is the circuit anyone meant.

`verify_pcb.py` compares pad positions against KiCad's own IPC-D-356 export rather than
against the generator's arithmetic. That distinction is not academic: the generator once
used the textbook rotation matrix, KiCad's is mirrored, and every rotated part landed on the
wrong side. It looked perfectly correct in the generator's own output. The optocouplers'
isolated pins were sitting in the logic zone until someone opened Pcbnew and read the pad
names.

## Before this becomes copper

Breadboard one channel and confirm it switches cleanly at 5 V and 24 V. Everything verified
so far is topology and geometry — nobody has yet watched the gate clamp work.

**[bench-test.md](bench-test.md) is the procedure**, with the expected voltage at every node
and what each failure mode looks like. The headline numbers: `Vgs` should be −4.8 V at 5 V and
−8.2 V at 24 V, and if it ever goes past −12 V the clamp is not working and the MOSFET is
being destroyed. One channel draws ~16 mA at 24 V; all eight draw ~126 mA.
