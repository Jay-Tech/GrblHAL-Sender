# Project is back Active

## GrblHal Sender a cross platform sender application  built using Avalonia UI and .Net

Goal was to create a application that will run on a cheap mini PC with a UI design that is touch monitor friendly, and will not require a mouse or keyboard to use.
</br>
---
![Home Screen](Media/HomeScreen.png)
</br>
## GrblHAL Setting supports export and import 
![Settings Screen](Media/Setting.png)
</br>
## Supports Serial, TCP, or WebSocket connection protocols.  Push and hold "Connect" to setup connection type and Auto Connect on app launch if desired 
![Settings Screen](Media/ConnectionOptions.png)
</br>
## Double clicking any Text Fields brings up virtual keyboard
![Keyboard](Media/KeyBoard.png)
</br>
## Host local Web Server for remote file upload 
![WebServer](Media/WebServer.png)
</br>
## Supports Wireless Controller
![GamePad](Media/DefaultGamePad.png) 
</br>
# Settings Overview
## App
![AppSetting](Media/SettingApp.png)
</br>
## Rapid-ATC
![AppSetting](Media/SettingRAtc.png)
</br>
## Wireess Gamepad
![Gamepad](Media/GamePad.png) ![Setting Button](Media/GamePadButtons.png) ![Setting Trigger](Media/GamePadTrigger.png)
</br>
## Web Server
![WebServer](Media/SettingWebServer.png)
</br>
## Macro Download and Upload Suppprted
![SDCard](Media/settingSdcard.png)

# Tool Change

What the sender does at an `M6` depends on the controller's `$341` tool change mode. The
mode is read from the machine at connect, so the relevant controls appear on their own —
there is nothing to configure in the app to match it.

| `$341` | Mode | What the sender does |
|---|---|---|
| 0 | Normal | Pauses at the `M6`, acknowledges it, waits for **Start**. Your program is responsible for stopping the spindle, moving to the change position and restoring afterwards. |
| 1 | Manual touch off | As above, plus a **Touch Off** button |
| 2 | Manual touch off @ G59.3 | As mode 1, and needs the [tool setter position](#tool-setter-position-g593) set |
| 3 | Automatic touch off @ G59.3 | Pauses; the controller probes for itself on **Start**. Needs the tool setter position set. |
| 4 | Ignore M6 | No pause — the tool number is recorded and the program continues |

In every mode except 4 the machine reports `Tool` state, the sender acknowledges it
(`0xA3`) once on entry, and the program continues only when you press **Start**. It behaves
like an `M0` pause: the acknowledgement does not resume anything.

## Streaming stops at the tool change

The sender holds the rest of the file back when it sends an `M6`, and does not resume until
the controller reports the change finished. This matters because grblHAL rejects g-code
while a tool change is pending (`error:40`) — anything already sitting in its receive
buffer past the `M6` is discarded rather than queued, which on a short file can be the
whole remainder of the program.

While the job is paused there you can **jog freely** and use **MDI**, including as many
moves as you like; the file position does not advance and nothing is lost. Macro and probe
buttons stay disabled, since those would interleave into the program.

## Manual touch off (modes 1 and 2)

**Touch Off** and **Set Ref** buttons appear beside the `TOOL` banner while the job is
paused.

- **Touch Off** sends `$TPW` — probes the new tool and applies its length offset
- **Set Ref** sends `$TLR` — captures the probe just taken as the reference `$TPW` measures
  against

Every tool change: stop at the `M6` → change the tool → jog it over the reference surface →
**Touch Off** → **Start**.

## Tool length reference — do this before the job

`$TPW` applies the *difference* between this probe and the previous one, so it needs a
baseline. Set it once per session under **Probe → Tool Setter**, with the tool you are going
to zero the stock from:

- **Probe Here** — probes straight down from where the machine is standing; jog the tool
  over the setter first
- **Probe at G59.3** — retracts Z to machine zero, travels to the stored tool setter
  position, descends and probes

Either way the reference is set only if the probe actually makes contact. Then zero on the
stock and start the job, and every tool change from there needs nothing but **Touch Off**.

The panel says whether the controller currently holds a reference, and the in-job **Set
Ref** button remains as a fallback if you start a job without one.

> `$TLR` must follow a *successful* probe. Sent before one, or after a failed one, it clears
> the reference instead of setting it — which is why the app only issues it on a good probe.

**Set Ref** rings red while the controller holds no reference and green once it does, from
the `TLR` field of the status report. It is disabled while a reference exists — sending
`$TLR` again re-bases the datum onto whatever was last probed, silently and with no error,
which would shift work Z zero by the difference between the tools. To re-establish it
deliberately — after moving the tool setter, say — send `$TLR` from MDI, which stays
available during a tool change pause.

Touch plate thickness plays no part in this. The offset is a differential against the same
surface, so the thickness cancels out. Thickness only matters for setting workpiece Z zero,
which is a separate operation in the **Probe** dialog.

## Tool setter position (G59.3)
![ToolSetter](Media/ToolSetter.png)
</br>
Modes 2 and 3 drive to the `G59.3` offset to reach the tool setter. Set it under
**Probe → Tool Setter**: jog the machine where you want it and store the position. X/Y and
Z are stored separately so neither overwrites the other by accident, and the stored value is
read back from the controller after each change so you can see what actually took effect.

> **The stored Z is where probing starts, not the tool setter surface.** The controller
> moves all three axes to `G59.3` and then probes downward by `$342`. Store Z at a safe
> clearance height above the setter — high enough for your longest tool, and no further
> above the setter than `$342`.

## Macro messages

Anything the controller says in words — including `(debug, ...)` output from a tool change
macro — is shown in the job panel, not only in the console. A macro that stops on
"manually unload the tool and unlock to continue" says so on screen.

# G-code Events
![GcodeEvents](Media/GcodeEvents.png)
</br>
Pre and post commands can be injected around any g-code event, configured under
**Utility → G-code Events**. The original use case is lifting a dust shoe out of the way for
a tool change or a homing cycle, but nothing about it is specific to that.

Each rule has a trigger and the commands to wrap it with:

- **Trigger** — one or more commands, comma separated, e.g. `$H,G28`. A `$` command matches
  as a prefix, so `$H` also catches `$HX`. A single word matches by value, so `M6` catches
  `T3M6` and `M06` but not `M61`.
- **Pre / Post** — commands to send before and after the trigger, separated by `|` for more
  than one. `{T}` inserts that word's value from the triggering line, `{LINE}` the whole line.
- **Job / MDI** — whether the rule applies to a streamed file, to commands you issue by
  hand, or both.

Rules are applied when a file is loaded, so reload the file after editing one.

## Getting the timing right in a job

grblHAL executes `M64`/`M65` when it *parses* them, which during a job is well ahead of the
cutting point — so a plain `M65P0` before an `M6` fires many blocks early, while the spindle
is still down and cutting.

A dwell forces the planner to drain before continuing, so putting one *first* moves the
output change to the right moment:

| | |
|---|---|
| `M65P0\|G4P0.2` | lifts early, then waits — wrong |
| `G4P0.1\|M65P0` | waits for the cut to finish, then lifts — right |

Aux output buttons follow these commands wherever they come from, so a rule that toggles a
pin moves the matching button too.

### Testing status

Runs on Windows and on Linux including the Pi 5.

Verified on hardware:
- Tool change streaming at `$341=0` — the job holds at the `M6`, survives jogging during the
  pause, and resumes cleanly on **Start**
- G-code event injection around a tool change, including the dwell-first ordering
- Serial connection and reconnection

Still to test:
- Manual touch off (`$341=1` / `2`) — the **Touch Off** button and `$TPW`
- Tool setter position (`G59.3`)
- Probing
- Single instance guard on Linux
