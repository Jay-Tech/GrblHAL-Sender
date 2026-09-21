# Running the sender as a Raspberry Pi appliance

How to take a Pi 5 off the full Raspberry Pi OS desktop image and run it as a
single-purpose machine control panel: no desktop environment, no login screen,
the sender on screen a few seconds after power-on and nothing else on the box.

Written against a Pi 5 (8 GB) booting from an NVMe SSD, which is the hardware
this has actually been run on.

## What you end up with

Raspberry Pi OS **Lite** (64-bit), an X server with no desktop environment,
openbox as a two-megabyte window manager, and the sender as the entire session.
Quitting the sender takes X down with it, the tty logs back in automatically and
the sender comes straight back up.

The win here is not memory. The app uses about 300 MB and you have eight
gigabytes. The win is that nothing else is running: no compositor competing for
the GPU, no notification daemon, no unattended upgrade restarting services
mid-job, and — the one that matters on a CNC panel — nothing probing the serial
port behind your back.

## Why Lite, and not a different distro

Ubuntu Server, DietPi and Alpine all look like the obvious answer and all cost
more than they save:

- `GpioOutputService` opens `/dev/gpiochip0` and expects it to be the RP1 behind
  the 40-pin header. Chip numbering and device-tree labels on the Pi 5 are a
  Raspberry Pi kernel contract; elsewhere they have moved. A wrong chip means
  relay outputs that silently do nothing, or that switch a pin you did not mean
  to switch.
- The toolpath view is real GLES through `OpenGlControlBase`, so it needs Mesa's
  V3D driver working against the Pi firmware. Pi OS ships the combination that
  has been tested together.
- Lite is the *same* image with the desktop removed, so if something breaks it
  broke because of something you took out — not because the kernel, the firmware
  and Mesa all changed at once.

## Why there is still an X server

"No GUI" does not mean "no display stack". `Avalonia.Desktop 12.1.2` resolves to
`Avalonia.X11`, `Avalonia.Win32` and `Avalonia.Native` — there is no Wayland
backend and no `Avalonia.LinuxFramebuffer` anywhere in the graph. On Linux this
app is X11, and what you are removing is the *desktop environment*, not the
display server.

`Avalonia.X11.dll` dlopen()s a specific set of libraries, and that set is the
whole story for what has to be installed:

| Library | Package |
|---|---|
| `libX11.so.6` | `libx11-6` |
| `libXext.so.6` | `libxext6` |
| `libXi.so.6` | `libxi6` |
| `libXrandr.so.2` | `libxrandr2` |
| `libXcursor.so.1` | `libxcursor1` |
| `libXfixes.so.3` | `libxfixes3` |
| `libICE.so.6` | `libice6` |
| `libSM.so.6` | `libsm6` |
| `libGL.so.1` | `libgl1` |

Skia, HarfBuzz and SDL3 are deliberately *not* on that list: those ship as
native `.so` files inside the publish output. The Skia build in use is
`SkiaSharp.NativeAssets.Linux.NoDependencies`, which is the fontconfig-free one,
and the UI font is embedded through `WithInterFont()`. So no fontconfig, no
system fonts, no system SDL. The publish is self-contained (~163 MB), so there
is no .NET runtime to install either.

## The quick path

Flash Raspberry Pi OS Lite (64-bit), boot it, ssh in, then:

```bash
sudo apt install ./grblhal-sender_1.4.0_arm64.deb
```

```bash
git clone https://github.com/Jay-Tech/GrblHAL-Sender.git
```

```bash
bash GrblHAL-Sender/installer/linux/kiosk/setup-kiosk.sh
```

```bash
sudo reboot
```

Invoked through `bash` because scripts are stored non-executable in this
repo, the same way `release.yml` chmods `build-deb.sh` before calling it.

Add `--rotate left` for a portrait panel. The script is safe to run twice, and
takes an optional `.deb` path as an argument if you would rather it did the
install as well.

## Two networks

A CNC panel usually sits on two at once: the controller on Ethernet, and the
shop LAN on Wi-Fi for the web UI, updates and everything else. Both hand out a
DHCP lease, and both install a default route:

```
default via 192.168.5.1 dev eth0  proto dhcp metric 100
default via 192.168.1.1 dev wlan0 proto dhcp metric 600
```

The lowest metric wins, which is normally the wired one — and the controller
does not route to the internet. Every connection then goes nowhere.

What makes this hard to read is that DNS keeps working, because a resolver is
still reachable on one of the two subnets. So `wget` prints a resolved address
and then sits on `Connecting to`, `apt` stalls, and the clock quietly drifts
because NTP cannot get out either. It all looks like a name resolution problem
and none of it is.

The fix takes away the wired connection's claim to be the default route while
leaving the interface and its subnet route alone — that route is how the sender
reaches the controller. Find what the connection is called first; on this build
it is `netplan-eth0` rather than NetworkManager's usual
`Wired connection 1`:

```bash
nmcli connection show
```

```bash
sudo nmcli connection modify netplan-eth0 ipv4.never-default yes ipv4.ignore-auto-dns yes
```

```bash
sudo nmcli connection up netplan-eth0
```

`ignore-auto-dns` because whatever serves DHCP on the machine side may be
advertising a nameserver too, and DNS should come from the LAN side only.

### Does it survive a reboot?

On Raspberry Pi OS Lite it does, and the naming is what tells you so. Seeing
`netplan-eth0` from `nmcli` and `90-NM-<uuid>.yaml` in `/etc/netplan` looks at
first like netplan is in charge and NetworkManager is only rendering what it is
told — which would mean an `nmcli` change gets regenerated away at boot. It is
the other way round. NetworkManager is the source of truth here and netplan is
just where it stores profiles, so a modification is written straight back into
that file. Confirm it against the UUID that matches the connection:

```bash
sudo cat /etc/netplan/90-NM-<uuid>.yaml
```

```yaml
      networkmanager:
        uuid: "75a1216a-9d1a-30cd-8aca-ace5526ec021"
        name: "netplan-eth0"
        passthrough:
          ipv4.ignore-auto-dns: "true"
          ipv4.never-default: "true"
```

`sudo` because these are root-only — the Wi-Fi PSK lives in the other one.

The arrangement that *does* lose `nmcli` changes is hand-written netplan YAML,
as on Ubuntu Server: a `50-cloud-init.yaml` or `01-netcfg.yaml` rather than
`90-NM-<uuid>.yaml`. There, set it at the source on the ethernet stanza:

```yaml
      dhcp4-overrides:
        route-metric: 1000
        use-dns: false
```

That leaves the default route in place but ranks it below the Wi-Fi's 600,
reaching the same end without removing anything. Apply with `sudo netplan
apply`.

Deleting the route by hand with `ip route del` is worth knowing as the fastest
way to confirm the diagnosis before changing anything, but DHCP puts it back on
the next renew.

Afterwards you want exactly one default route, both subnet routes still present,
and the controller still answering — then the same again after a reboot:

```bash
ip route
```

```bash
ping -c3 192.168.5.1
```

None of this affects the web UI: `WebServerService` calls `ListenAnyIP`, so it
binds every interface and stays reachable on the LAN address either way.

## What the script does, and why

**Installs X and one window manager.** `xserver-xorg-core`,
`xserver-xorg-input-libinput`, `xfonts-base`, `xinit`, `x11-xserver-utils`,
`xinput`, `openbox`, and the libraries in the table above. Two of those are easy
to leave out and painful to diagnose: without `xfonts-base` the server will not
start at all, and without an input driver it comes up looking perfect and
ignores the touchscreen completely.

**Uses openbox rather than no window manager.** The main window asks for
fullscreen and would get it either way. The file pickers would not — load
g-code, SD card upload and settings import/export all go through Avalonia's
`StorageProvider`, which with no desktop portal present falls back to a managed
picker that is a real top-level window. With nothing managing windows, those
open unfocused or badly placed and swallow keyboard input.

**Adds the user to `dialout`, `input`, `video`, `render` and `gpio`.** The
`input` one is not obvious: SDL3 reads `/dev/input/event*` directly for gamepad
jogging, so X owning the touchscreen does nothing for it.

**Purges ModemManager and brltty if present.** ModemManager probes new tty
devices and sends AT commands at them; brltty claims several USB-serial bridges
outright, and a CH340 that vanishes seconds after appearing is almost always
brltty. Neither has any business on a CNC panel.

**Starts X from the shell profile, not a systemd service.** X needs a real
logind session before it is handed DRM master and the input devices. A system
service starts outside one and fails at exactly that point, with an error that
points nowhere useful. So: console autologin on tty1, and a guard in
`~/.bash_profile` that runs `startx`. That guard also checks for
`/boot/firmware/no-kiosk`, which is the recovery hatch — if the app ever comes up
broken enough that you cannot reach a shell, put the drive in another machine
and touch that file on the FAT partition to boot to a plain console.

It runs `startx` as a child rather than `exec`-ing it, and logs to
`~/kiosk.log`. An exec replaces the login shell, so a session that dies during
startup ends the login with it, agetty respawns, and after five rounds systemd
hits the restart limit and stops the tty for good — leaving a black screen with
a blinking caret and no way to see why, because the console that would have
shown the error is the one the loop destroyed. As a child, a session that exits
inside ten seconds falls through to a visible shell with the reason in the log,
and one that exits after longer is treated as an ordinary exit and restarts the
kiosk as before.

## Touch

Worth knowing before you start: on the desktop image the Pi 5 almost certainly
was not using X at all. Raspberry Pi OS runs Wayland there — wayfire on
Bookworm, labwc on newer — and since Avalonia has no Wayland backend, the sender
has been running under XWayland. Confirm on the current install with:

```bash
echo $XDG_SESSION_TYPE
```

`wayland` means the touch events reach the app through
kernel evdev, libinput, the compositor, XWayland, then XInput2. On Lite with a
bare X server that becomes kernel evdev, `xf86-input-libinput`, XInput2. Two
hops shorter, and it is the path Avalonia's X11 backend is actually written
against.

So whatever you had to enable divides in two:

- **Anything in `/boot/firmware/config.txt`** — a `dtoverlay` for a DSI or DPI
  panel, `disable_touchscreen`, display enablement — is firmware and device
  tree. Same file on Lite, carries over verbatim. Copy it across before you
  wipe the current install.
- **Anything set in the desktop's Screen Configuration, Raspberry Pi
  Configuration, `wayfire.ini` or labwc's `rc.xml`** is compositor
  configuration and does not carry over. Under bare X the equivalents are
  `xinput` properties: `xinput map-to-output` to bind a touch device to a
  particular display, and the coordinate transformation matrix, which
  `~/.xinitrc` already sets when you give it a rotation.

The app needs genuine multi-touch rather than pointer emulation:
`CameraGestureHandler` tracks a separate pointer id per finger, because two
fingers pinch-zoom and pan the toolpath at the same time. Single-touch that
arrives as emulated mouse clicks will look like it works until someone tries to
zoom.

Three checks, in order of how much they tell you:

```bash
libinput list-devices
```

Your panel should be listed with `Capabilities: touch`. If it is not here, it is
a kernel or udev problem and X is not involved yet.

```bash
xinput list
```

Under X it should appear as a slave pointer device.

```bash
xinput test-xi2 --root
```

The one that actually settles it. Put two fingers on the glass: you want
`TouchBegin` / `TouchUpdate` / `TouchEnd` events carrying two different `detail`
values. If all you see is `Motion` and `ButtonPress`, you are getting pointer
emulation and pinch-zoom will not work.

If the device is present but arrives as a plain pointer, check how udev tagged
it — `ID_INPUT_TOUCHSCREEN=1` is what makes `xf86-input-libinput` treat it as a
direct touch device:

```bash
udevadm info /dev/input/event0 | grep ID_INPUT
```

`libinput-tools` and `xinput` are both installed by the setup script for exactly
this, because on a box with no desktop there is nothing else left to ask.

### What actually happened

Nothing had to be enabled. On a bare Xorg with `xf86-input-libinput`, the panel
came up with working touch and working two-finger gestures on the toolpath
straight away — no `dtoverlay`, no coordinate matrix, no device configuration.
Pinch needs a separate pointer id per finger, so gestures working is proof of
genuine multi-touch rather than pointer emulation.

Worth setting against the effort the desktop image took. That path ran through
the compositor and XWayland before reaching the app and needed touch enabled by
hand; this one is the path Avalonia's X11 backend is written against, and it
simply worked.

## Display rotation

The desktop compositor was handling this for you, and it is the part most likely
to need redoing. `~/.xinitrc` has a `ROTATION` variable at the top; set it to
`left`, `right` or `inverted` and it rotates the output with `xrandr` and then
applies the matching coordinate transformation matrix to every input device with
"touch" in its name.

That second step is the one people forget. Rotating the screen does not rotate
the touchscreen — X goes on handing the driver's raw coordinates to a display
that is now on its side, so touches land at the wrong end of it. On a screen
with a feed hold button on it, that is a safety problem rather than a cosmetic
one. Check it before you cut anything.

If the name heuristic misses your panel, find it with `xinput list` and set the
matrix against its real name.

## Things that will bite you

**ICU.** A self-contained .NET build carries no ICU, and .NET on Linux refuses to
start without it — the app exits before the first frame with `Couldn't find a
valid ICU package installed on the system`. Whether a given image satisfies that
is luck: on the Trixie Pi OS Lite image `libicu76` was already present by the
time the setup script looked, having come in as a dependency of something else,
so the hazard is smaller than it sounds. It is still a dependency on what
happens to be installed alongside you. Current builds set
`InvariantGlobalization` and remove the question — the app already forced
`InvariantCulture` on every thread in `Main`, because grblHAL only ever speaks
dot-decimal, so switching the rest of ICU off changed nothing. If you are
deploying a `.deb` built before that change, the setup script installs
`libicu72` (Bookworm) or `libicu76` (Trixie) for you.

**X dying instantly — two different causes that look identical.** Either one
kills the server in about 150 ms, and with the old `exec startx` guard that
presented as a black screen with no explanation. Both are handled by the setup
script; this is here so the log is recognisable if it happens again.

The first is fbdev. With no configuration at all, X enumerates every video
driver installed, and on Pi OS Lite that includes `fbdev`, which cannot resolve
a busID on a KMS-only machine and fails fatally — one driver's failure ends the
whole server:

```
(EE) Cannot run in framebuffer mode. Please specify busIDs
     for all framebuffer devices
```

The second only becomes visible once the first is gone. A Pi 5 presents two DRM
nodes — a V3D render-only device and the display — and autoconfig assigns the
display as a *GPU device* rather than as a screen, leaving the server with none:

```
(II) modeset(G0): using drv /dev/dri/card1
(EE) No devices detected.
(EE) no screens found
```

The `G0` is the tell: that is an offload GPU, not `Screen 0`. The script
installs `99-kms-screen.conf`, which declares the Device, the Screen and a
ServerLayout explicitly so autoconfig has nothing left to get wrong, and it
substitutes the DRM node rather than hardcoding it — card numbering follows
probe order, and the display is not card0 on a Pi 5.

**Config location.** `ConfigManager` uses `SpecialFolder.ApplicationData`, which
is `$HOME/.config` on Linux. Launch the app from anything without a proper
`HOME` and your settings land somewhere surprising, or silently do not persist.
That is a second reason the session starts from a login rather than a system
service.

**GL.** If the 3D pane comes up blank or obviously software-rendered, the
missing piece is `libgl1-mesa-dri` — `libGL` resolves without it but there is no
hardware driver behind it. The app degrades rather than failing here:
`RenderControlFactory` swaps in the software renderer when GL cannot be brought
up.

**The web UI and the pendant are unaffected.** The embedded server and the
pendant listener on 8422 stay reachable over the network through all of this. If
you reach the web UI by `raspberrypi.local`, check that `avahi-daemon` is
installed — a Lite image is not guaranteed to have it.

## Getting back to a prompt

The session restarts itself when the app exits, so closing the window does not
give you a console. Any of:

- ssh in, which is the normal answer
- `Ctrl+Alt+F2` for a second tty — the kiosk only claims tty1
- `touch /boot/firmware/no-kiosk` from another machine, to boot to a console

## What about no X at all?

Avalonia's DRM/KMS backend would remove X entirely. `Avalonia.LinuxFramebuffer`
is published at 12.1.2, the same version as everything else here, and a fair
amount of this app is already shaped for it: exactly one class in the codebase
is `Window`-typed (`MainWindow`), every other view resolves its host through
`TopLevel.GetTopLevel(this)`, the tool and console dialogs are already in-window
overlays through `DialogHostView`, and text entry goes through the app's own
`VirtualKeyboardView` rather than a system on-screen keyboard - which is usually
the thing that makes a kiosk-without-X unworkable.

Two things stand between here and there. A third is already done.

**The canvas host is no longer window-specific.** Choosing between the landscape
and portrait canvases, scaling the chosen one to fit, the renderer overlay switch
and the touch text-handle fix all used to live in `MainWindow`, which put them
out of reach of any host that is not a Window - the single-view branch set
`MainView` directly and would have shown an unscaled 1920x1080 landscape canvas
on whatever panel was attached. That logic is now in `RootCanvasHost`, which
`MainWindow` hosts and the single-view branch uses as its root, so both paths get
the same behaviour.

**The file pickers would have to be rebuilt.** Load g-code, SD card upload and
settings import/export all go through `TopLevel.StorageProvider`.
`Avalonia.Dialogs` has no single-view code path - it references `ShowDialog` and
hosts the managed chooser in a `Window`, which the framebuffer backend does not
have. Those flows would need to become views inside the existing `DialogHostView`
overlay. On a touchscreen that is arguably the better answer anyway, but it is a
feature to build, not a backend to switch.

**Shutdown would need wiring.** `ShutdownRequested` is on the desktop lifetime
only, so `ShutDownServices` would never run - and the comment on that method
describes finding a windowless instance still holding port 8422 and still able to
move the machine. A `PosixSignalRegistration` for SIGTERM and SIGINT covers it,
but it has to be done deliberately.

Past that sit the unknowns worth measuring before committing: whether EGL/GBM
brings up the GLES context for the toolpath view, how the evdev/libinput touch
path behaves, and rotation - there is no `xrandr` on DRM, so a panel that is not
natively portrait would need KMS rotation and a matching transform on the touch
input.

None of that is a reason not to do it. It is a reason to do it as its own piece
of work, on a machine that is already running, rather than as part of getting off
the desktop image.
