#!/usr/bin/env bash
# Publishes the TUI and GUI of Hst Commander for Linux to publish/<runtime>/ for testing.
#
# Linux runtimes are linux-x64, linux-arm64 and linux-arm (32-bit, e.g. 32-bit Raspberry Pi OS), as in the release.
# The apps are published by publish.sh, see there for the output. It can run on any system with bash and the .NET
# SDK, e.g. on the Raspberry Pi itself, or Git Bash on Windows for a 64-bit Raspberry Pi OS with --runtime
# linux-arm64.
#
# Usage: publish-linux.sh [--runtime linux-x64|linux-arm64|linux-arm] [--app all|tui|gui] [--no-smb-plugin]
#   --runtime        defaults to the architecture of the user space when run on Linux (a 32-bit Raspberry Pi OS runs
#                    a 64-bit kernel, so uname -m alone says aarch64), otherwise linux-x64
#   --app            tui, gui or all (default)
#   --no-smb-plugin  leaves out the SMB plugin, like the release packages
#
# Example: ./publish-linux.sh --runtime linux-arm64 --app tui
set -euo pipefail

runtimes="linux-x64 linux-arm64 linux-arm"
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
    runtime=linux-x64
    if [ "$(uname -s)" = Linux ]; then
        case "$(uname -m)" in
            aarch64|arm64)
                if [ "$(getconf LONG_BIT)" = 32 ]; then
                    runtime=linux-arm
                else
                    runtime=linux-arm64
                fi
                ;;
            arm*) runtime=linux-arm ;;
        esac
    fi
fi

case " $runtimes " in
    *" $runtime "*) ;;
    *) echo "--runtime must be one of $runtimes, not $runtime" >&2; exit 2 ;;
esac

# ${args[@]+...} as bash 3.2 of macOS treats an empty array as unset
exec bash "$(dirname "${BASH_SOURCE[0]}")/publish.sh" "$runtime" ${args[@]+"${args[@]}"}
