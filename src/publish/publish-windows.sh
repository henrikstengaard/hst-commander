#!/usr/bin/env bash
# Publishes the TUI and GUI of Hst Commander for Windows to publish/<runtime>/ for testing.
#
# Windows runtimes are win-x64, win-x86 and win-arm64, as in the release. The apps are published by publish.sh, see
# there for the output. It can run on any system with bash and the .NET SDK, e.g. Git Bash on Windows or Linux for a
# Windows machine with --runtime win-x64.
#
# Usage: publish-windows.sh [--runtime win-x64|win-x86|win-arm64] [--app all|tui|gui] [--no-smb-plugin]
#   --runtime        defaults to the architecture of Windows when run on Windows (Git Bash), otherwise win-x64
#   --app            tui, gui or all (default)
#   --no-smb-plugin  leaves out the SMB plugin, like the release packages
#
# Example: ./publish-windows.sh --runtime win-arm64 --app gui
set -euo pipefail

runtimes="win-x64 win-x86 win-arm64"
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
    case "$(uname -s)" in
        MINGW*|MSYS*|CYGWIN*)
            # the architecture of Windows, also from a 32-bit shell
            case "${PROCESSOR_ARCHITEW6432:-${PROCESSOR_ARCHITECTURE:-AMD64}}" in
                ARM64) runtime=win-arm64 ;;
                x86) runtime=win-x86 ;;
                *) runtime=win-x64 ;;
            esac
            ;;
        *) runtime=win-x64 ;;
    esac
fi

case " $runtimes " in
    *" $runtime "*) ;;
    *) echo "--runtime must be one of $runtimes, not $runtime" >&2; exit 2 ;;
esac

# ${args[@]+...} as bash 3.2 of macOS treats an empty array as unset
exec bash "$(dirname "${BASH_SOURCE[0]}")/publish.sh" "$runtime" ${args[@]+"${args[@]}"}
