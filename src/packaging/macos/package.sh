#!/usr/bin/env bash
# Packages a published Hst Commander app for macOS as .dmg: the gui as an app bundle to drag to Applications
# (create-dmg, window with dmg-background.svg), the tui as the executable (hdiutil).
# Runs on macOS (codesign, tiffutil, hdiutil, create-dmg from brew). Apps are signed with the Developer ID identity
# MACOS_SIGN_IDENTITY in the keychain MACOS_KEYCHAIN (set up by .github/scripts/macos-signing-setup.ps1) with the
# hardened runtime notarization requires, else ad hoc, which arm64 requires. The dmg itself isn't signed here.
# usage: package.sh <tui|gui> <osx-x64|osx-arm64> <version> <publish dir> <output dir>
set -euo pipefail

app=$1
rid=$2
version=$3
publish_dir=$(cd "$4" && pwd)
mkdir -p "$5"
output_dir=$(cd "$5" && pwd)
script_dir=$(cd "$(dirname "$0")" && pwd)

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
stage=$work/dmg
mkdir -p "$stage"

# signs a file or bundle with the developer id identity when set up, else ad hoc
sign() {
  if [ -n "${MACOS_SIGN_IDENTITY:-}" ] && [ -n "${MACOS_KEYCHAIN:-}" ]; then
    codesign --force --sign "$MACOS_SIGN_IDENTITY" --keychain "$MACOS_KEYCHAIN" --options runtime --timestamp \
      --entitlements "$script_dir/hst-commander.entitlements" "$1"
  else
    codesign --force --sign - "$1"
  fi
  codesign --verify --strict --verbose=2 "$1"
}

# signs every mach-o binary in a directory (the executable and any native libraries next to it)
sign_binaries() {
  find "$1" -type f -print0 | while IFS= read -r -d '' file; do
    if file -b "$file" | grep -q 'Mach-O'; then
      sign "$file"
    fi
  done
}

case $app in
  tui)
    volume_name="Hst Commander TUI"
    cp -a "$publish_dir/." "$stage/"
    chmod 755 "$stage/hst.commander"
    sign_binaries "$stage"
    ;;
  gui)
    volume_name="Hst Commander"
    bundle=$stage/Hst\ Commander.app
    mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
    cp -a "$publish_dir/." "$bundle/Contents/MacOS/"
    chmod 755 "$bundle/Contents/MacOS/hst.commander.gui"
    cp "$script_dir/../../icon/hst-commander.icns" "$bundle/Contents/Resources/hst-commander.icns"
    cat > "$bundle/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>Hst Commander</string>
  <key>CFBundleDisplayName</key>
  <string>Hst Commander</string>
  <key>CFBundleIdentifier</key>
  <string>com.henrikstengaard.hstcommander</string>
  <key>CFBundleVersion</key>
  <string>$version</string>
  <key>CFBundleShortVersionString</key>
  <string>$version</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleExecutable</key>
  <string>hst.commander.gui</string>
  <key>CFBundleIconFile</key>
  <string>hst-commander.icns</string>
  <key>LSApplicationCategoryType</key>
  <string>public.app-category.utilities</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF
    # binaries inside the bundle first, then the bundle (--deep isn't used, as it doesn't apply entitlements)
    sign_binaries "$bundle/Contents/MacOS"
    sign "$bundle"
    # 1x and 2x (retina) background in one tiff
    tiffutil -cathidpicheck "$script_dir/dmg-background.png" "$script_dir/dmg-background@2x.png" -out "$work/background.tiff"
    ;;
  *) echo "Unknown app '$app'" >&2; exit 1 ;;
esac

# hdiutil (also used by create-dmg) and the finder scripting of create-dmg sometimes fail on build machines, so retry
dmg=$output_dir/hst-commander.$app.$rid.dmg
for attempt in 1 2 3 4 5; do
  rm -f "$dmg" "$output_dir"/rw.*.dmg
  if [ "$app" = gui ]; then
    # icon positions match the arrow in dmg-background.svg
    if create-dmg --volname "$volume_name" --volicon "$script_dir/../../icon/hst-commander.icns" \
      --background "$work/background.tiff" --window-pos 200 120 --window-size 640 400 --icon-size 128 \
      --icon "Hst Commander.app" 160 190 --hide-extension "Hst Commander.app" --app-drop-link 480 190 \
      "$dmg" "$stage"; then
      exit 0
    fi
  elif hdiutil create -volname "$volume_name" -srcfolder "$stage" -ov -format UDZO "$dmg"; then
    exit 0
  fi
  echo "Creating dmg failed (attempt $attempt), retrying"
  sleep 5
done
exit 1
