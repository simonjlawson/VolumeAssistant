<##
Install-VolumeAssistantApp.ps1

Publishes the VolumeAssistant.App (system tray) project as a Native AOT self-contained executable,
copies the published files to an install directory (preserving existing appsettings.json), and
creates a Start Menu shortcut plus an optional startup entry so the tray app launches automatically
on Windows login.

The project has <PublishAot>true</PublishAot> configured, so dotnet publish produces a single
native executable with no .NET runtime dependency.  Requires the .NET 10 SDK (not just runtime)
and the MSVC build tools (Visual Studio C++ workload or Build Tools) to be installed on the
build machine.

Usage examples:

    .\scripts\Install-VolumeAssistantApp.ps1
    .\scripts\Install-VolumeAssistantApp.ps1 -InstallDir "C:\Program Files\VolumeAssistantApp" -AddStartup $false
#>

param(
    [string]$ProjectPath = "src\VolumeAssistant.App",
    [string]$Configuration = "Release",
    [string]$InstallDir = "$env:ProgramFiles\VolumeAssistantApp",
    [string]$AppName = "VolumeAssistant",
    # Publish RID – must match the RuntimeIdentifier declared in the csproj (win-x64)
    [string]$Runtime = "win-x64",
    # Native AOT publish by default (set to $false to fall back to framework-dependent publish)
    [bool]$PublishAot = $true,
    # Add a registry run key to start the tray app on Windows login
    [bool]$AddStartup = $true
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
if (-not $scriptRoot -and $PSScriptRoot) { $scriptRoot = $PSScriptRoot }

# Resolve project path - prefer repository-root-relative resolution first so the script
# works when invoked from any current directory (for example when the caller's CWD
# is C:\Windows\system32). Try repo-root, then script-root, then the raw value.
$repoRoot = Split-Path -Path $scriptRoot -Parent
$candidates = @()
if ($repoRoot) { $candidates += Join-Path $repoRoot $ProjectPath }
$candidates += Join-Path $scriptRoot $ProjectPath
$candidates += $ProjectPath

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

if ($PublishAot) {
    # Native AOT publish: restore first to ensure the win-x64 runtime pack is available,
    # then publish with AOT enabled.  The csproj already has <PublishAot>true</PublishAot>
    # so no extra -p flag is needed, but it is repeated here for clarity.
    Write-Info "Running: dotnet restore -r $Runtime $($csproj.FullName)"
    dotnet restore -r $Runtime $csproj.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet restore failed (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }

    Write-Info "Running: dotnet publish (Native AOT) -c $Configuration -r $Runtime -o $publishTemp $($csproj.FullName)"
    dotnet publish --no-restore -c $Configuration -r $Runtime -p:PublishAot=true -o $publishTemp $csproj.FullName
} else {
    # Framework-dependent fallback (requires .NET 10 runtime on target machine)
    Write-Info "Running: dotnet restore -r $Runtime $($csproj.FullName)"
    dotnet restore -r $Runtime $csproj.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet restore failed (exit code $LASTEXITCODE)."
        exit $LASTEXITCODE
    }

    Write-Info "Running: dotnet publish (self-contained) -c $Configuration -r $Runtime --self-contained true -o $publishTemp $($csproj.FullName)"
    dotnet publish --no-restore -c $Configuration -r $Runtime --self-contained true -p:PublishAot=false -o $publishTemp $csproj.FullName
}
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed (exit code $LASTEXITCODE)."
    exit $LASTEXITCODE
}

Write-Info "Publish complete. Preparing install directory: $InstallDir"
if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }

# If the app is already running, attempt to close it so files can be overwritten
$foundProcesses = @()
$allProcs = Get-Process -ErrorAction SilentlyContinue
foreach ($p in $allProcs) {
    try {
        $mf = $p.MainModule.FileName
        if ($mf -ieq $exePath) { $foundProcesses += $p; continue }
    } catch {}
    if ($p.ProcessName -ieq $assemblyName -or $p.ProcessName -like "$($assemblyName)*") { $foundProcesses += $p }
}

if ($foundProcesses.Count -gt 0) {
    Write-Info "Detected running process(es) for '$assemblyName'. Attempting to close..."
    foreach ($p in $foundProcesses) {
        try {
            if ($p.CloseMainWindow()) {
                Write-Info "Sent close request to process $($p.Id). Waiting up to 10s for exit..."
                $sw = [System.Diagnostics.Stopwatch]::StartNew()
                while (-not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 200; $p.Refresh() }
                $sw.Stop()
            }
            if (-not $p.HasExited) {
                Write-Info "Process $($p.Id) did not exit; stopping forcefully..."
                Stop-Process -Id $p.Id -Force -ErrorAction Stop
                Start-Sleep -Milliseconds 500
            } else {
                Write-Info "Process $($p.Id) exited."
            }
        } catch {
            Write-Warning "Could not stop process $($p.Id): $_. Attempting force stop."
            try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch { Write-Warning "Failed to stop process $($p.Id): $_" }
        }
    }
    # Give the OS a moment to release file handles
    Start-Sleep -Seconds 1
}

# Copy files: overwrite non-appsettings; only copy appsettings* if they don't exist yet
$publishedFiles = Get-ChildItem -Path $publishTemp -File

function Copy-WithRetry($source, $destination, $attempts = 5) {
    for ($i = 1; $i -le $attempts; $i++) {
        try {
            Copy-Item -Path $source -Destination $destination -Force -ErrorAction Stop
            return $true
        } catch {
            Write-Warning "Copy attempt $i failed for '$destination': $_"
            if ($i -lt $attempts) { Start-Sleep -Seconds (2 * $i) } else { throw }
        }
    }
}

foreach ($f in $publishedFiles) {
    $dest = Join-Path $InstallDir $f.Name
    if ($f.Name -like 'appsettings*') {
        if (-not (Test-Path $dest)) {
            Copy-WithRetry $f.FullName $dest
            Write-Info "Copied (new) $($f.Name)"
        } else {
            Write-Info "Skipped existing $($f.Name)"
        }
    } else {
        try {
            Copy-WithRetry $f.FullName $dest
            Write-Info "Copied $($f.Name)"
        } catch {
            Write-Warning "Failed to copy $($f.Name) after retries: $_"
            # Continue with next file; do not abort entire install here
        }
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
    $shortcut.Description = "VolumeAssistant tray app - syncs Windows volume with Cambridge Audio"
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

try {
    Write-Info "Install complete. Starting app: $exePath"
    Start-Process -FilePath $exePath -WorkingDirectory $InstallDir -ErrorAction Stop
    Write-Info "App started."
} catch {
    Write-Warning "Failed to start app automatically: $_"
    Write-Info "Install complete. Run '$exePath' to start the tray app."
}
