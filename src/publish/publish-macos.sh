#!/usr/bin/env bash
# Publishes the TUI and GUI of Hst Commander for macOS to publish/<runtime>/ for testing.
#
# macOS runtimes are osx-x64 and osx-arm64, as in the release. Published on a Mac, the executables are signed ad
# hoc, as arm64 runs only signed executables. The apps are published by publish.sh, see there for the output. It can
# run on any system with bash and the .NET SDK, e.g. on Linux for a Mac with --runtime osx-arm64 (then sign them on
# the Mac).
#
# Usage: publish-macos.sh [--runtime osx-x64|osx-arm64] [--app all|tui|gui] [--no-smb-plugin]
#   --runtime        defaults to the architecture of the Mac when run on macOS (also from a shell running with
#                    Rosetta), otherwise osx-arm64
#   --app            tui, gui or all (default)
#   --no-smb-plugin  leaves out the SMB plugin, like the release packages
#
# Example: ./publish-macos.sh --runtime osx-x64
set -euo pipefail

runtimes="osx-x64 osx-arm64"
runtime=""
args=()
while [ $# -gt 0 ]; do
    case "$1" in
        --runtime|-r) runtime="${2:?--runtime needs a runtime: $runtimes}"; shift 2 ;;
        -h|--help) sed -n '2,/^set -euo/{/^set -euo/d;s/^# \{0,1\}//;p;}' "$0"; exit 0 ;;
        *) args+=("$1"); shift ;;
    esac
done

if [ -z "$runtime" ]; then
    if [ "$(uname -s)" = Darwin ] && [ "$(sysctl -n hw.optional.arm64 2> /dev/null || echo 0)" != 1 ]; then
        runtime=osx-x64
    else
        runtime=osx-arm64
    fi
fi

case " $runtimes " in
    *" $runtime "*) ;;
    *) echo "--runtime must be one of $runtimes, not $runtime" >&2; exit 2 ;;
esac

# ${args[@]+...} as bash 3.2 of macOS treats an empty array as unset
exec bash "$(dirname "${BASH_SOURCE[0]}")/publish.sh" "$runtime" ${args[@]+"${args[@]}"}
