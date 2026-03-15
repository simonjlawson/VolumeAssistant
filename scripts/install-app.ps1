# Install latest App release (tag starts with App-)
# Run as Administrator

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator."; exit 1
}

$repo = 'simonjlawson/VolumeAssistant'
$prefix = 'App-'
$assetPattern = 'VolumeAssistant.App-*.zip'
$targetDir = 'C:\Program Files\VolumeAssistant\App'
$tempZip = Join-Path $env:TEMP 'VolumeAssistant.App.latest.zip'

# Application identification
$appExeNamePattern = 'VolumeAssistant*.exe'

function Stop-ExistingApp {
    Write-Output "Looking for running VolumeAssistant app processes to stop..."
    $procs = Get-Process | Where-Object { $_.Name -like 'VolumeAssistant*' } -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        try {
            Write-Output "Stopping process $($p.Name) (Id $($p.Id))..."
            Stop-Process -Id $p.Id -Force -ErrorAction Stop
        } catch {
            Write-Warning "Failed to stop process $($p.Name): $_"
        }
    }
}

function Ensure-StartupShortcut($exePath) {
    $startupDir = [Environment]::GetFolderPath('Startup')
    $linkName = 'VolumeAssistant App.lnk'
    $linkPath = Join-Path $startupDir $linkName
    if (Test-Path $linkPath) {
        Write-Output "Startup shortcut already exists: $linkPath"
        return
    }
    Write-Output "Creating startup shortcut for $exePath"
    $w = New-Object -ComObject WScript.Shell
    $s = $w.CreateShortcut($linkPath)
    $s.TargetPath = $exePath
    $s.WorkingDirectory = Split-Path $exePath
    $s.Save()
}

Write-Output "Querying GitHub releases for $repo (prefix=$prefix)..."
$releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases" -UseBasicParsing
$release = $releases | Where-Object { $_.tag_name -like "$prefix*" } | Sort-Object {[datetime]$_.published_at} -Descending | Select-Object -First 1
if (-not $release) { Write-Error "No release with prefix $prefix found."; exit 1 }

$asset = $release.assets | Where-Object { $_.name -like $assetPattern } | Select-Object -First 1
if (-not $asset) { Write-Error "No asset matching $assetPattern found in release $($release.tag_name)."; exit 1 }

$downloadUrl = $asset.url
Write-Output "Downloading $($asset.name) from release $($release.tag_name)..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -Headers @{ 'Accept' = 'application/octet-stream' } -UseBasicParsing

Stop-ExistingApp

Write-Output "Extracting to $targetDir..."
if (Test-Path $targetDir) { Remove-Item -Path $targetDir -Recurse -Force }
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
Expand-Archive -Path $tempZip -DestinationPath $targetDir -Force

Write-Output "Installed App to $targetDir"
Remove-Item -Path $tempZip -Force

# Find executable
$exe = Get-ChildItem -Path $targetDir -Filter $appExeNamePattern -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
$exePath = $null
if ($exe) {
    $exePath = $exe.FullName
    Write-Output "Found executable: $exePath"
    Ensure-StartupShortcut -exePath $exePath
    Write-Output "Starting application..."
    Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath) | Out-Null
} else {
    # Fallback to DLL
    $dll = Get-ChildItem -Path $targetDir -Filter 'VolumeAssistant.App.dll' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($dll) {
        $dllPath = $dll.FullName
        Write-Output "Found DLL: $dllPath - will run via dotnet"
        $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
        if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
        Ensure-StartupShortcut -exePath "$dotnet `"$dllPath`""
        Start-Process -FilePath $dotnet -ArgumentList "`"$dllPath`"" -WorkingDirectory (Split-Path $dllPath) | Out-Null
    } else {
        Write-Warning "No executable or DLL found to start."
    }
}

Write-Output "Done."
