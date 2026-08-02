# Shop Output Board — design notes

An 8-channel opto-isolated high-side switch board driven by a Raspberry Pi Pico over USB-C,
for the sender's shop outputs. Replaces bare 3.3V logic with a robust 5–24V signal that can
run down a shop wall to relay modules and SSRs.

**Read the verification note at the end before ordering anything.**

## What it does

```
USB-C ──► Pico ──► 3.3V logic ──╫── opto barrier ──╫── 5-24V domain ──► screw terminals
                                ╫                  ╫
                        (isolated grounds — the whole point)
```

The isolation is not decorative. The Pico's ground stays with the host computer; the output
domain's ground goes out to the shop. Tying them together would make the optos into mere
level shifters and hand you a ground loop between the host and whatever the relays are bolted
to. **The two ground nets must not touch anywhere on the board.**

## Per-channel circuit

Repeated eight times.

```
                    V+ (5-24V)
                        │
        ┌───────────────┼───────────────┐
        │               │               │
       [R1]           source            │
      100k              │               │
        │          ┌────┴────┐          │
        ├──────────┤ Q1      │          │
        │  gate    │ AO3401A │          │
       [D1]        │  P-MOS  │          │
      8V2 zener    └────┬────┘          │
        │             drain             │
        │               │               │
        │               ├──────────────────► OUT n  (screw terminal)
        │               │               │
        │              [R3]           [LED]
        │              10k            [R4] 2k2
        │               │               │
       [R2]             │               │
      4k7               │               │
        │               │               │
     ┌──┴──┐            │               │
     │opto │            │               │
     │ out │            │               │
     └──┬──┘            │               │
        │               │               │
   ISO_GND ─────────────┴───────────────┴──────► GND (screw terminal)


   Pico GPn ──[R5 470R]──► opto LED ──► Pico GND
```

### Why these values

**Q1 gate drive is the only subtle part.** A P-MOSFET high-side switch needs its gate pulled
below the source to turn on, but `Vgs` must stay inside the device rating. Pulling the gate
straight to ground works at 5V and destroys the part at 24V — the AO3401A is rated ±12V.

D1 (8.2V Zener) clamps it:

| V+ | Gate node | Vgs | State |
|---|---|---|---|
| 5V | ≈0.2V | −4.8V | Fully enhanced (Rds(on) ≈ 60 mΩ) |
| 12V | 3.8V | −8.2V | Clamped |
| 24V | 15.8V | −8.2V | Clamped, well inside the ±12V rating |

So one circuit covers the whole 5–24V range with no jumpers. At 5V the Zener never conducts
and the divider does the work; above ~9V the Zener takes over.

**R1 (100k)** holds the gate at V+ when the opto is dark, so the default state is off — the
same fail-safe reasoning as choosing active-high relay boards for the Pi header.

**R3 (10k)** pulls the output to ground when the channel is off, so the terminal presents a
defined low rather than floating. A floating input on whatever you have plugged in is exactly
the noise-susceptibility you are trying to get away from.

**R5 (470R)** sets about 4.5 mA through the opto LED from 3.3V. With a CTR-ranked opto
(PC817B or EL357N-C, 100%+) that gives at least 4.5 mA of collector current against the
~2.5 mA R2 needs at 24V — comfortable margin.

> **Aggregate GPIO current is worth watching.** Eight channels at 4.5 mA is 36 mA drawn from
> RP2040 pins with everything on. That is within what the chip and the Pico's 3V3 regulator
> will do, but it is not nothing. Do not raise the LED current without rechecking it.

**LED + R4 (2k2)** sits on the *output* side, across the terminal. It shows what the terminal
is actually doing rather than what the Pico intended — if a MOSFET fails, the LED tells the
truth. Brightness varies with supply (≈1.4 mA at 5V, ≈10 mA at 24V), so use a high-efficiency
LED so it is visible at the bottom of the range.

## Input protection

On the V+ terminal, in order:

| Part | Purpose |
|---|---|
| Polyfuse, 1.1A hold | Limits a wiring fault to something that resets itself |
| P-MOSFET reverse-polarity block | Lossless, unlike a series Schottky — a 0.4V drop matters at 5V |
| TVS, SMAJ30A | Surge clamp. 30V standoff so a 24V supply with ripple does not sit on it |
| 100µF / 50V electrolytic + 100nF ceramic | Bulk and local decoupling for coil inrush |

## Bill of materials

Quantities for one board. Parts chosen for wide availability in assembly-house catalogues.

| Ref | Qty | Part | Notes |
|---|---|---|---|
| Q1–Q8 | 8 | AO3401A, SOT-23 | P-MOSFET, −30V, −4A, logic level |
| U1–U8 | 8 | PC817B or EL357N-C | Opto, CTR ranked ≥100% |
| D1–D8 | 8 | MMSZ5237B, SOD-123 | 8.2V Zener, gate clamp. Two-pin package so pins map to pads; a SOT-23 Zener has three. |
| R1 | 8 | 100k, 0805 | Gate pull-up |
| R2 | 8 | 4k7, 0805, 0.25W | Opto collector load |
| R3 | 8 | 10k, 0805 | Output pull-down |
| R4 | 8 | 2k2, 0805 | LED series |
| R5 | 8 | 470R, 0805 | Opto LED series |
| LED1–8 | 8 | High-efficiency, 0805 | Output state |
| Q9 | 1 | AO3401A, SOT-23 | Reverse polarity. Same part as the channels — 4A is ample for the whole board and it keeps one MOSFET line in the BOM. |
| F1 | 1 | Polyfuse 1.1A | |
| TVS1 | 1 | SMAJ30A | |
| C1 | 1 | 100µF / 50V electrolytic | |
| C2 | 1 | 100nF, 0805 | |
| J1 | 1 | 2-way screw terminal, 5.08mm | V+ / GND in |
| J2, J3 | 2 | 6-way screw terminal, 3.81mm | 4× OUT + 2× GND each |
| — | 2 | 20-pin 2.54mm socket strip | Pico, socketed for reflashing |

Pico supplies its own USB-C. Nothing on the isolated side is powered from USB.

## Layout notes

**The isolation barrier is a layout problem, not a schematic one.** Getting the schematic
right and the layout wrong gives you a board that looks isolated and is not:

- Keep a clear gap between the two ground pours — 3mm is comfortable at these voltages, and a
  routed slot under the optos is better still
- No traces, no pour, no stitching vias cross the barrier except the optos themselves
- The optos are the only components straddling it

Otherwise: keep V+ and output traces generous (these are low-current, so this is about
robustness not heating), put the bulk cap near the terminal block, and keep the LEDs at the
board edge where you can see them.

## What I have not done

I designed this; I have not verified it. There is no simulation behind it, no breadboard, no
bench measurement. Specifically unverified:

- Every footprint against its actual part
- The gate drive under real load and switching (the Zener clamp is sound in principle; I have
  not scoped it)
- Opto CTR margin at end-of-life — optos degrade, and 4.5 mA of LED current is not generous
- Thermals, though at these currents there should be nothing to find

Before committing to a run: build one channel on a breadboard and confirm it switches cleanly
at both 5V and 24V, and take PCBWay's free DFM check. A first order of 5 boards is cheap
insurance against a footprint error.

**Mains stays off this board.** It switches signal voltages to certified relay modules or SSRs
that handle the load. Creepage, clearance, fusing and earthing for mains are governed by
standards that vary by jurisdiction and want a qualified review — not something to inherit
from a design document.
