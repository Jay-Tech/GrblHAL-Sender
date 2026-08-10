# Bench test — one channel, before this becomes copper

Everything verified so far is topology and geometry. ERC, DRC, the netlist assertions and the
pad checks all confirm the board matches the design. **None of them confirm the design
works.** This is the procedure that does.

It tests one channel on a breadboard. That is deliberate: the eight channels are identical,
so a fault in one is a fault in all eight, and finding it here costs an evening instead of a
board respin. The channel circuit is unchanged in any planned revision, so this procedure
stays valid for later board versions too.

## What this proves, and what it doesn't

**Proves:** that the gate clamp holds `Vgs` inside the AO3401A's ±12 V rating across the whole
5–24 V supply range; that the MOSFET fully enhances at both ends; that the optocoupler can
sink enough current to pull the gate down at 24 V; that the output defaults off.

**Does not prove:** footprint correctness (that needs real boards), thermal behaviour under a
real relay load for hours, or optocoupler CTR at end-of-life. Those come later.

## The one thing that will bite you

**Do not connect a mains-powered oscilloscope's ground clip to `ISO_GND`.**

The whole point of this circuit is that the logic ground and the field ground never touch.
Your scope's ground clip is bonded to mains earth, and so is your computer — the one the
Pico's USB is plugged into. Clip onto `ISO_GND` and you have just wired the two grounds
together through the bench, defeating the isolation you are trying to measure and quite
possibly reading nonsense or letting smoke out.

Use one of:

- **A floating DMM** (battery powered) for the isolated side. This is enough for every
  measurement in the table below — they are all DC
- **A differential probe**, or a battery-powered / isolated scope, if you want to see edges
- **A USB isolator** on the Pico, or run the Pico from a power bank with a pre-loaded script
  rather than a laptop

The measurements that matter here are all steady-state DC, so a floating DMM does the job.
You only need a scope if something fails and you want to see why.

## Parts

The board BOM is all SMD. For a breadboard you want through-hole, with one exception.

| Board part | Breadboard | Note |
|---|---|---|
| Q1 — AO3401A, SOT-23 | **AO3401A on a SOT-23→DIP breakout** | Do **not** substitute another P-MOSFET. The entire question being asked is whether the clamp keeps `Vgs` inside *this* part's ±12 V rating. A different MOSFET is a different experiment |
| U1 — LTV-817S, SOP-4 | LTV-817**B** / PC817**B**, DIP-4 | Same optocoupler family, through-hole package. The **B** matters — CTR rank drives the margin being tested |
| D1 — MMSZ5237B, SOD-123 | 1N4738A, DO-41 | 8.2 V zener, through-hole equivalent |
| R1 100k, R2 4k7, R3 10k, R4 2k2, R5 470R | any 1/4 W through-hole | Values matter, package doesn't |
| LED1 | high-efficiency green | Indicator only, but see the note on brightness below |

Orderable part numbers and quantities are in the shopping list below.

Plus: a bench supply that does 5 V and 24 V with a current limit, a Pico, and a floating DMM.

**Set the bench supply's current limit to about 100 mA.** One channel should draw ~16 mA at
24 V. A limit near 100 mA lets it run normally but turns a wiring mistake into a supply that
folds back rather than a part that vents.

### Shopping list

Orderable parts for **one channel**, with spares where the part is cheap and easy to cook.
Everything here is a commodity stocked by Digi-Key, Mouser, Farnell and the usual eBay/Amazon
sellers — no part on this list is hard to find.

| Qty | Part | MPN | Why this one |
|---|---|---|---|
| 5 | P-MOSFET, SOT-23 | **AO3401A** | **The part under test.** Do not substitute — the whole question is whether the clamp keeps `Vgs` inside *this* device's ±12 V rating |
| 2 | SOT-23 → DIP breakout board | generic "SOT-23-3 to DIP adapter" | The AO3401A has no through-hole version. Buy a strip of 10, they cost pennies |
| 2 | Optocoupler, DIP-4, **rank B** | **LTV-817B** (Lite-On) or **PC817B** (Sharp) | Through-hole sibling of the board's LTV-817S, same die. **The B suffix is the point** — CTR rank is what the 24 V case actually tests |
| 5 | Zener, 8.2 V, DO-41 | **1N4738A** | Through-hole equivalent of the MMSZ5237B. 1 W rather than 500 mW, which only helps on a breadboard |
| 5 each | Resistors, 1/4 W through-hole | 100 kΩ, 4.7 kΩ, 10 kΩ, 2.2 kΩ, 470 Ω | Values matter, package and tolerance do not. Carbon film is fine |
| 2 | LED, 3 mm or 5 mm, **high-efficiency green** | e.g. **Kingbright WP710A10LSGD** | At a 5 V supply this only draws ~1.4 mA. A dull LED will look like a failure when the circuit is fine |

**Buy the optocouplers rank-marked.** An unranked PC817 can be CTR 80%, and the design needs
≥ 75% — testing with a marginal part tells you nothing useful about a board that will be
built with rank C. If you can only get unranked, note it and treat a soft result at 24 V as
"suspect the opto" rather than "suspect the design".

### Worth adding for three more parts

The reverse-polarity protection on the board uses **the same clamp trick** as a channel — Q9
with D9 and R6 holding its gate 8.2 V below the source. If you are already breadboarding,
proving it costs one extra AO3401A, one extra 1N4738A and one extra 100 kΩ, all of which are
in the quantities above.

Wire it as `netlist.md` describes: **drain to the supply, source to the load**, gate through
R6 to ground with D9 clamping to the source. Then reverse the supply leads and confirm the
output stays dead. That is the other half of the input section nobody has watched work.

## Build

Two separate grounds. Keep them on opposite ends of the breadboard and never join them.

**Logic side** — powered only by the Pico's USB:

```
Pico GP2 ──[R5 470R]──► PC817 pin 1 (LED anode)
                        PC817 pin 2 (LED cathode) ──► Pico GND
```

**Isolated side** — powered only by the bench supply:

```
V+ (bench +) ─┬─────────────────────────┬──────────── Q1 source (pin 2)
              │                         │
            [R1 100k]              D1 cathode
              │                         │
              ├─────────────────────────┴──── GATE ── Q1 gate (pin 1)
              │                    (D1 anode to GATE)
            [R2 4k7]
              │
              └──► PC817 pin 4 (collector)
                   PC817 pin 3 (emitter) ──► ISO_GND (bench −)

Q1 drain (pin 3) ── OUT ─┬──[R3 10k]──► ISO_GND
                         └──[R4 2k2]──►|LED1|──► ISO_GND
```

Check before powering: **D1's cathode goes to V+, its anode to the gate.** Backwards, it
clamps nothing and shorts the gate — this is the single easiest thing to get wrong, and it is
on the review checklist in [netlist.md](netlist.md) for that reason.

## The measurements

Drive GP2 low for OFF and high for ON. All voltages measured **with respect to `ISO_GND`**
except `Vgs`, which is `V_GATE − V+` and is the number the whole design turns on.

### At V+ = 5 V

| Measurement | Expected | Why |
|---|---|---|
| `V_GATE`, OFF | 5.0 V | R1 holds the gate at the source — default off |
| `Vgs`, OFF | 0 V | Q1 off |
| `V_OUT`, OFF | 0 V | R3 pulls the terminal down, no floating output |
| `V_GATE`, ON | **≈0.22 V** | Zener never conducts at 5 V; R1/R2 divide: 5 × 4k7/104k7 |
| `Vgs`, ON | **≈−4.8 V** | Fully enhanced, well inside ±12 V |
| `V_OUT`, ON | ≈5.0 V | Rds(on) ~60 mΩ, drop is negligible |
| Current through R2, ON | ≈48 µA | Measure as mV across R2 ÷ 4700 |

### At V+ = 24 V

| Measurement | Expected | Why |
|---|---|---|
| `V_GATE`, OFF | 24.0 V | Same fail-safe |
| `V_OUT`, OFF | 0 V | |
| `V_GATE`, ON | **≈15.8 V** | **The clamp working.** 24 − 8.2 |
| `Vgs`, ON | **≈−8.2 V** | Inside ±12 V with 3.8 V to spare — this is the result the design exists for |
| `V_OUT`, ON | ≈24.0 V | |
| Current through R2, ON | **≈3.3 mA** | The optocoupler must sink this. It is ~70× the 5 V figure |

That last row is the real stress point and it is easy to overlook. At 5 V the opto sinks 48 µA
— trivial. At 24 V it must sink 3.3 mA against ~4.5 mA of LED drive, so it needs **CTR ≥ 75%**.
A PC817B (130–260%) has roughly 1.8× margin at minimum rank. If you have substituted an
unranked PC817 (rank A can be 80%), this is where it shows up.

## Pass / fail

**Pass** if `Vgs` at 24 V ON sits between −7.5 V and −9 V, `V_OUT` reaches within 0.2 V of V+
in both cases, and `V_OUT` is 0 V in both OFF cases.

**Stop immediately if `Vgs` is more negative than −12 V.** That means the zener is not
clamping — wrong part, wrong orientation, or open — and Q1 is being damaged as you watch.

Other failures and what they mean:

| Symptom | Likely cause |
|---|---|
| `V_GATE` at 24 V ON is well above 15.8 V (say 19–22 V) | Optocoupler cannot sink 3.3 mA — CTR too low, or R5 wrong so the LED is underdriven |
| `V_GATE` ≈ 0 V at 24 V ON, `Vgs` ≈ −24 V | D1 backwards or open. Kill power |
| `V_OUT` never reaches V+ | Q1 not fully enhanced — check `Vgs` first; if `Vgs` is right, suspect the part or the breakout wiring |
| `V_OUT` sits at some middle voltage when OFF | R3 missing, or Q1 source/drain swapped so the body diode conducts |
| Output on with the Pico unpowered | R1 open. This is the fail-safe and it must hold |

## Also worth checking while it is on the bench

**The fail-safe, explicitly.** With V+ at 24 V, unplug the Pico entirely. `V_OUT` must be 0 V
and stay there. Then plug it back in and watch that the output does not glitch high during
enumeration. This is the same property that was verified on the Pi header for
`grblhal-gpio-relays`, and it is worth confirming on this hardware path too.

**R2's temperature at 24 V.** It dissipates about 53 mW continuous. The BOM specifies
`RC0805FR-074K7L`, which is a 0.125 W part, while [design.md](design.md) describes R2 as
"4k7, 0805, 0.25W". 53 mW on a 125 mW part is 42% of rating — fine, but the doc and the part
number disagree and the discrepancy should be resolved rather than inherited. It should be
barely warm; if it is hot, something is drawing far more than intended.

**The off-delay behaviour**, if you have the sender driving it: an M5/M3 pair inside the delay
window should hold the output continuously rather than cycling it. That is listed as still
unverified in the GPIO relay work and this is a convenient place to see it.

## When it passes

Then, and only then, order boards — five, not fifty. Populate one channel plus the whole
input protection section, confirm it behaves the same as the breadboard, and only then
populate the other seven. See [fabrication.md](fabrication.md) for the cost sequencing.
