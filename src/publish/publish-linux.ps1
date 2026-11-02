<#
.SYNOPSIS
    Publishes the TUI and GUI of Hst Commander for Linux to publish/<runtime>/ for testing.

.DESCRIPTION
    Linux runtimes are linux-x64, linux-arm64 and linux-arm (32-bit, e.g. 32-bit Raspberry Pi OS), as in the
    release. The apps are published by publish.ps1, see there for the output. It can run on any system with the .NET
    SDK, e.g. on Windows for a 64-bit Raspberry Pi OS with -Runtime linux-arm64.

.PARAMETER Runtime
    linux-x64, linux-arm64 or linux-arm. Defaults to the architecture of the user space when run on Linux (a 32-bit
    Raspberry Pi OS runs a 64-bit kernel, so it's the process's architecture), otherwise linux-x64.

.PARAMETER App
    tui, gui or all (default).

.PARAMETER NoSmbPlugin
    Leaves out the SMB plugin, like the release packages.

.EXAMPLE
    ./publish-linux.ps1 -Runtime linux-arm64 -App tui
#>
param(
    [ValidateSet('linux-x64', 'linux-arm64', 'linux-arm')]
    [string]$Runtime,

    [ValidateSet('all', 'tui', 'gui')]
    [string]$App = 'all',

    [switch]$NoSmbPlugin
)

$ErrorActionPreference = 'Stop'

if (-not $Runtime) {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
    $Runtime = if ($IsLinux -and $architecture -eq 'Arm64') { 'linux-arm64' }
        elseif ($IsLinux -and $architecture -eq 'Arm') { 'linux-arm' }
        else { 'linux-x64' }
}

& (Join-Path $PSScriptRoot 'publish.ps1') -Runtime $Runtime -App $App -NoSmbPlugin:$NoSmbPlugin
