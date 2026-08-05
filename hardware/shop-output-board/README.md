# Shop Output Board

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
| Order it | [fabrication.md](fabrication.md) |
| Check connectivity without any CAD tool | [netlist.md](netlist.md) |
| Write or flash device firmware | [../../docs/pico-gpio-protocol.md](../../docs/pico-gpio-protocol.md) |

## State

| | |
|---|---|
| Schematic | generated, ERC clean (0 violations) |
| Netlist | 78 nets, 84 parts, all assertions pass |
| Footprints | assigned; pad-to-pin mapping checked |
| Board placement | generated, DRC clean (0 violations) |
| **Routing** | **not done — this is what is left** |
| Bench verification | none |

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
