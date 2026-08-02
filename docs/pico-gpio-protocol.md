# PICOGPIO protocol v1

A line protocol for driving relay outputs from a microcontroller attached to the host over
USB. Written for a Raspberry Pi Pico, but nothing in it is Pico-specific.

The point of a host-attached output device — rather than the controller's own aux outputs —
is that it sits outside the g-code stream. `M62`/`M64` execute when grblHAL *parses* them,
which during a job is well ahead of the cutting point, so stream-based outputs need dwell
tricks to land at the right moment. A host-side output has no ordering problem because it
is not in the stream at all.

## Transport

USB CDC, `8N1`. Baud rate is ignored by CDC but set 115200 for the sake of tools.

ASCII, one command per line, `\n` terminated (`\r\n` accepted). One response line per
command. Commands are case-insensitive; responses are lower-case except the banner.

The device must **never** send unsolicited output other than the banner on reset. A host
that opens the wrong port has to be able to tell immediately, and a device that chatters
makes that harder.

## Commands

| Command | Meaning | Response |
|---|---|---|
| `?` | Identify | banner (see below) |
| `C <pin> <0\|1>` | Claim `<pin>` as an output, driven to the given level in the same operation | `ok` |
| `O <pin> <0\|1>` | Set an already-claimed pin | `ok` |
| `X` | Release every pin and drive all outputs inactive | `ok` |
| `H` | Heartbeat; feeds the watchdog and does nothing else | `ok` |

Errors are `err <reason>`, e.g. `err bad pin`, `err not claimed`, `err parse`. A reason is
for a human reading a terminal; the host only distinguishes `ok` from everything else.

`C` takes an initial level so a pin is never briefly at whatever the output register held —
long enough to click a relay. Claiming an already-claimed pin re-drives it and is not an
error, which keeps host reconnects idempotent.

## Banner

Sent in response to `?`, and once on reset:

```
PICOGPIO 1 pins=0-22,26-28 wd=5000
```

| Field | Meaning |
|---|---|
| `PICOGPIO` | Fixed. The host refuses to drive anything without it. |
| `1` | Protocol version. The host rejects majors it does not know. |
| `pins=` | Comma-separated list of valid pin numbers and inclusive ranges |
| `wd=` | Watchdog timeout in milliseconds; `0` means no watchdog |

The host sends `H` at roughly half `wd`.

## Watchdog — required

**If no complete line arrives for `wd` milliseconds, the device drives every output
inactive and releases the pins.**

This is the part that makes a USB device safer than driving pins from the host directly. It
covers the cases the host cannot: the application crashing, the cable being pulled, the host
sleeping while USB power stays up, someone killing the process. A host-side "turn everything
off on exit" only works when the exit is orderly.

The device must recover on its own when the host comes back, without a power cycle. After a
watchdog trip it returns to the reset state, so the host re-claims its pins with `C`.

## Firmware requirements

Beyond the watchdog:

- **Boot inactive.** Outputs must read inactive from power-on through firmware init, before
  the host has said anything. Same reasoning as choosing active-high relay boards on a Pi:
  the pins idle low, so active-high hardware is safe by default.
- **Unknown commands are `err`, never silent.** A host talking an older protocol should fail
  loudly rather than appear to work.
- **No output buffering across lines.** Respond per command; the host matches responses by
  order, not by tag.

## Host behaviour

The host **must not probe serial ports.** The port is chosen explicitly in configuration.
A grblHAL controller and a Pico both enumerate as `/dev/ttyACM*` on Linux and are
indistinguishable by name, and an identify string written at a controller is at best noise
in its console.

After opening the port the host sends `?` and waits for a banner before issuing anything
else. No banner, wrong name, or an unknown major version means the backend reports itself
unavailable and never writes.

## Example session

```
→ ?
← PICOGPIO 1 pins=0-22,26-28 wd=5000
→ C 16 0
← ok
→ C 17 0
← ok
→ O 16 1
← ok
→ H
← ok
→ O 16 0
← ok
→ X
← ok
```

## Wiring note

The relay coil supply is not the Pico's USB rail. A four-channel board's coils can pull
300–400 mA against a budget shared with everything else on that host controller. The device
provides logic level and a common ground; the coils get their own supply.

Pico logic is 3.3V and not 5V tolerant. Solid-state relay modules that accept 3–32 V DC on
the control input are the tidiest pairing, with active-high mechanical boards next.
