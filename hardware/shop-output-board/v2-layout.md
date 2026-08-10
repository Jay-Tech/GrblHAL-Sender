# Shop Output Board v2 — layout plan

Four channels, in a rearranged channel block.
**68 × 92 mm against v1's 150 × 115 — a 64% area reduction.**

Drawn here with a soldered thumb-sized RP2040 module, but **that choice is worth ~4 mm and
nothing in width** — keeping the socketed Pico gives you 68 × 96 mm. The size comes from the
channel count and the layout, not the MCU. See [Where the size actually goes](#where-the-size-actually-goes).

This is a plan, not a board. Nothing here is generated yet, and v1 remains the design that is
about to be fabbed. When this gets built it should get its own directory rather than
overwriting v1, because v1 is the one with a bench-test result attached to it (or will be).

**The channel circuit is unchanged.** Same opto, same gate clamp, same values. That is
deliberate: it means [bench-test.md](bench-test.md) validates v2 as well, and the one part of
the design nobody has watched work does not get disturbed by a packaging exercise.

## Where the size actually goes

Two changes do essentially all of the work, and **the module is not one of them.** That is
worth stating plainly, because it is the opposite of the intuition that started this.

| Change | Saves width | Saves height |
|---|---|---|
| 8 → 4 channels | **~82 mm** | nothing |
| Channel column → two sub-columns | nothing | **~16 mm** |
| Pico → thumb module | **nothing** | ~4 mm |

At four channels the channel bank is **51.4 mm** wide (3 × 14 mm pitch, plus parts reaching
±4.7 mm). A Pico's pad span is **48.3 mm**. The Pico therefore *already fits inside the width
the channels demand* — swapping it for a 15.2 mm module frees space that nothing else was
asking for.

Height is nearly as close. A Pico's pads sit at y = 7.00–24.78, so the next row can start at
24.78. A thumb module's body ends at 21.0. **The module buys 3.78 mm.**

So the honest summary: v2 is small because it has four channels and a rearranged channel
block. The module is a packaging choice, not a size one — see below for what it *is* worth.

## The channel, rearranged

v1 stacks all seven isolated-side parts in one vertical column 35 mm tall, in schematic
reading order. That is lovely to read and wasteful: the lane is 16 mm wide and the parts are
2 mm wide, so 85% of the width is air.

v2 splits each channel into two sub-columns 7 mm apart, grouped by **net** rather than by
schematic order:

```
                    channel centre x = cx
                  cx-3.5            cx+3.5
  y=41.19            ▀▀▀▀ opto pins 3,4 ▀▀▀▀        (on centreline)
  y=44      ══════════ V+ rail ══════════
  y=47             R2                  Q            R2→opto above; Q source→V+ above
  y=51             D                   R3
  y=55             R1                  R4
  y=59                                 LED
```

**Left column is the gate network, right column is the output network.** Every part in the
left column touches `GATE`; every part in the right column touches `OUT`. The two meet only
at Q, which is why Q sits at the top of the right column — its gate reaches left, its drain
runs down the right, its source goes straight up to the rail.

That ordering is not cosmetic. It puts R2 directly beneath the optocoupler it connects to,
and it gives `OUT` an unobstructed vertical run down the right-hand side to the terminal,
which is the longest and highest-current net in a channel.

Height, opto pads to the bottom of the LED: **19 mm, against v1's 35 mm.**

### The one crossing this creates

`OPTOCn` runs from opto pin 4 at y=41.19 down to R2 at y=47, and the `V+` rail sits at y=44
in between. Four channels means four crossings.

v1 has the same problem and solves it with vias. At four channels it is cheap either way —
eight vias, or segment the rail and jog it. **Do not solve it by moving the V+ rail below the
channel parts**: R1, D and Q's source all tap the rail, and moving it down turns one crossing
into three long vertical runs per channel.

## Board plan

```
  y=0    ┌──────────────────────────────────────┐
         │  ○          ╔══════════╗          ○  │  M3 corners; USB-C overhangs here
         │             ║  RP2040  ║             │  module body y=0..21
  y=21   │             ║  module  ║             │  pad rows x=25.11 / 42.89
         │             ╚══════════╝             │  7 pads each, 2.54 pitch
  y=27   │   R501   R502   R503   R504          │  470R, one per channel
  y=31.8 │   ▄▄      ▄▄      ▄▄      ▄▄         │  opto pins 1,2   (LOGIC)
  y=34   ╞══════════════════════════════════════╡ ← barrier starts
         ║       N O   C O P P E R   —  5 mm    ║   optos centred y=36.5
  y=39   ╞══════════════════════════════════════╡ ← barrier ends
  y=41.2 │   ▀▀      ▀▀      ▀▀      ▀▀         │  opto pins 3,4   (ISOLATED)
  y=44   │  ════════════ V+ rail ═══════════    │
         │   R2 Q    R2 Q    R2 Q    R2 Q       │  4 channels, two sub-columns each
         │   D  R3   D  R3   D  R3   D  R3      │
         │   R1 R4   R1 R4   R1 R4   R1 R4      │
  y=59   │      LED     LED     LED     LED     │  indicator bank, one row
  y=66   │   R6      D9                         │  Q9 gate satellites
  y=72   │   F1   Q9   TVS1   C1   C2           │  input protection chain
  y=84   │  [J1]        [J2  OUT 1-4 + GND]     │  terminals, bottom edge
  y=92   │  ○                                ○  │
         └──────────────────────────────────────┘
         x=0                                  x=68
```

**Channels on a 14 mm pitch starting at x = 13**: 13, 27, 41, 55. Centre of the channel bank
lands on x = 34, the board centreline, which is also where the module sits.

Sub-columns at **cx ± 3.5 mm**. Parts reach cx ± 4.7, leaving 4.6 mm between adjacent
channels for the `OUT` trace and its clearance.

### Vertical budget

| Band | y | Height | Scales with channels? |
|---|---|---|---|
| Module | 0–21 | 21 | no |
| R5 row + opto logic pads | 21–34 | 13 | no |
| Barrier | 34–39 | 5 | no |
| Channel parts | 39–60 | 21 | no |
| Input protection | 60–78 | 18 | no |
| Terminals | 78–92 | 14 | no |

Nothing in the height budget scales with channel count — which is the whole reason 2 channels
would barely beat 4. Width is the only axis channels move.

## Input protection, compressed

Same topology, same part *types*, tighter placement and two parts that can shrink now that
they feed four channels instead of eight.

| Part | x | y | Change from v1 |
|---|---|---|---|
| J1 | 10 | 84 | — |
| F1 | 14 | 72 | 1812, unchanged — see below |
| Q9 | 24 | 72 | — |
| R6 | 20 | 66 | — |
| D9 | 30 | 66 | — |
| TVS1 | 34 | 72 | — |
| C1 | 46 | 72 | **47 µF, 6.3 × 7.7 mm** — was 100 µF, 8 × 10.5 mm |
| C2 | 56 | 72 | — |
| J2 | 45 | 84 | Single 6-way: 4 × OUT + 2 × GND. **J3 deleted** |

**Keep F1 at 1.1 A / 24 V (Bourns MF-MSMF110/24X-2, 1812) even though four channels need
less.** Two reasons. The part is already researched, sourced and paid for in BOM-line terms,
and — more to the point — the 24 V rating is the constraint that forced 1812 in the first
place, and that does not relax with channel count. Dropping to a lower hold current buys a
smaller body only if you also drop below 24 V, which defeats the range. See
[design.md](design.md) for the vendor tables.

C1 is a genuine saving: bulk sized for coil inrush halves with the channel count, and the
package goes from 8 × 10.5 mm to 6.3 × 7.7 mm.

## Module: the open decision

**Keeping the Pico is a legitimate answer**, and given the numbers above it may be the right
one. It costs 3.78 mm of height and two BOM lines. It buys: no new footprint, no pin-map
change, no firmware change, and a module you can pull and replace with a fingernail. v1's
two-socket-strip approach is already proven on a routed, DRC-clean board.

What a soldered thumb module actually buys, none of which is size:

- **No through-hole parts except the terminals.** The two socket strips are the only
  through-hole parts on v1 besides connectors; deleting them makes the board pure SMT plus
  three screw terminals
- **Lower profile.** A socketed Pico stands ~8 mm proud on its socket; a soldered module is
  flat. If this ends up in a shallow enclosure that matters
- **Two fewer BOM lines** and one less assembly step

And what it costs: a footprint you have to verify yourself, a new pin map, and a module that
needs hot air to remove if it dies.

If you want the small board, take it from the channel count. Treat the module as a separate
question decided on assembly and enclosure, not on millimetres.

The layout above is drawn for a **XIAO form factor** — 21 × 17.8 mm, 7 castellated pads per
side, 2.54 mm pitch, **17.78 mm row spacing**. Three boards share it closely enough that the
footprint is the same:

| Module | Size | MCU | Sourcing |
|---|---|---|---|
| **Seeed XIAO RP2040** | 21 × 17.8 | RP2040 | Widest distribution — DigiKey, Mouser, Seeed |
| Waveshare RP2040-Zero | 23.5 × 18 | RP2040 | Very cheap, widely stocked |
| Pimoroni Tiny 2040 | 22.9 × 18.2 | RP2040 | Single vendor |
| ~~Pimoroni Tiny 2350~~ | 22.9 × 18 | RP2350 | **See the erratum note below** |

**Recommendation: XIAO RP2040**, on sourcing. This is a board you may want to rebuild in
three years, and a single-vendor module is the part most likely to be gone by then.

### Why not RP2350

RP2350 carries erratum **E9**: a GPIO in *input* mode sources up to ~0.1 mA and latches near
2.1 V. It is a fault in the pad's analogue circuitry, and Raspberry Pi addressed it with
documentation rather than an SDK fix.

That collides with two commitments this design has already made:

- [pico-gpio-protocol.md](../../docs/pico-gpio-protocol.md) requires that a watchdog trip
  drives outputs inactive **and releases the pins** — releasing to input mode is precisely
  the E9 condition
- R1 is 100 kΩ, so only **82 µA** of optocoupler collector current pulls the gate to the
  zener clamp and turns a channel on at 24 V

E9's ~100 µA and this circuit's 82 µA trip point are the same order of magnitude. In practice
the opto LED should clamp the pad near 1.0 V — below the 1.3–2.4 V band where E9 leaks
meaningfully — so it likely self-limits. But "likely" is carrying the fail-safe, and nothing
in this application needs an M33 at 150 MHz to toggle four pins. **RP2350 is a cost with no
matching benefit here.**

If you do want RP2350 later, the clean mitigation is lowering R1 to ~22 kΩ, which raises the
trip threshold to ~370 µA and puts comfortable daylight between the two numbers. That changes
the channel circuit, so it would need a fresh bench test — which is exactly what this v2 is
otherwise designed to avoid.

### Footprint: use two pad rows, not a module footprint

KiCad ships **no footprint for any RP2040 module**, the Pico included. v1 sidestepped that by
placing two generic `PinSocket_1x20_P2.54mm_Vertical` strips rather than a Pico footprint, and
the same trick applies: place two **`PinHeader_1x07_P2.54mm_Vertical_SMD`** rows 17.78 mm
apart.

This matters more than it sounds. A wrong custom footprint is the single most likely fault on
a first board — your own [fabrication.md](fabrication.md) says so — and a footprint drawn from
a module's marketing dimensions is a prime candidate. Two stock pad rows on a pitch you can
measure with callipers is a much smaller risk surface.

**Verify the row spacing against the module you actually buy** before generating. 17.78 mm is
the XIAO convention and matches its 17.8 mm width, but confirm it on the part.

### Soldered down means USB-C must reach an edge

A socketed Pico sits ~8 mm above the board, so its USB-C clears everything and a cable can
come in over the edge. A soldered module sits flat, so **its USB-C is at PCB level and must
overhang a board edge or you cannot plug it in.**

Hence the module at top-centre with the connector overhanging y = 0. Rotating it to put the
pads along X — the v1 Pico's orientation — puts the USB-C in the middle of the board and makes
the design unusable. This is easy to get wrong and impossible to fix after fab.

## What changes in the generators

Roughly a day's work, most of it in one dict.

**`generate_schematic.py`**
- Add `N_CHANNELS = 4`; derive `CHANNEL_GP` from it rather than hardcoding eight entries
- MPN table: C1 → 47 µF, delete J3
- Delete J3 and its six nets
- *Only if the module changes:* replace `GP_TO_PICO_PIN` with the module's pad map (14 pads,
  not 40) and swap the two `PinSocket_1x20` footprints for two
  `PinHeader_1x07_P2.54mm_Vertical_SMD`. **Keeping the Pico makes this section empty** — which
  is most of the argument for keeping it

**`generate_pcb.py`**
- `BOARD_W, BOARD_H` → `68.0, 92.0`
- `BARRIER_TOP, BARRIER_BOT` → `34.0, 39.0`
- `VPLUS_Y` → `44.0`
- `CH_X0, CH_PITCH` → `13.0, 14.0`
- **`CY` becomes `(dx, y)` per part instead of `y` per part.** This is the real structural
  change — every consumer assumes channel parts share the centreline
- New `POWER` coordinates; module placement replaces `PICO_J4`/`PICO_J5`
- `RISER_X` moves into a gap between the new channel positions

**Everything downstream is parameterised already** and should follow for free:
`verify_netlist.py`, `verify_pcb.py`, `generate_channel_diagram.py` and
`generate_power_diagram.py` all read the constants above rather than duplicating them. The
channel diagram will want checking, since it draws a single column and now has two.

## Open before this gets built

- **Whether to change the MCU at all.** Worth 3.78 mm. Keeping the Pico costs one dimension
  and removes every other open question in this list
- If it changes: **which module**, and its row spacing measured on the actual part
- If it changes: soldered or socketed — soldered is assumed here, and drives the USB-C edge
  constraint
- Whether v1 passes its bench test. **v2 inherits the entire circuit**, so a v1 failure is a
  v2 failure, and there is no sense generating boards for an unproven channel

## Correction to an earlier estimate

An earlier version of this analysis claimed the module swap was the width win and channel
count only shrank one axis. That was wrong: at four channels the channel bank (51.4 mm) is
wider than a Pico's pad span (48.3 mm), so the Pico costs no width at all. The numbers in
[Where the size actually goes](#where-the-size-actually-goes) are the corrected ones.
