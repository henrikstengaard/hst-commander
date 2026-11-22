# Installs the Hst Commander TUI for the current user on Windows from a GitHub release, and adds it to the user's
# path, e.g. in PowerShell:
#   irm https://github.com/henrikstengaard/hst-commander/releases/latest/download/install.ps1 | iex
# With options:
#   & ([scriptblock]::Create((irm https://github.com/henrikstengaard/hst-commander/releases/latest/download/install.ps1))) -Smb
#
# Options:
#   -Smb              also installs the SMB plugin
#   -SmbOnly          only installs the SMB plugin (e.g. for the GUI)
#   -Version <tag>    installs the release with the tag, e.g. 1.0.190 (also a prerelease), instead of the latest
#   -Uninstall        removes the TUI and its path entry (the settings and plugins in ~\.hst-commander are kept)
#
# The TUI is installed in %LOCALAPPDATA%\hst-commander\tui, which is added to the user's path. The SMB plugin is
# installed in ~\.hst-commander\plugins. Installing again updates them. Runs in Windows PowerShell 5.1 and PowerShell 7;
# it never exits the shell it's run in (with iex), errors are thrown.
param(
    [switch]$Smb,
    [switch]$SmbOnly,
    [string]$Version,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
# the progress bar makes downloads slow in Windows PowerShell 5.1
$ProgressPreference = 'SilentlyContinue'

function Install-HstCommander
{
    param([bool]$InstallTui, [bool]$InstallSmb, [string]$Tag, [bool]$Remove)

    $repository = 'henrikstengaard/hst-commander'
    $installDir = Join-Path $env:LOCALAPPDATA 'hst-commander\tui'
    $exe = 'hst.commander.exe'
    $pluginsDir = Join-Path $env:USERPROFILE '.hst-commander\plugins'
    $pluginName = 'Hst.Commander.Plugins.Smb'

    if ($env:OS -ne 'Windows_NT')
    {
        throw 'install.ps1 is for Windows; on macOS and Linux run install.sh with bash'
    }

    if ($Remove)
    {
        Set-UserPath -Remove $installDir | Out-Null
        if (Test-Path $installDir)
        {
            Remove-Item -Recurse -Force $installDir
        }
        Write-Host 'Hst Commander TUI has been uninstalled. Settings and plugins in ~\.hst-commander are kept.'
        return
    }

    # github needs tls 1.2, which Windows PowerShell 5.1 doesn't always use by default
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    # the architecture of Windows, also when PowerShell runs emulated (x64 on arm64) or as a 32-bit process
    $arch = $env:PROCESSOR_ARCHITEW6432
    if (-not $arch)
    {
        $arch = $env:PROCESSOR_ARCHITECTURE
    }
    $arch = switch ($arch)
    {
        'AMD64' { 'x64' }
        'ARM64' { 'arm64' }
        'x86' { 'x86' }
        default { throw "Hst Commander isn't released for $arch" }
    }

    $releaseUrl = if ($Tag) { "https://api.github.com/repos/$repository/releases/tags/$Tag" } else { "https://api.github.com/repos/$repository/releases/latest" }
    try
    {
        $release = Invoke-RestMethod -Uri $releaseUrl -UseBasicParsing
    }
    catch
    {
        throw "Release $(if ($Tag) { $Tag } else { 'latest' }) wasn't found ($releaseUrl): $($_.Exception.Message)"
    }
    $Tag = $release.tag_name

    $work = Join-Path ([IO.Path]::GetTempPath()) ('hst-commander-install-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try
    {
        if ($InstallTui)
        {
            $asset = Get-ReleaseAsset $release "_tui_windows_$($arch)_portable.zip"
            Write-Host "Downloading Hst Commander TUI $Tag for Windows $arch"
            $zip = Join-Path $work 'tui.zip'
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing
            $files = Join-Path $work 'tui'
            Expand-Archive -Path $zip -DestinationPath $files
            if (-not (Test-Path (Join-Path $files $exe)))
            {
                throw "$($asset.name) has no $exe"
            }

            if (Test-Path $installDir)
            {
                try
                {
                    Remove-Item -Recurse -Force $installDir
                }
                catch
                {
                    throw "Hst Commander TUI in $installDir couldn't be replaced, quit it and install again: $($_.Exception.Message)"
                }
            }
            New-Item -ItemType Directory -Force -Path (Split-Path $installDir) | Out-Null
            Move-Item -Path $files -Destination $installDir
            Write-Host "Installed Hst Commander TUI $Tag in $installDir"
            if (Set-UserPath -Add $installDir)
            {
                Write-Host "Added $installDir to the path"
            }
        }

        if ($InstallSmb)
        {
            $asset = Get-ReleaseAsset $release '_smb-plugin.zip'
            Write-Host "Downloading the SMB plugin $Tag"
            $zip = Join-Path $work 'smb-plugin.zip'
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -UseBasicParsing
            $files = Join-Path $work 'smb'
            Expand-Archive -Path $zip -DestinationPath $files
            $pluginFiles = Join-Path $files $pluginName
            if (-not (Test-Path (Join-Path $pluginFiles "$pluginName.dll")))
            {
                throw "$($asset.name) has no $pluginName"
            }
            # the source of SMBLibrary is kept with the plugin
            Get-ChildItem -Path $files -Filter 'SMBLibrary-source-*.zip' | Move-Item -Destination $pluginFiles

            $pluginDir = Join-Path $pluginsDir $pluginName
            if (Test-Path $pluginDir)
            {
                try
                {
                    Remove-Item -Recurse -Force $pluginDir
                }
                catch
                {
                    throw "The SMB plugin in $pluginDir couldn't be replaced, quit Hst Commander and install again: $($_.Exception.Message)"
                }
            }
            New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null
            Move-Item -Path $pluginFiles -Destination $pluginDir
            Write-Host "Installed the SMB plugin $Tag in $pluginDir"
        }
    }
    finally
    {
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }

    if ($InstallTui)
    {
        Write-Host 'Start it with hst.commander (open a new terminal for the path to take effect)'
    }
}

# the release's file ending with the suffix
function Get-ReleaseAsset
{
    param($Release, [string]$Suffix)

    $asset = $Release.assets | Where-Object { $_.name.EndsWith($Suffix) } | Select-Object -First 1
    if (-not $asset)
    {
        throw "Release $($Release.tag_name) has no file ending with '$Suffix'"
    }
    $asset
}

# adds a directory to or removes it from the user's path in the registry (keeping its unexpanded variables, like
# %USERPROFILE%) and in this session, and tells running programs (e.g. Explorer) the environment changed. Returns
# whether the path changed.
function Set-UserPath
{
    param([string]$Add, [string]$Remove)

    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    try
    {
        $path = [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $entries = @($path -split ';' | Where-Object { $_ })
        $directory = if ($Add) { $Add } else { $Remove }
        $others = @($entries | Where-Object { $_.TrimEnd('\') -ne $directory.TrimEnd('\') })
        $newEntries = if ($Add) { $others + $directory } else { $others }
        if ($newEntries.Count -eq $entries.Count)
        {
            return $false
        }
        $key.SetValue('Path', ($newEntries -join ';'), [Microsoft.Win32.RegistryValueKind]::ExpandString)
    }
    finally
    {
        $key.Close()
    }

    $sessionEntries = @($env:Path -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ne $directory.TrimEnd('\') })
    if ($Add)
    {
        $sessionEntries += $directory
    }
    $env:Path = $sessionEntries -join ';'

    if (-not ('HstCommanderInstall.Environment' -as [type]))
    {
        Add-Type -Namespace HstCommanderInstall -Name Environment -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags,
    uint timeout, out UIntPtr result);
'@
    }
    $result = [UIntPtr]::Zero
    # HWND_BROADCAST, WM_SETTINGCHANGE, SMTO_ABORTIFHUNG
    [HstCommanderInstall.Environment]::SendMessageTimeout([IntPtr]0xffff, 0x1a, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$result) | Out-Null
    return $true
}

Install-HstCommander -InstallTui (-not $SmbOnly) -InstallSmb ($Smb -or $SmbOnly) -Tag $Version -Remove $Uninstall
