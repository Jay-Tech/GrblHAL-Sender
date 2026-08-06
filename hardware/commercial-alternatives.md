# Commercial relay boards — and why the custom board is on the shelf

Found while pricing parts for the custom board, 2026-08-06. Both are Pico carrier boards with
relays on them, and either covers the shop-outputs use case for a fraction of what the custom
board costs to build.

**This does not make the custom boards wrong — it makes them unnecessary.** The design in
`shop-output-board/` and `shop-output-board-v2/` switches a 5–24 V *signal* out to screw
terminals, which then has to drive external relay modules or SSRs. So the real comparison was
never board-against-board. It was:

> custom PCB + 8 relay modules + a field PSU + an enclosure + assembly
> **against**
> one bought board, around £22, in a case, on a DIN rail

That is not close, and it stopped being close the moment the requirement was "switch some
shop lights and a dust extractor".

## The two candidates

| | Waveshare Pico-Relay-B | SB Components Pico Relay Board |
|---|---|---|
| Channels | 8 | 4 |
| Contacts | 10 A 250 VAC / 10 A 30 VDC | 7 A 240 VAC / 10 A 30 VDC |
| Coil drive | photocoupler + **isolated DC-DC supply** | photocoupler, **shared 5 V and GND** |
| Flyback | — (verify) | LL4148 per channel |
| Drive sense | **verify before trusting** | **active-high**, confirmed in schematic |
| GPIO | **verify** | GP18–21, jumper-selectable |
| Enclosure | ABS, DIN-rail mount | bare board |
| Docs | wiki | GitHub, schematic PDF published |

Waveshare: <https://www.waveshare.com/pico-relay-b.htm>
SB Components: <https://github.com/sbcshop/Raspberry-Pi-Pico-Relay-Board>

## The isolation difference, stated honestly

The SB Components board has PC817 optocouplers, but tracing its schematic the opto output
sits on the **same 5 V rail and the same ground** as the Pico. The optos are current buffers;
they isolate nothing. That is exactly what `shop-output-board/design.md` warns about —
*"tying them together would make the optos into mere level shifters"*.

**This matters much less on a relay board than it did on the custom design**, and it is worth
being clear why. The custom board sent a 5–24 V signal out on screw terminals, so the field
ground physically left the board on wires and a shared ground meant a real loop between the
host and whatever the relays were bolted to. A relay board sends **dry contacts** out instead.
Contacts are galvanically isolated by construction, so the load side is isolated regardless of
what the coil drive does.

What the SB board's arrangement does cost:

- Relay coil current — roughly 70–90 mA each, so ~360 mA with four on — is drawn from the
  **same 5 V rail as the Pico**
- Coil switching noise couples into that rail

Waveshare's onboard isolated DC-DC avoids both. In a shop with a VFD spindle a few metres
away, that is a real robustness difference rather than a specification nicety. It is the main
reason to prefer the 8-channel board even if four channels would do.

## Verify these before trusting either one

The firmware side is genuinely free: `docs/pico-gpio-protocol.md` declares its pin map in the
banner and `GrbLHALSender/Gpio/PicoBanner.cs` parses it, so the host *discovers* the pins.
Flash the same PICOGPIO firmware with the right pin list and the sender works unchanged. That
design decision has now paid for itself.

What is not free is the fail-safe. Check both:

1. **Drive polarity.** The whole safety story is that outputs read inactive from power-on
   through firmware init, before the host has said anything. Plenty of relay boards are
   **active-low**, where a floating GPIO at boot energises the relay. SB Components is
   confirmed active-high from its schematic (GPIO → 330 Ω → opto LED → GND, so no drive means
   no coil). **Waveshare is unverified — its wiki blocks automated fetching, so read it.**
2. **Boot state, on the bench.** Power the board with nothing driving the pins and confirm
   every relay stays open right through boot and USB enumeration. Ten minutes with a
   multimeter, and it is the same property that was verified on the Pi header on 2026-08-01.
3. **Whether the coil supply is shared**, if you care about spindle noise — see above.

## What the custom boards are still for

Nothing here obsoletes the design; it just removes the reason to build it *now*. Both boards
are verified, DRC-clean and documented, and they remain the answer if a requirement ever
appears that a bought relay board cannot meet:

- a switched **5–24 V signal output** rather than dry contacts, for driving SSRs or an
  existing relay panel that expects a voltage
- genuine **galvanic isolation of the output domain**, with the field ground leaving the board
- a specific form factor — v2 is 70 × 92 mm against the Waveshare's enclosure

And the parts of this that outlived the hardware: `KICAD-CHEATSHEET.md`, which is four traps
that each passed every check available at the time, and
`shop-output-board/bench-test.md`, whose method applies to anything with a gate clamp in it.
