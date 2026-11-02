#!/usr/bin/env bash
# Publishes the TUI and GUI of Hst Commander for testing, like build-and-release.yml but without packages.
#
# Shared by publish-windows.sh, publish-macos.sh and publish-linux.sh, which choose the runtime. Each app is
# published self-contained, single-file and ReadyToRun to publish/<runtime>/<app>/ in the repository, the executable
# renamed to hst.commander or hst.commander.gui as in the release packages. Debug symbols are kept for stack traces.
# The SMB plugin, which releases leave out, is added to plugins/Hst.Commander.Plugins.Smb/ next to each app unless
# --no-smb-plugin is given.
#
# Usage: publish.sh <runtime> [--app all|tui|gui] [--no-smb-plugin]
#   <runtime>        .NET runtime identifier, e.g. win-x64, osx-arm64 or linux-arm64
#   --app            tui, gui or all (default)
#   --no-smb-plugin  leaves out the SMB plugin, like the release packages
set -euo pipefail

if [ $# -lt 1 ] || [[ $1 == -* ]]; then
    echo "Usage: publish.sh <runtime> [--app all|tui|gui] [--no-smb-plugin]" >&2
    exit 2
fi

runtime="$1"
shift
app=all
smb_plugin=1
while [ $# -gt 0 ]; do
    case "$1" in
        --app) app="${2:?--app needs tui, gui or all}"; shift 2 ;;
        --no-smb-plugin) smb_plugin=0; shift ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

case "$app" in
    all|tui|gui) ;;
    *) echo "--app must be tui, gui or all, not $app" >&2; exit 2 ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository="$(cd "$script_dir/../.." && pwd)"
publish_root="$repository/publish"
host="$(uname -s)"
extension=""
if [[ $runtime == win-* ]]; then
    extension=".exe"
fi

# dotnet.exe doesn't understand the /c/... paths of Git Bash on Windows
native_path() {
    if command -v cygpath > /dev/null 2>&1; then
        cygpath -m "$1"
    else
        printf '%s\n' "$1"
    fi
}

smb_plugin_dir="$publish_root/smb-plugin/Hst.Commander.Plugins.Smb"
if [ "$smb_plugin" = 1 ]; then
    # managed only, so the same for every runtime
    rm -rf "$smb_plugin_dir"
    echo "Publishing SMB plugin to $smb_plugin_dir"
    dotnet publish "$(native_path "$repository/src/Hst.Commander.Plugins.Smb/Hst.Commander.Plugins.Smb.csproj")" \
        --configuration Release "-p:PublishDir=$(native_path "$smb_plugin_dir")/"
fi

# name, project and executable of each app
for entry in "tui Hst.Commander.Tui hst.commander" "gui Hst.Commander.Gui hst.commander.gui"; do
    read -r name project exe_name <<< "$entry"
    if [ "$app" != all ] && [ "$app" != "$name" ]; then
        continue
    fi

    app_dir="$publish_root/$runtime/$name"
    rm -rf "$app_dir"
    echo "Publishing $name for $runtime to $app_dir"
    dotnet publish "$(native_path "$repository/src/$project/$project.csproj")" \
        --configuration Release -p:PublishSingleFile=True -p:SelfContained=True -p:PublishReadyToRun=True \
        -p:IncludeNativeLibrariesForSelfExtract=true "-p:RuntimeIdentifier=$runtime" \
        "-p:PublishDir=$(native_path "$app_dir")/"

    # the symbols keep their name, as the executable refers to them by it
    exe="$app_dir/$exe_name$extension"
    mv "$app_dir/$project$extension" "$exe"

    if [ "$smb_plugin" = 1 ]; then
        mkdir -p "$app_dir/plugins"
        cp -R "$smb_plugin_dir" "$app_dir/plugins/"
    fi

    if [[ $runtime == osx-* ]] && [ "$host" = Darwin ]; then
        # arm64 runs only signed executables, so sign ad hoc as src/packaging/macos/package.sh does
        codesign --force --sign - "$exe"
    fi

    echo "Published $exe"
done

case "$host" in
    MINGW*|MSYS*|CYGWIN*)
        if [[ $runtime != win-* ]]; then
            echo "Published on Windows: after copying the apps to the $runtime machine, make the executables executable with chmod +x."
        fi
        ;;
esac

if [[ $runtime == osx-* ]] && [ "$host" != Darwin ]; then
    echo "Published on another system: on the Mac, sign the executables ad hoc with codesign --force --sign - <executable>, as arm64 runs only signed executables."
fi
