#!/usr/bin/env bash
# Publishes the TUI and GUI of Hst Commander for macOS and packages them as .dmg for testing, the same way as the
# build-macos job of .github/workflows/build-and-release.yml: the GUI as an app bundle to drag to Applications, the TUI
# as the executable.
#
# Like the workflow, each app is published with dotnet publish (Release, self-contained, single-file, ReadyToRun) with
# the version of Directory.Build.props with the git commit count as its last part, the debug symbols are removed, the
# executable is renamed to hst.commander or hst.commander.gui, and src/packaging/macos/package.sh makes the .dmg. So
# the SMB plugin isn't included, as in the release. Apps are published to publish/<runtime>/dmg-publish/<app>/ and the
# packages are written to publish/<runtime>/dmg/hst-commander.<app>.<runtime>.dmg, both deleted first.
#
# Runs on macOS only (codesign, tiffutil, hdiutil), and the GUI needs create-dmg (brew install create-dmg).
#
# Usage: publish-macos-dmg.sh [--runtime osx-x64|osx-arm64] [--app all|tui|gui]
#   --runtime  defaults to the architecture of the Mac (also from a shell running with Rosetta)
#   --app      tui, gui or all (default)
#
# Example: ./publish-macos-dmg.sh --app gui
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repository=$(cd "$script_dir/../.." && pwd)

# PUBLISH_ARGS of build-and-release.yml
publish_args=(--configuration Release -p:PublishSingleFile=True -p:SelfContained=True -p:PublishReadyToRun=True
    -p:IncludeNativeLibrariesForSelfExtract=true)

runtime=""
app=all
while [ $# -gt 0 ]; do
    case "$1" in
        --runtime|-r) runtime="${2:?--runtime needs a runtime: osx-x64 osx-arm64}"; shift 2 ;;
        --app) app="${2:?--app needs tui, gui or all}"; shift 2 ;;
        -h|--help) sed -n '2,/^set -euo/{/^set -euo/d;s/^# \{0,1\}//;p;}' "$0"; exit 0 ;;
        *) echo "Unknown option $1, see --help" >&2; exit 2 ;;
    esac
done

if [ "$(uname -s)" != Darwin ]; then
    echo "Making .dmg packages needs macOS (codesign, tiffutil, hdiutil); use publish-macos.sh on other systems" >&2
    exit 1
fi

case "$app" in
    all|tui|gui) ;;
    *) echo "--app must be tui, gui or all, not $app" >&2; exit 2 ;;
esac

if [ -z "$runtime" ]; then
    if [ "$(sysctl -n hw.optional.arm64 2> /dev/null || echo 0)" = 1 ]; then
        runtime=osx-arm64
    else
        runtime=osx-x64
    fi
fi

case "$runtime" in
    osx-x64|osx-arm64) ;;
    *) echo "--runtime must be osx-x64 or osx-arm64, not $runtime" >&2; exit 2 ;;
esac

if [ "$app" != tui ] && ! command -v create-dmg > /dev/null; then
    echo "Packaging the GUI needs create-dmg: brew install create-dmg" >&2
    exit 1
fi

# build_version of the workflow's version job: the version of Directory.Build.props with the git commit count as its
# last part; passed to dotnet publish, so Directory.Build.targets doesn't replace it again
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$repository/src/Directory.Build.props" | head -n 1)
commit_count=$(git -C "$repository" rev-list --count HEAD)
build_version=$(echo "$version" | sed -E "s/^(.*)\.[0-9]+.*$/\1.$commit_count/")

publish_root="$repository/publish/$runtime/dmg-publish"
output_dir="$repository/publish/$runtime/dmg"
rm -rf "$publish_root" "$output_dir"

for entry in "tui Hst.Commander.Tui hst.commander" "gui Hst.Commander.Gui hst.commander.gui"; do
    read -r name project exe <<< "$entry"
    if [ "$app" != all ] && [ "$app" != "$name" ]; then
        continue
    fi

    # "Build app" step
    publish_dir="$publish_root/$name"
    echo "Publishing $name $build_version for $runtime to $publish_dir"
    dotnet publish "$repository/src/$project/$project.csproj" "${publish_args[@]}" "-p:Version=$build_version" \
        "-p:RuntimeIdentifier=$runtime" "-p:PublishDir=$publish_dir/"

    # "Prepare app output" step: remove the debug symbols and rename the executable
    rm -f "$publish_dir"/*.pdb
    executable=""
    for file in "$publish_dir/$project"*; do
        if [ -e "$file" ]; then
            executable=$file
            break
        fi
    done
    if [ -z "$executable" ]; then
        echo "No executable $project found in $publish_dir" >&2
        exit 1
    fi
    executable_name=$(basename "$executable")
    mv "$executable" "$publish_dir/${executable_name/$project/$exe}"

    # "Build dmg" step
    echo "Packaging $name $build_version as .dmg"
    bash "$repository/src/packaging/macos/package.sh" "$name" "$runtime" "$build_version" "$publish_dir" "$output_dir"
done

echo "Packages in $output_dir:"
ls -1 "$output_dir"/*.dmg
