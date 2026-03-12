<##
Stop-VolumeAssistant.ps1

Stops the installed VolumeAssistant Windows service, waits for it to reach the
`Stopped` state (with timeout), and prints the final status. If a simple
`service-start.log` exists in the install folder it will show the last 50 lines
to help diagnose stop failures.

Run as administrator.

Usage:
    .\scripts\Stop-VolumeAssistant.ps1
    .\scripts\Stop-VolumeAssistant.ps1 -InstallDir "C:\Program Files\VolumeAssistant" -ServiceName "VolumeAssistant" -ForceStop:$false
#>

param(
    [string]$InstallDir = "$env:ProgramFiles\VolumeAssistant",
    [string]$ServiceName = "VolumeAssistant",
    [int]$TimeoutSeconds = 30,
    [switch]$ForceStop
)

function Ensure-Administrator {
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
    if (-not $isAdmin) {
        Write-Error "This script should be run as Administrator to stop services reliably."
        exit 1
    }
}

function Write-Info([string]$text) { Write-Host $text -ForegroundColor Cyan }

Ensure-Administrator

Write-Info "Checking for service '$ServiceName'..."
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Error "Service '$ServiceName' not found."
    exit 1
}

if ($svc.Status -eq 'Stopped') {
    Write-Info "Service '$ServiceName' is already stopped."
    exit 0
}

Write-Info "Stopping service '$ServiceName'..."
try {
    if ($ForceStop.IsPresent) { Stop-Service -Name $ServiceName -Force -ErrorAction Stop } else { Stop-Service -Name $ServiceName -ErrorAction Stop }
} catch {
    Write-Error "Failed to send stop to service: $_"
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc.Status -eq 'Stopped') { break }
    Start-Sleep -Seconds 1
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
Write-Info "Service status: $($svc.Status)"

if ($svc.Status -ne 'Stopped') {
    Write-Error "Service did not reach Stopped state within $TimeoutSeconds seconds."
    $logPath = Join-Path $InstallDir 'service-start.log'
    if (Test-Path $logPath) {
        Write-Info "--- Last 50 lines of $logPath ---"
        Get-Content -Path $logPath -Tail 50 | ForEach-Object { Write-Host $_ }
    }
    Write-Info "Service query:"
    sc.exe query $ServiceName | ForEach-Object { Write-Host $_ }
    exit 2
}

Write-Info "Service stopped successfully."

exit 0
