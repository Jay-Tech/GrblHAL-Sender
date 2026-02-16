#!/bin/bash
set -euo pipefail

# Usage: ./build-dmg.sh <version> <arch> <publish-dir>
# Example: ./build-dmg.sh 1.0.0 x64 ../../publish/osx-x64

VERSION="${1:?Usage: build-dmg.sh <version> <arch> <publish-dir>}"
ARCH="${2:?Specify arch: x64 or arm64}"
PUBLISH_DIR="${3:?Specify publish directory}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

APP_NAME="GrblHAL Sender"
BUNDLE_ID="com.jaytech.grblhal-sender"
APP_DIR="$REPO_ROOT/artifacts/${APP_NAME}.app"
ARTIFACTS_DIR="$REPO_ROOT/artifacts"

mkdir -p "$ARTIFACTS_DIR"

# Clean previous build
rm -rf "$APP_DIR"

# Create .app bundle structure
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

# Copy published files
cp -r "$PUBLISH_DIR"/. "$APP_DIR/Contents/MacOS/"
chmod +x "$APP_DIR/Contents/MacOS/GrbLHALSender.Desktop"

# Copy .icns icon
cp "$REPO_ROOT/icons/GHalSender.icns" "$APP_DIR/Contents/Resources/GHalSender.icns"

# Create Info.plist
cat > "$APP_DIR/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>${APP_NAME}</string>
    <key>CFBundleDisplayName</key>
    <string>${APP_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>${BUNDLE_ID}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>GrbLHALSender.Desktop</string>
    <key>CFBundleIconFile</key>
    <string>GHalSender</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSHumanReadableCopyright</key>
    <string>Copyright Jay-Tech</string>
</dict>
</plist>
EOF

# Create DMG using create-dmg (installed via brew)
DMG_FILENAME="GrblHALSender-${VERSION}-osx-${ARCH}.dmg"

create-dmg \
  --volname "${APP_NAME}" \
  --volicon "$REPO_ROOT/icons/GHalSender.icns" \
  --window-pos 200 120 \
  --window-size 600 400 \
  --icon-size 100 \
  --icon "${APP_NAME}.app" 150 185 \
  --hide-extension "${APP_NAME}.app" \
  --app-drop-link 450 185 \
  --no-internet-enable \
  "$ARTIFACTS_DIR/$DMG_FILENAME" \
  "$APP_DIR"

echo "Created: $ARTIFACTS_DIR/$DMG_FILENAME"
