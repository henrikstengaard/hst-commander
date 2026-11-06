#!/usr/bin/env bash
# Publishes the TUI and GUI of Hst Commander for macOS and packages them as .dmg for testing, like the release: the
# GUI as an app bundle to drag to Applications, the TUI as the executable.
#
# Runs on macOS only (codesign, tiffutil, hdiutil), and the GUI needs create-dmg (brew install create-dmg). The apps
# are published by publish-macos.sh to publish/<runtime>/ and packaged by src/packaging/macos/package.sh to
# publish/<runtime>/dmg/hst-commander.<app>.<runtime>.dmg. The version is the one of Directory.Build.props with the
# git commit count as its last part, as in the release. Unlike the release, the SMB plugin is included unless
# --no-smb-plugin is given.
#
# Usage: publish-macos-dmg.sh [--runtime osx-x64|osx-arm64] [--app all|tui|gui] [--no-smb-plugin]
#   --runtime        defaults to the architecture of the Mac (also from a shell running with Rosetta)
#   --app            tui, gui or all (default)
#   --no-smb-plugin  leaves out the SMB plugin, like the release packages
#
# Example: ./publish-macos-dmg.sh --app gui
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repository=$(cd "$script_dir/../.." && pwd)

runtime=""
app=all
args=()
while [ $# -gt 0 ]; do
    case "$1" in
        --runtime|-r) runtime="${2:?--runtime needs a runtime: osx-x64 osx-arm64}"; shift 2 ;;
        --app) app="${2:?--app needs tui, gui or all}"; shift 2 ;;
        -h|--help) sed -n '2,/^set -euo/{/^set -euo/d;s/^# \{0,1\}//;p;}' "$0"; exit 0 ;;
        *) args+=("$1"); shift ;;
    esac
done

if [ "$(uname -s)" != Darwin ]; then
    echo "Making .dmg packages needs macOS (codesign, tiffutil, hdiutil); use publish-macos.sh on other systems" >&2
    exit 1
fi

case "$app" in
    all) apps="tui gui" ;;
    tui|gui) apps=$app ;;
    *) echo "--app must be tui, gui or all, not $app" >&2; exit 2 ;;
esac

if [ "$app" != tui ] && ! command -v create-dmg > /dev/null; then
    echo "Packaging the GUI needs create-dmg: brew install create-dmg" >&2
    exit 1
fi

if [ -z "$runtime" ]; then
    if [ "$(sysctl -n hw.optional.arm64 2> /dev/null || echo 0)" = 1 ]; then
        runtime=osx-arm64
    else
        runtime=osx-x64
    fi
fi

# publish-macos.sh checks the runtime; ${args[@]+...} as bash 3.2 of macOS treats an empty array as unset
bash "$script_dir/publish-macos.sh" --runtime "$runtime" --app "$app" ${args[@]+"${args[@]}"}

# the version of Directory.Build.props with the git commit count as its last part, as build-and-release.yml does
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$repository/src/Directory.Build.props" | head -n 1)
if commit_count=$(git -C "$repository" rev-list --count HEAD 2> /dev/null); then
    version="${version%.*}.$commit_count"
fi

output_dir="$repository/publish/$runtime/dmg"
rm -rf "$output_dir"
for name in $apps; do
    echo "Packaging $name $version as .dmg"
    bash "$repository/src/packaging/macos/package.sh" "$name" "$runtime" "$version" \
        "$repository/publish/$runtime/$name" "$output_dir"
done

echo "Packages in $output_dir:"
ls -1 "$output_dir"/*.dmg
