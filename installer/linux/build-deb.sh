#!/bin/bash
set -euo pipefail

# Usage: ./build-deb.sh <version> <arch> <publish-dir>
# Example: ./build-deb.sh 1.0.0 amd64 ../../publish/linux-x64

VERSION="${1:?Usage: build-deb.sh <version> <arch> <publish-dir>}"
ARCH="${2:?Specify arch: amd64 or arm64}"
PUBLISH_DIR="${3:?Specify publish directory}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

PKG_NAME="grblhal-sender"
PKG_DIR="$REPO_ROOT/artifacts/${PKG_NAME}_${VERSION}_${ARCH}"
ARTIFACTS_DIR="$REPO_ROOT/artifacts"

# Clean previous build
rm -rf "$PKG_DIR"
mkdir -p "$PKG_DIR/DEBIAN"
mkdir -p "$PKG_DIR/usr/lib/$PKG_NAME"
mkdir -p "$PKG_DIR/usr/bin"
mkdir -p "$PKG_DIR/usr/share/applications"
mkdir -p "$PKG_DIR/usr/share/icons/hicolor/256x256/apps"
mkdir -p "$PKG_DIR/usr/share/icons/hicolor/128x128/apps"
mkdir -p "$PKG_DIR/usr/share/icons/hicolor/64x64/apps"
mkdir -p "$PKG_DIR/usr/share/icons/hicolor/48x48/apps"
mkdir -p "$PKG_DIR/usr/share/icons/hicolor/32x32/apps"
mkdir -p "$PKG_DIR/usr/share/icons/hicolor/16x16/apps"

# Copy published files
cp -r "$PUBLISH_DIR"/. "$PKG_DIR/usr/lib/$PKG_NAME/"
chmod +x "$PKG_DIR/usr/lib/$PKG_NAME/GrbLHALSender.Desktop"

# Create symlink launcher
cat > "$PKG_DIR/usr/bin/$PKG_NAME" << 'LAUNCHER'
#!/bin/bash
exec /usr/lib/grblhal-sender/GrbLHALSender.Desktop "$@"
LAUNCHER
chmod +x "$PKG_DIR/usr/bin/$PKG_NAME"

# Copy icons
cp "$REPO_ROOT/icons/icon-256x256.png" "$PKG_DIR/usr/share/icons/hicolor/256x256/apps/$PKG_NAME.png"
cp "$REPO_ROOT/icons/icon-128x128.png" "$PKG_DIR/usr/share/icons/hicolor/128x128/apps/$PKG_NAME.png"
cp "$REPO_ROOT/icons/icon-64x64.png"   "$PKG_DIR/usr/share/icons/hicolor/64x64/apps/$PKG_NAME.png"
cp "$REPO_ROOT/icons/icon-48x48.png"   "$PKG_DIR/usr/share/icons/hicolor/48x48/apps/$PKG_NAME.png"
cp "$REPO_ROOT/icons/icon-32x32.png"   "$PKG_DIR/usr/share/icons/hicolor/32x32/apps/$PKG_NAME.png"
cp "$REPO_ROOT/icons/icon-16x16.png"   "$PKG_DIR/usr/share/icons/hicolor/16x16/apps/$PKG_NAME.png"

# Create .desktop file
cp "$SCRIPT_DIR/grblhal-sender.desktop" "$PKG_DIR/usr/share/applications/"

# Create DEBIAN/control
#
# Depends is the set of shared libraries Avalonia.X11 actually dlopen()s - the
# p/invoke table in Avalonia.X11.dll names libX11, libXext, libXi, libXrandr,
# libXcursor, libXfixes, libICE, libSM and libGL. Only libx11-6 was declared,
# which held up on desktop images where the rest arrive with the desktop anyway
# and fell over on a bare Raspberry Pi OS Lite install: the package installed
# cleanly and the app then died at startup on a missing libXi.
#
# Skia, HarfBuzz and SDL3 are deliberately absent: those ship as native .so
# files inside the publish output (SkiaSharp.NativeAssets.Linux.NoDependencies
# in particular is the fontconfig-free build), so a system copy is never used.
#
# libicu is absent for a different reason - the Desktop project sets
# InvariantGlobalization, so the runtime never goes looking for ICU. Remove
# that property and this line has to grow a libicu dependency again.
#
# libgl1-mesa-dri is a Recommends rather than a Depends: without it libGL
# resolves but there is no hardware driver behind it, and the 3D toolpath falls
# back to the software renderer instead of failing. apt installs Recommends by
# default, so the appliance case gets it and an unusual host can decline it.
cat > "$PKG_DIR/DEBIAN/control" << EOF
Package: $PKG_NAME
Version: $VERSION
Section: electronics
Priority: optional
Architecture: $ARCH
Depends: libx11-6, libxext6, libxi6, libxrandr2, libxcursor1, libxfixes3, libice6, libsm6, libgl1
Recommends: libgl1-mesa-dri
Maintainer: Jay-Tech <jay-tech@users.noreply.github.com>
Description: GrblHAL Sender - Cross-platform G-code sender
 A feature-rich G-code sender application for grblHAL CNC controllers.
 Supports 3D toolpath visualization, gamepad jogging, and serial/network
 communication with grblHAL firmware.
Homepage: https://github.com/Jay-Tech/GrblHAL-Sender
EOF

# Build the .deb
dpkg-deb --build --root-owner-group "$PKG_DIR"

echo "Created: $PKG_DIR.deb"
