<#
.SYNOPSIS
    Publishes the TUI and GUI of Hst Commander for Windows to publish/<runtime>/ for testing.

.DESCRIPTION
    Windows runtimes are win-x64, win-x86 and win-arm64, as in the release. The apps are published by publish.ps1,
    see there for the output. It can run on any system with the .NET SDK, e.g. on Linux for a Windows machine with
    -Runtime win-x64.

.PARAMETER Runtime
    win-x64, win-x86 or win-arm64. Defaults to the architecture of Windows when run on Windows, otherwise win-x64.

.PARAMETER App
    tui, gui or all (default).

.PARAMETER NoSmbPlugin
    Leaves out the SMB plugin, like the release packages.

.EXAMPLE
    ./publish-windows.ps1 -Runtime win-arm64 -App gui
#>
param(
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$Runtime,

    [ValidateSet('all', 'tui', 'gui')]
    [string]$App = 'all',

    [switch]$NoSmbPlugin
)

$ErrorActionPreference = 'Stop'

if (-not $Runtime) {
    # $IsWindows is only defined in PowerShell 6 and later
    $onWindows = $PSVersionTable.PSEdition -eq 'Desktop' -or $IsWindows
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $Runtime = if ($onWindows -and $architecture -eq 'Arm64') { 'win-arm64' }
        elseif ($onWindows -and $architecture -eq 'X86') { 'win-x86' }
        else { 'win-x64' }
}

& (Join-Path $PSScriptRoot 'publish.ps1') -Runtime $Runtime -App $App -NoSmbPlugin:$NoSmbPlugin
