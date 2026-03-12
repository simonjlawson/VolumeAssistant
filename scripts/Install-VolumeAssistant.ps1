<##
Install-VolumeAssistant.ps1

Publishes the VolumeAssistant.Service project, copies the published files to an install
directory (only copying any appsettings files if they do not already exist at the
destination), and installs the Windows service.

Run as administrator.

Usage examples:

    .\scripts\Install-VolumeAssistant.ps1
    .\scripts\Install-VolumeAssistant.ps1 -ProjectPath "src\VolumeAssistant.Service" -InstallDir "C:\Program Files\VolumeAssistant"
#>

param(
    [string]$ProjectPath = "src\VolumeAssistant.Service",
    [string]$Configuration = "Release",
    [string]$InstallDir = "$env:ProgramFiles\VolumeAssistant",
    [string]$ServiceName = "VolumeAssistant",
    [string]$ServiceDisplayName = "VolumeAssistant Service"
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

# Resolve project path by trying several likely locations:
# 1) as provided (absolute or relative to current directory)
# 2) relative to the script folder
# 3) relative to the repository root (script parent)
$candidates = @()
$candidates += $ProjectPath
$candidates += Join-Path $scriptRoot $ProjectPath
$repoRoot = Split-Path -Path $scriptRoot -Parent
if ($repoRoot) { $candidates += Join-Path $repoRoot $ProjectPath }

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

$publishTemp = Join-Path -Path $env:TEMP -ChildPath "VolumeAssistant.Publish.$([Guid]::NewGuid().ToString())"
if (Test-Path $publishTemp) { Remove-Item -Recurse -Force $publishTemp }
New-Item -ItemType Directory -Path $publishTemp | Out-Null

try {
    $dotnet = Get-Command dotnet -ErrorAction Stop
} catch {
    Write-Error "dotnet CLI not found in PATH. Install .NET SDK or add it to PATH."
    exit 1
}

# Publish framework-dependent (no RID) by default
Write-Info "Running: dotnet publish -c $Configuration -o $publishTemp $($csproj.FullName)"
dotnet publish --no-restore -c $Configuration -o $publishTemp $csproj.FullName
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

Write-Info "Publish complete. Preparing install directory: $InstallDir"
if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }

# Copy files: overwrite non-appsettings files; only copy appsettings* if they don't exist
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

# Determine how to run the service: self-contained exe or dotnet + dll
$exeName = "$assemblyName.exe"
$dllName = "$assemblyName.dll"
$exePath = Join-Path $InstallDir $exeName
$dllPath = Join-Path $InstallDir $dllName

if (Test-Path $exePath) {
    $binaryPath = "`"$exePath`""
    Write-Info "Detected self-contained executable: $exePath"
} elseif (Test-Path $dllPath) {
    $dotnetExe = (Get-Command dotnet).Source
    $binaryPath = "`"$dotnetExe`" `"$dllPath`""
    Write-Info "Detected framework-dependent publish, will run with dotnet: $binaryPath"
} else {
    Write-Error "Neither '$exeName' nor '$dllName' found in publish output. Cannot create service."
    exit 1
}

Write-Info "Installing service: $ServiceName"

# If service exists, stop and delete it first
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Info "Service '$ServiceName' exists. Stopping and removing..."
    try { Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue } catch {}
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

try {
    New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $ServiceDisplayName -Description "Installed from source by Install-VolumeAssistant.ps1" -StartupType Automatic
    Write-Info "Service '$ServiceName' created."
} catch {
    Write-Error "Failed to create service: $_"
    exit 1
}

Write-Info "Starting service: $ServiceName"
try { Start-Service -Name $ServiceName -ErrorAction Stop; Write-Info "Service started." } catch { Write-Error "Failed to start service: $_" }

Write-Info "Cleaning up temporary publish directory."
try { Remove-Item -Recurse -Force $publishTemp } catch {}

Write-Info "Install complete."
