<#
.SYNOPSIS
    Publishes the TUI and GUI of Hst Commander for macOS to publish/<runtime>/ for testing.

.DESCRIPTION
    macOS runtimes are osx-x64 and osx-arm64, as in the release. Published on a Mac, the executables are signed ad
    hoc, as arm64 runs only signed executables. The apps are published by publish.ps1, see there for the output. It
    can run on any system with the .NET SDK, e.g. on Windows for a Mac with -Runtime osx-arm64 (then sign them on
    the Mac).

.PARAMETER Runtime
    osx-x64 or osx-arm64. Defaults to the architecture of the Mac when run on macOS, otherwise osx-arm64.

.PARAMETER App
    tui, gui or all (default).

.PARAMETER NoSmbPlugin
    Leaves out the SMB plugin, like the release packages.

.EXAMPLE
    ./publish-macos.ps1 -Runtime osx-x64
#>
param(
    [ValidateSet('osx-x64', 'osx-arm64')]
    [string]$Runtime,

    [ValidateSet('all', 'tui', 'gui')]
    [string]$App = 'all',

    [switch]$NoSmbPlugin
)

$ErrorActionPreference = 'Stop'

if (-not $Runtime) {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $Runtime = if ($IsMacOS -and $architecture -eq 'X64') { 'osx-x64' } else { 'osx-arm64' }
}

& (Join-Path $PSScriptRoot 'publish.ps1') -Runtime $Runtime -App $App -NoSmbPlugin:$NoSmbPlugin
