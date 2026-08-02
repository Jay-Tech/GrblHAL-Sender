# Shop Output Board — netlist

Complete connectivity for the design in [design.md](design.md). This is the reviewable
artifact: it is the whole circuit, independent of any CAD tool, and it is what gets entered
into KiCad.

8 channels, opto-isolated high-side switches, 5–24V output domain, USB-C via a socketed Pico.

## Nets

Two ground nets that **must never connect**:

| Net | Domain | Notes |
|---|---|---|
| `PICO_GND` | Logic | Pico ground, referenced to the host's USB |
| `ISO_GND` | Output | Field ground, out to the shop |

Everything else:

| Net | Notes |
|---|---|
| `VIN_RAW` | Straight off the input terminal, before protection |
| `VIN_F` | After the polyfuse, before reverse-polarity |
| `V+` | Protected rail. Source for all 8 channels |
| `GATE1`..`GATE8` | Per-channel MOSFET gate |
| `OPTOC1`..`OPTOC8` | Per-channel opto collector |
| `OUT1`..`OUT8` | Per-channel output, to terminal |
| `GP2`..`GP9` | Pico logic, one per channel |

## Input protection

| Ref | Part | Pin | Net |
|---|---|---|---|
| J1 | Screw terminal 2-way 5.08mm | 1 | `VIN_RAW` |
| | | 2 | `ISO_GND` |
| F1 | Polyfuse 1.1A hold | 1 | `VIN_RAW` |
| | | 2 | `VIN_F` |
| Q9 | P-MOSFET, −30V, ≥3A | D (drain) | `VIN_F` |
| | | S (source) | `V+` |
| | | G (gate) | `Q9GATE` |
| R6 | 100k | 1 | `Q9GATE` |
| | | 2 | `ISO_GND` |
| D9 | BZX84C8V2 | cathode | `V+` |
| | | anode | `Q9GATE` |
| TVS1 | SMAJ30A | 1 | `V+` |
| | | 2 | `ISO_GND` |
| C1 | 100µF / 50V | + | `V+` |
| | | − | `ISO_GND` |
| C2 | 100nF | 1 | `V+` |
| | | 2 | `ISO_GND` |

> **Q9 needs its own gate clamp, and this is easy to miss.** A reverse-polarity P-MOSFET is
> normally drawn with the gate tied straight to ground, which puts `Vgs = −V+` on it. At 24V
> that exceeds the ±12V rating and destroys the part on the bench, not in the field. D9 and R6
> are the same clamp trick used per channel — R6 pulls the gate down to turn it on, D9 stops
> `Vgs` going past 8.2V.

Orientation matters: **drain to the supply, source to the load.** With correct polarity the
body diode conducts first, then the gate pulls low and the channel shorts around it. Reversed,
`Vgs` goes positive, the channel stays off and the body diode blocks. Swapping D and S gives a
part that conducts in both directions and protects nothing.

## Per channel — n = 1..8

Channel *n* uses Pico `GP(n+1)`, so channel 1 is GP2 and channel 8 is GP9. GP0/GP1 are left
free for a serial console, and GP23/24/25/29 are reserved by the Pico itself.

### Logic side

| Ref | Part | Pin | Net |
|---|---|---|---|
| R5n | 470R | 1 | `GP(n+1)` |
| | | 2 | `U_n` pin 1 |
| U_n | PC817B / EL357N-C | 1 (LED anode) | R5n pin 2 |
| | | 2 (LED cathode) | `PICO_GND` |

### Isolated side

| Ref | Part | Pin | Net |
|---|---|---|---|
| U_n | PC817B / EL357N-C | 4 (collector) | `OPTOCn` |
| | | 3 (emitter) | `ISO_GND` |
| R2n | 4k7 0.25W | 1 | `GATEn` |
| | | 2 | `OPTOCn` |
| R1n | 100k | 1 | `V+` |
| | | 2 | `GATEn` |
| Dn | BZX84C8V2 | cathode | `V+` |
| | | anode | `GATEn` |
| Qn | AO3401A | S (source) | `V+` |
| | | G (gate) | `GATEn` |
| | | D (drain) | `OUTn` |
| R3n | 10k | 1 | `OUTn` |
| | | 2 | `ISO_GND` |
| R4n | 2k2 | 1 | `OUTn` |
| | | 2 | LEDn anode |
| LEDn | High-efficiency, 0805 | anode | R4n pin 2 |
| | | cathode | `ISO_GND` |

## Output terminals

| Ref | Part | Pin | Net |
|---|---|---|---|
| J2 | Screw terminal 6-way 3.81mm | 1 | `OUT1` |
| | | 2 | `OUT2` |
| | | 3 | `OUT3` |
| | | 4 | `OUT4` |
| | | 5 | `ISO_GND` |
| | | 6 | `ISO_GND` |
| J3 | Screw terminal 6-way 3.81mm | 1 | `OUT5` |
| | | 2 | `OUT6` |
| | | 3 | `OUT7` |
| | | 4 | `OUT8` |
| | | 5 | `ISO_GND` |
| | | 6 | `ISO_GND` |

## Pico socket

Two 20-way 2.54mm sockets, 0.1" rows on a 21.0mm span. Only these pins connect:

| Pico pin | Signal | Net |
|---|---|---|
| 3 | GND | `PICO_GND` |
| 4 | GP2 | `GP2` |
| 5 | GP3 | `GP3` |
| 6 | GP4 | `GP4` |
| 7 | GP5 | `GP5` |
| 9 | GP6 | `GP6` |
| 10 | GP7 | `GP7` |
| 11 | GP8 | `GP8` |
| 12 | GP9 | `GP9` |
| 8, 13, 18, 23, 28, 33, 38 | GND | `PICO_GND` |

Nothing on the isolated side is powered from the Pico, and the Pico is powered only from its
own USB-C. `VSYS` and `3V3_EN` are left unconnected — do not be tempted to feed the Pico from
`V+`, since that would bridge the two domains and defeat the isolation.

## Review checklist

Worth checking against this before it goes to layout:

- [ ] `PICO_GND` and `ISO_GND` appear in no rule together, and no component bridges them except U1–U8
- [ ] Every Zener is cathode-to-`V+`, anode-to-gate — reversed, it clamps nothing and shorts the gate
- [ ] Q9 drain to `VIN_F`, source to `V+` — not the other way round
- [ ] Qn source to `V+`, drain to `OUTn` — the channels are the opposite orientation to Q9,
      which is correct but looks wrong at a glance
- [ ] Opto pin 3 is the emitter and pin 4 the collector on the chosen part; verify against the
      datasheet for the exact package, as this differs between manufacturers
