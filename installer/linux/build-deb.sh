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
cat > "$PKG_DIR/DEBIAN/control" << EOF
Package: $PKG_NAME
Version: $VERSION
Section: electronics
Priority: optional
Architecture: $ARCH
Depends: libx11-6, libfontconfig1, libfreetype6
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
