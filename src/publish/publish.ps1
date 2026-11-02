<#
.SYNOPSIS
    Publishes the TUI and GUI of Hst Commander for testing, like build-and-release.yml but without packages.

.DESCRIPTION
    Shared by publish-windows.ps1, publish-macos.ps1 and publish-linux.ps1, which choose the runtime. Each app is
    published self-contained, single-file and ReadyToRun to publish/<runtime>/<app>/ in the repository, the executable
    renamed to hst.commander or hst.commander.gui as in the release packages. Debug symbols are kept for stack traces.
    The SMB plugin, which releases leave out, is added to plugins/Hst.Commander.Plugins.Smb/ next to each app unless
    -NoSmbPlugin is given.

.PARAMETER Runtime
    .NET runtime identifier, e.g. win-x64, osx-arm64 or linux-arm64.

.PARAMETER App
    tui, gui or all (default).

.PARAMETER NoSmbPlugin
    Leaves out the SMB plugin, like the release packages.
#>
param(
    [Parameter(Mandatory)]
    [string]$Runtime,

    [ValidateSet('all', 'tui', 'gui')]
    [string]$App = 'all',

    [switch]$NoSmbPlugin
)

$ErrorActionPreference = 'Stop'

$repository = Resolve-Path (Join-Path $PSScriptRoot '../..')
$publishRoot = Join-Path $repository 'publish'
$apps = @(
    @{ Name = 'tui'; Project = 'Hst.Commander.Tui'; Exe = 'hst.commander' },
    @{ Name = 'gui'; Project = 'Hst.Commander.Gui'; Exe = 'hst.commander.gui' }
) | Where-Object { $App -eq 'all' -or $_.Name -eq $App }
$extension = if ($Runtime -like 'win-*') { '.exe' } else { '' }
# $IsWindows is only defined in PowerShell 6 and later
$onWindows = $PSVersionTable.PSEdition -eq 'Desktop' -or $IsWindows

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE"
    }
}

$smbPluginDir = $null
if (-not $NoSmbPlugin) {
    # managed only, so the same for every runtime
    $smbPluginDir = Join-Path $publishRoot 'smb-plugin/Hst.Commander.Plugins.Smb'
    if (Test-Path $smbPluginDir) {
        Remove-Item -Recurse -Force $smbPluginDir
    }

    Write-Host "Publishing SMB plugin to $smbPluginDir"
    Invoke-DotNet publish (Join-Path $repository 'src/Hst.Commander.Plugins.Smb/Hst.Commander.Plugins.Smb.csproj') `
        --configuration Release "-p:PublishDir=$smbPluginDir/"
}

foreach ($a in $apps) {
    $appDir = Join-Path $publishRoot "$Runtime/$($a.Name)"
    if (Test-Path $appDir) {
        Remove-Item -Recurse -Force $appDir
    }

    Write-Host "Publishing $($a.Name) for $Runtime to $appDir"
    Invoke-DotNet publish (Join-Path $repository "src/$($a.Project)/$($a.Project).csproj") `
        --configuration Release '-p:PublishSingleFile=True' '-p:SelfContained=True' '-p:PublishReadyToRun=True' `
        '-p:IncludeNativeLibrariesForSelfExtract=true' "-p:RuntimeIdentifier=$Runtime" "-p:PublishDir=$appDir/"

    # the symbols keep their name, as the executable refers to them by it
    $exe = Join-Path $appDir "$($a.Exe)$extension"
    Move-Item -Path (Join-Path $appDir "$($a.Project)$extension") -Destination $exe

    if ($smbPluginDir) {
        $pluginsDir = Join-Path $appDir 'plugins'
        New-Item -ItemType Directory -Force $pluginsDir | Out-Null
        Copy-Item -Recurse -Path $smbPluginDir -Destination $pluginsDir
    }

    if ($Runtime -like 'osx-*' -and $IsMacOS) {
        # arm64 runs only signed executables, so sign ad hoc as src/packaging/macos/package.sh does
        & codesign --force --sign - $exe
        if ($LASTEXITCODE -ne 0) {
            throw "codesign of $exe failed"
        }
    }

    Write-Host "Published $exe"
}

if ($Runtime -notlike 'win-*' -and $onWindows) {
    Write-Host "Published on Windows: after copying the apps to the $Runtime machine, make the executables executable with chmod +x."
}

if ($Runtime -like 'osx-*' -and -not $IsMacOS) {
    Write-Host 'Published on another system: on the Mac, sign the executables ad hoc with codesign --force --sign - <executable>, as arm64 runs only signed executables.'
}
