<##
Start-VolumeAssistant.ps1

Starts the installed VolumeAssistant Windows service, waits for it to reach the
`Running` state (with timeout), and prints the final status. If a simple
`service-start.log` exists in the install folder it will show the last 50 lines
to help diagnose startup failures.

Run as administrator.

Usage:
    .\scripts\Start-VolumeAssistant.ps1
    .\scripts\Start-VolumeAssistant.ps1 -InstallDir "C:\Program Files\VolumeAssistant" -ServiceName "VolumeAssistant"
#>

param(
    [string]$InstallDir = "$env:ProgramFiles\VolumeAssistant",
    [string]$ServiceName = "VolumeAssistant",
    [int]$TimeoutSeconds = 30
)

function Ensure-Administrator {
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
    if (-not $isAdmin) {
        Write-Error "This script should be run as Administrator to start services reliably."
        exit 1
    }
}

function Write-Info([string]$text) { Write-Host $text -ForegroundColor Cyan }

Ensure-Administrator

Write-Info "Checking for service '$ServiceName'..."
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Error "Service '$ServiceName' not found. Ensure it is installed."
    exit 1
}

if ($svc.Status -eq 'Running') {
    Write-Info "Service '$ServiceName' is already running."
    exit 0
}

Write-Info "Starting service '$ServiceName'..."
try {
    Start-Service -Name $ServiceName -ErrorAction Stop
} catch {
    Write-Error "Failed to start service: $_"
    exit 1
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc.Status -eq 'Running') { break }
    Start-Sleep -Seconds 1
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
Write-Info "Service status: $($svc.Status)"

if ($svc.Status -ne 'Running') {
    Write-Error "Service did not reach Running state within $TimeoutSeconds seconds."
    # Show last lines of a log if present to help diagnostics
    $logPath = Join-Path $InstallDir 'service-start.log'
    if (Test-Path $logPath) {
        Write-Info "--- Last 50 lines of $logPath ---"
        Get-Content -Path $logPath -Tail 50 | ForEach-Object { Write-Host $_ }
    }
    exit 2
}

Write-Info "Service started successfully."

# Optionally show a short service configuration for debugging
Write-Info "Service configuration:"
sc.exe qc $ServiceName | ForEach-Object { Write-Host $_ }

exit 0
