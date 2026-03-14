<##
Install-VolumeAssistantApp.ps1

Publishes the VolumeAssistant.App (system tray) project, copies the published files to an
install directory (preserving existing appsettings.json), and creates a Start Menu shortcut
plus an optional startup entry so the tray app launches automatically on Windows login.

Usage examples:

    .\scripts\Install-VolumeAssistantApp.ps1
    .\scripts\Install-VolumeAssistantApp.ps1 -InstallDir "C:\Program Files\VolumeAssistantApp" -AddStartup $true
#>

param(
    [string]$ProjectPath = "src\VolumeAssistant.App",
    [string]$Configuration = "Release",
    [string]$InstallDir = "$env:ProgramFiles\VolumeAssistantApp",
    [string]$AppName = "VolumeAssistant",
    # Publish RID for self-contained publish
    [string]$Runtime = "win-x64",
    # Publish self-contained by default
    [bool]$SelfContained = $true,
    # Add a registry run key to start the tray app on Windows login
    [bool]$AddStartup = $false
)

function Ensure-Administrator {
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
    if (-not $isAdmin) {
        Write-Error "This script must be run as Administrator. Right-click and 'Run as administrator'."
        exit 1
    }
}

function Write-Info([string]$text) { Write-Host $text -ForegroundColor Cyan }

Ensure-Administrator

$scriptRoot = Split-Path -Path $MyInvocation.MyCommand.Definition -Parent

# Resolve project path
$candidates = @()
$candidates += $ProjectPath
$repoRoot = Split-Path -Path $scriptRoot -Parent
if ($repoRoot) { $candidates += Join-Path $repoRoot $ProjectPath }
$candidates += Join-Path $scriptRoot $ProjectPath

$found = $null
foreach ($cand in $candidates) {
    if (Test-Path $cand) { $found = (Resolve-Path -Path $cand).ProviderPath; break }
}

if ($found) {
    $ProjectPath = $found
} else {
    Write-Error "Project path '$ProjectPath' not found. Tried: $($candidates -join ', ')"
    exit 1
}

# Find the first .csproj under the project path
$csproj = Get-ChildItem -Path $ProjectPath -Filter *.csproj -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $csproj) {
    Write-Error "No .csproj found under '$ProjectPath'."
    exit 1
}

$assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($csproj.Name)

Write-Info "Publishing project: $($csproj.FullName)"

$publishTemp = Join-Path -Path $env:TEMP -ChildPath "VolumeAssistantApp.Publish.$([Guid]::NewGuid().ToString())"
if (Test-Path $publishTemp) { Remove-Item -Recurse -Force $publishTemp }
New-Item -ItemType Directory -Path $publishTemp | Out-Null

try {
    $dotnet = Get-Command dotnet -ErrorAction Stop
} catch {
    Write-Error "dotnet CLI not found in PATH. Install .NET SDK or add it to PATH."
    exit 1
}

if ($SelfContained) {
    Write-Info "Running: dotnet restore -r $Runtime $($csproj.FullName)"
    dotnet restore -r $Runtime $csproj.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet restore failed (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }

    Write-Info "Running: dotnet publish -c $Configuration -r $Runtime --self-contained true -o $publishTemp $($csproj.FullName)"
    dotnet publish --no-restore -c $Configuration -r $Runtime --self-contained true -o $publishTemp $csproj.FullName
} else {
    Write-Info "Running: dotnet publish -c $Configuration -o $publishTemp $($csproj.FullName)"
    dotnet publish -c $Configuration -o $publishTemp $csproj.FullName
}
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

Write-Info "Publish complete. Preparing install directory: $InstallDir"
if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }

# Copy files: overwrite non-appsettings; only copy appsettings* if they don't exist yet
$publishedFiles = Get-ChildItem -Path $publishTemp -File
foreach ($f in $publishedFiles) {
    $dest = Join-Path $InstallDir $f.Name
    if ($f.Name -like 'appsettings*') {
        if (-not (Test-Path $dest)) {
            Copy-Item -Path $f.FullName -Destination $dest -Force
            Write-Info "Copied (new) $($f.Name)"
        } else {
            Write-Info "Skipped existing $($f.Name)"
        }
    } else {
        Copy-Item -Path $f.FullName -Destination $dest -Force
        Write-Info "Copied $($f.Name)"
    }
}

# Locate the published executable
$exeName = "$assemblyName.exe"
$exePath = Join-Path $InstallDir $exeName

if (-not (Test-Path $exePath)) {
    Write-Error "Executable '$exeName' not found in '$InstallDir'. Install may be incomplete."
    exit 1
}

Write-Info "Installed to: $exePath"

# Create a Start Menu shortcut
$startMenuDir = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenuDir "$AppName.lnk"
try {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = "VolumeAssistant tray app — syncs Windows volume with Cambridge Audio"
    $shortcut.Save()
    Write-Info "Start Menu shortcut created: $shortcutPath"
} catch {
    Write-Warning "Could not create Start Menu shortcut: $_"
}

# Optionally add a registry run key for automatic startup on login
if ($AddStartup) {
    $regKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $regKey -Name $AppName -Value "`"$exePath`"" -ErrorAction SilentlyContinue
    if ($LASTEXITCODE -eq 0 -or $?) {
        Write-Info "Added startup registry entry: $AppName -> $exePath"
    } else {
        Write-Warning "Could not add startup registry entry."
    }
}

Write-Info "Cleaning up temporary publish directory."
try { Remove-Item -Recurse -Force $publishTemp } catch {}

Write-Info "Install complete. Run '$exePath' to start the tray app."
