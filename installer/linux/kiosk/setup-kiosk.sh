#!/bin/bash
# setup-kiosk.sh - turn a fresh Raspberry Pi OS Lite (64-bit) install into a
# single-purpose GrblHAL Sender appliance: no desktop environment, no login
# screen, the sender on screen a few seconds after power-on.
#
# Usage:
#   ./setup-kiosk.sh [--rotate left|right|inverted] [grblhal-sender_x.y.z_arm64.deb]
#
# Run it as the user that will own the session - not as root and not under sudo.
# It writes into that user's $HOME and adds that user to the device groups, and
# both of those are wrong if $USER is root. It calls sudo itself where it needs
# to. Safe to run twice.
#
# See docs/pi-appliance.md for what it is doing and why.
set -euo pipefail

ROTATION=normal
DEB=""

while [ $# -gt 0 ]; do
    case "$1" in
        --rotate) ROTATION="${2:?--rotate needs a value}"; shift 2 ;;
        -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
        *.deb)    DEB="$1"; shift ;;
        *)        echo "unknown argument: $1" >&2; exit 1 ;;
    esac
done

case "$ROTATION" in
    normal|left|right|inverted) ;;
    *) echo "--rotate must be normal, left, right or inverted" >&2; exit 1 ;;
esac

if [ "$(id -u)" -eq 0 ]; then
    echo "Run this as the user that will own the session, not as root." >&2
    exit 1
fi
command -v apt-get >/dev/null || {
    echo "This expects Debian / Raspberry Pi OS." >&2; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
APP_DIR=/usr/lib/grblhal-sender
RUNTIMECONFIG="$APP_DIR/GrbLHALSender.Desktop.runtimeconfig.json"

say() { printf '\n== %s\n' "$*"; }

# ---------------------------------------------------------------------------
say "X server, one window manager, and the libraries Avalonia.X11 dlopen()s"
# No --no-install-recommends here. xserver-xorg-core recommends xfonts-base and
# libgl1-mesa-dri; without the first X will not start at all ("could not open
# default font 'fixed'") and without the second there is no hardware GL behind
# libGL, which drops the 3D toolpath onto the software renderer. The input
# driver is listed explicitly because a server with no input driver comes up
# looking perfect and ignores the touchscreen entirely. libinput-tools is the one
# luxury here: `libinput list-devices` and `xinput test-xi2` are how you find out
# whether a touchscreen is delivering real multi-touch or emulated mouse clicks,
# and on a box with no desktop there is nothing else left to ask.
if ! sudo apt-get update; then
    echo >&2
    echo "apt could not reach the mirrors." >&2
    if [ "$(ip route show default | wc -l)" -gt 1 ]; then
        echo >&2
        echo "There is more than one default route installed:" >&2
        ip route show default | sed 's/^/    /' >&2
        echo >&2
        echo "A CNC panel usually has two networks - the controller on Ethernet" >&2
        echo "and the shop LAN on Wi-Fi - and both hand out a DHCP default route." >&2
        echo "The lowest metric wins, which is normally the wired one, and the" >&2
        echo "controller does not route to the internet. DNS still resolves," >&2
        echo "because a resolver is reachable on one of the subnets, so this looks" >&2
        echo "like a name problem when every connection is simply going nowhere." >&2
        echo >&2
        echo "Keep the interface and its subnet route - that is how the sender" >&2
        echo "reaches the controller - and take away only its claim to be the" >&2
        echo "default route:" >&2
        echo >&2
        echo "    nmcli connection show" >&2
        echo "    sudo nmcli connection modify <wired> ipv4.never-default yes ipv4.ignore-auto-dns yes" >&2
        echo "    sudo nmcli connection up <wired>" >&2
        echo >&2
        echo "See docs/pi-appliance.md, 'Two networks'." >&2
    fi
    exit 1
fi
sudo apt-get install -y \
    xserver-xorg-core xserver-xorg-input-libinput xserver-xorg-legacy \
    xfonts-base xinit x11-xserver-utils xinput libinput-tools openbox \
    libx11-6 libxext6 libxi6 libxrandr2 libxcursor1 libxfixes3 \
    libice6 libsm6 libgl1 libglx-mesa0 libgl1-mesa-dri

# ---------------------------------------------------------------------------
if [ -n "$DEB" ]; then
    say "Installing $DEB"
    sudo apt-get install -y "$(realpath "$DEB")"
fi

# ---------------------------------------------------------------------------
say "Checking the sender is actually installed"
# .xinitrc ends with `exec /usr/bin/grblhal-sender`. If that is not there, the
# session dies the instant X comes up, the tty logs straight back in and starts
# it again, and the result on the panel is a black screen with no clue what went
# wrong - the one failure mode of this setup that gives you nothing to read. The
# script used to install that .xinitrc regardless. Refuse instead.
if [ ! -x /usr/bin/grblhal-sender ]; then
    echo >&2
    echo "/usr/bin/grblhal-sender is not installed." >&2
    echo >&2
    echo "Install the package first, then run this again:" >&2
    echo >&2
    echo "    sudo apt install ./grblhal-sender_<version>_arm64.deb" >&2
    echo >&2
    echo "or pass the .deb to this script and it will do it:" >&2
    echo >&2
    echo "    bash $0 grblhal-sender_<version>_arm64.deb" >&2
    echo >&2
    exit 1
fi
echo "   present"

say "ICU"
# A self-contained .NET build does not carry ICU, and .NET on Linux refuses to
# start without it unless the build sets InvariantGlobalization - which current
# builds do, so this is normally a no-op. It stays here because an older .deb
# predates that change and would otherwise die before the first frame with
# "Couldn't find a valid ICU package installed on the system".
if [ -f "$RUNTIMECONFIG" ] && grep -q '"System.Globalization.Invariant": *true' "$RUNTIMECONFIG"; then
    echo "   build is invariant-globalization - ICU not required"
else
    ICU_PKG=$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -1)
    if [ -n "$ICU_PKG" ]; then
        echo "   installing $ICU_PKG"
        sudo apt-get install -y "$ICU_PKG"
    else
        echo "   WARNING: no libicuNN package found; if the app exits instantly," >&2
        echo "            install ICU by hand or rebuild with InvariantGlobalization." >&2
    fi
fi

# ---------------------------------------------------------------------------
say "Device group membership for $USER"
# dialout: the serial port to the controller.
# input:   SDL3 reads /dev/input/event* directly for gamepad jogging - it does
#          not go through X, so X having the touchscreen is not enough.
# video,
# render:  DRM nodes, for the GL context behind the toolpath view.
# gpio:    the relay outputs on the 40-pin header.
for GROUP in dialout input video render gpio; do
    if getent group "$GROUP" >/dev/null; then
        sudo adduser "$USER" "$GROUP" >/dev/null && echo "   $GROUP"
    fi
done

# ---------------------------------------------------------------------------
say "Removing things that fight over the serial port"
# ModemManager probes new tty devices and talks AT commands at them, which at
# best eats the first seconds after a controller is plugged in. brltty claims
# several of the USB-serial bridges outright - a CH340 that vanishes moments
# after appearing is almost always brltty. Neither has any use on a CNC panel.
for PKG in modemmanager brltty; do
    if dpkg-query -W -f='${Status}' "$PKG" 2>/dev/null | grep -q "^install ok installed$"; then
        echo "   purging $PKG"
        sudo apt-get purge -y "$PKG"
    else
        echo "   $PKG not installed"
    fi
done

# ---------------------------------------------------------------------------
say "Session files"
# Only back up an .xinitrc that is not one of ours. Comparing against the
# template instead would back up on every run, because the ROTATION line is
# rewritten immediately after the copy.
if [ -f "$HOME/.xinitrc" ] && ! grep -q "GrblHAL Sender appliance" "$HOME/.xinitrc"; then
    cp -a "$HOME/.xinitrc" "$HOME/.xinitrc.bak.$(date +%Y%m%d%H%M%S)"
    echo "   kept your previous .xinitrc as .xinitrc.bak.*"
fi
install -m 0755 "$SCRIPT_DIR/xinitrc" "$HOME/.xinitrc"
sed -i "s/^ROTATION=.*/ROTATION=$ROTATION/" "$HOME/.xinitrc"
echo "   ~/.xinitrc (rotation: $ROTATION)"

# startx from the shell profile rather than a systemd service, because X needs a
# real logind session to be handed DRM master and the input devices. A system
# service starts outside one and fails at exactly that point, in a way whose
# error message points nowhere useful.
#
# The /boot/firmware/no-kiosk check is the recovery hatch: if the app ever comes
# up broken enough that you cannot get to a shell, put the drive in another
# machine and touch that file on the FAT partition to boot to a plain console.
MARK_A="# --- GrblHAL Sender kiosk (setup-kiosk.sh) ---"
MARK_B="# --- end GrblHAL Sender kiosk ---"
PROFILE="$HOME/.bash_profile"
# At login bash reads only the FIRST of .bash_profile, .bash_login and .profile.
# Raspberry Pi OS ships a .profile and no .bash_profile, so creating one here
# would quietly stop .profile being sourced, taking whatever PATH and
# environment the distro put in it with it. Chain to it when creating the file.
if [ ! -s "$PROFILE" ] && [ -f "$HOME/.profile" ]; then
    echo '[ -f "$HOME/.profile" ] && . "$HOME/.profile"' > "$PROFILE"
    echo "   created ~/.bash_profile, chained to your existing ~/.profile"
fi
touch "$PROFILE"
if grep -qF "$MARK_A" "$PROFILE"; then
    # Drop any block a previous run left behind, so re-running does not stack
    # up copies. Matched as whole lines rather than as a sed address range, so
    # nothing in the markers needs escaping.
    awk -v a="$MARK_A" -v b="$MARK_B" '
        $0 == a { skip = 1 }
        !skip   { print }
        $0 == b { skip = 0 }
    ' "$PROFILE" > "$PROFILE.new" && mv "$PROFILE.new" "$PROFILE"
fi
cat >> "$PROFILE" <<PROFILE_BLOCK
$MARK_A
if [ -z "\${DISPLAY:-}" ] && [ "\${XDG_VTNR:-}" = "1" ] && [ ! -e /boot/firmware/no-kiosk ]; then
    exec startx -- -nocursor
fi
$MARK_B
PROFILE_BLOCK
echo "   ~/.bash_profile"

# ---------------------------------------------------------------------------
say "Boot straight to a console login for $USER"
if command -v raspi-config >/dev/null; then
    sudo raspi-config nonint do_boot_behaviour B2   # console autologin
    echo "   console autologin enabled"
else
    echo "   raspi-config not present - set console autologin yourself" >&2
fi

# ---------------------------------------------------------------------------
cat <<'DONE'

Done. Reboot to pick it up: the group changes do not apply to this shell.

    sudo reboot

Getting back to a prompt afterwards:
  - ssh in, or
  - Ctrl+Alt+F2 for a second tty (the kiosk only claims tty1), or
  - touch /boot/firmware/no-kiosk from another machine to boot to a console.

DONE
