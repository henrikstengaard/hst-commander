#!/usr/bin/env bash
# Packages a published Hst Commander app for Linux as AppImage and .deb.
# usage: package.sh <tui|gui> <linux-x64|linux-arm64|linux-arm> <version> <publish dir> <output dir>
# appimagetool is used from the APPIMAGETOOL environment variable (default: appimagetool on the path).
set -euo pipefail

app=$1
rid=$2
version=$3
publish_dir=$(realpath "$4")
mkdir -p "$5"
output_dir=$(realpath "$5")
script_dir=$(cd "$(dirname "$0")" && pwd)
icon=$script_dir/../../icon/hst-commander-512x512.png

case $app in
  tui)
    exe=hst.commander
    id=hst-commander
    name="Hst Commander TUI"
    comment="Dual-pane file manager for the terminal"
    terminal=true
    extra_depends=""
    ;;
  gui)
    exe=hst.commander.gui
    id=hst-commander-gui
    name="Hst Commander"
    comment="Dual-pane file manager"
    terminal=false
    extra_depends=", libx11-6, libice6, libsm6, libfontconfig1"
    ;;
  *) echo "Unknown app '$app'" >&2; exit 1 ;;
esac

case $rid in
  linux-x64) appimage_arch=x86_64; deb_arch=amd64 ;;
  linux-arm64) appimage_arch=aarch64; deb_arch=arm64 ;;
  linux-arm) appimage_arch=armhf; deb_arch=armhf ;;
  *) echo "Unknown runtime '$rid'" >&2; exit 1 ;;
esac

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

write_desktop_file() {
  cat > "$1" <<EOF
[Desktop Entry]
Type=Application
Name=$name
Comment=$comment
Exec=$exe
Icon=$id
Terminal=$terminal
Categories=System;FileManager;
EOF
}

# appimage
appdir=$work/AppDir
mkdir -p "$appdir/usr/bin"
cp -a "$publish_dir/." "$appdir/usr/bin/"
chmod 755 "$appdir/usr/bin/$exe"
write_desktop_file "$appdir/$id.desktop"
cp "$icon" "$appdir/$id.png"
cat > "$appdir/AppRun" <<EOF
#!/bin/sh
here=\$(dirname "\$(readlink -f "\$0")")
exec "\$here/usr/bin/$exe" "\$@"
EOF
chmod 755 "$appdir/AppRun"
ARCH=$appimage_arch "${APPIMAGETOOL:-appimagetool}" --no-appstream "$appdir" "$output_dir/hst-commander.$app.$rid.AppImage"

# deb
root=$work/deb
mkdir -p "$root/DEBIAN" "$root/usr/lib/$id" "$root/usr/bin" "$root/usr/share/applications" "$root/usr/share/icons/hicolor/512x512/apps"
cp -a "$publish_dir/." "$root/usr/lib/$id/"
chmod 755 "$root/usr/lib/$id/$exe"
ln -s "../lib/$id/$exe" "$root/usr/bin/$exe"
write_desktop_file "$root/usr/share/applications/$id.desktop"
cp "$icon" "$root/usr/share/icons/hicolor/512x512/apps/$id.png"
chmod 644 "$root/usr/share/applications/$id.desktop" "$root/usr/share/icons/hicolor/512x512/apps/$id.png"
installed_size=$(du -sk "$root/usr" | cut -f1)
cat > "$root/DEBIAN/control" <<EOF
Package: $id
Version: $version
Architecture: $deb_arch
Maintainer: Henrik Nørfjand Stengaard
Installed-Size: $installed_size
Depends: libc6, libgcc-s1, libstdc++6, libicu78 | libicu77 | libicu76 | libicu75 | libicu74 | libicu73 | libicu72 | libicu71 | libicu70 | libicu69 | libicu68 | libicu67$extra_depends
Section: utils
Priority: optional
Homepage: https://github.com/henrikstengaard/hst-commander
Description: $name - $comment
 Dual-pane file manager inspired by Norton Commander and Midnight Commander,
 browsing local directories and images like ISO 9660 in the same panes.
EOF
dpkg-deb -Zxz --build --root-owner-group "$root" "$output_dir/hst-commander.$app.linux-$deb_arch.deb"
