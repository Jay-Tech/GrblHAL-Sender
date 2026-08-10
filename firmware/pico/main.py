# PICOGPIO v1 reference implementation for MicroPython on a Raspberry Pi Pico.
# Protocol: docs/pico-gpio-protocol.md
#
# Copy to the board as main.py so it runs on boot and owns the USB serial port.
# Ctrl-C at a terminal drops back to the REPL if you need to edit it.

import sys
import select
import time
from machine import Pin

VERSION = 1
PIN_SPEC = "0-22,26-28"
WATCHDOG_MS = 5000

_outputs = {}


def _valid(pin):
    return 0 <= pin <= 22 or 26 <= pin <= 28


def _all_off():
    """Drive every claimed pin inactive and release it."""
    for pin in _outputs.values():
        pin.value(0)
    _outputs.clear()


def _banner():
    return "PICOGPIO %d pins=%s wd=%d" % (VERSION, PIN_SPEC, WATCHDOG_MS)


def _handle(line):
    parts = line.strip().split()
    if not parts:
        return None

    cmd = parts[0].upper()

    if cmd == "?":
        return _banner()
    if cmd == "H":
        return "ok"
    if cmd == "X":
        _all_off()
        return "ok"

    if cmd in ("C", "O"):
        if len(parts) != 3:
            return "err parse"
        try:
            number = int(parts[1])
            level = 1 if int(parts[2]) else 0
        except ValueError:
            return "err parse"

        if not _valid(number):
            return "err bad pin"

        if cmd == "C":
            # Claim and drive in one step so the pin is never briefly at whatever
            # the output register held. Re-claiming is not an error, which keeps
            # host reconnects idempotent.
            pin = Pin(number, Pin.OUT)
            pin.value(level)
            _outputs[number] = pin
            return "ok"

        if number not in _outputs:
            return "err not claimed"
        _outputs[number].value(level)
        return "ok"

    return "err unknown"


def run():
    poller = select.poll()
    poller.register(sys.stdin, select.POLLIN)

    buffer = ""
    last_rx = time.ticks_ms()

    # Announced on reset so a host that opens the port mid-boot still identifies it.
    print(_banner())

    while True:
        # Short timeout rather than a blocking read, so the watchdog still runs
        # when the host has gone quiet.
        if poller.poll(100):
            char = sys.stdin.read(1)
            if char is None:
                continue

            last_rx = time.ticks_ms()

            if char in ("\n", "\r"):
                if buffer:
                    response = _handle(buffer)
                    if response is not None:
                        print(response)
                    buffer = ""
            elif len(buffer) < 64:
                buffer += char
            else:
                # A runaway line is almost certainly not this protocol. Drop it
                # rather than growing the buffer without limit.
                buffer = ""

        elif time.ticks_diff(time.ticks_ms(), last_rx) > WATCHDOG_MS:
            # The host crashed, the cable was pulled, or the machine went to sleep
            # with USB power still up. None of those are reachable by the host's
            # own shutdown handler, which is the whole reason this exists.
            _all_off()
            last_rx = time.ticks_ms()


try:
    run()
finally:
    # Ctrl-C back to the REPL should not leave a relay energised.
    _all_off()
