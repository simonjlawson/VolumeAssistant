# Install latest Service release (tag starts with Service-)
# Run as Administrator

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator."; exit 1
}

$repo = 'simonjlawson/VolumeAssistant'
$prefix = 'Service-'
$assetPattern = 'VolumeAssistant.Service-*.zip'
$targetDir = 'C:\Program Files\VolumeAssistant\Service'
$tempZip = Join-Path $env:TEMP 'VolumeAssistant.Service.latest.zip'
$serviceName = 'VolumeAssistantService'

Write-Output "Querying GitHub releases for $repo (prefix=$prefix)..."
$releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases" -UseBasicParsing
$release = $releases | Where-Object { $_.tag_name -like "$prefix*" } | Sort-Object {[datetime]$_.published_at} -Descending | Select-Object -First 1
if (-not $release) { Write-Error "No release with prefix $prefix found."; exit 1 }

$asset = $release.assets | Where-Object { $_.name -like $assetPattern } | Select-Object -First 1
if (-not $asset) { Write-Error "No asset matching $assetPattern found in release $($release.tag_name)."; exit 1 }

$downloadUrl = $asset.url
Write-Output "Downloading $($asset.name) from release $($release.tag_name)..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -Headers @{ 'Accept' = 'application/octet-stream' } -UseBasicParsing

Write-Output "Extracting to $targetDir..."
if (Test-Path $targetDir) { Remove-Item -Path $targetDir -Recurse -Force }
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
Expand-Archive -Path $tempZip -DestinationPath $targetDir -Force

# Install or update Windows service
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Write-Output "Stopping and removing existing service $serviceName..."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$exePath = Join-Path $targetDir 'VolumeAssistant.Service.dll'
$binPath = "`"$dotnet`" `"$exePath`""
Write-Output "Creating service $serviceName with binPath: $binPath"
& sc.exe create $serviceName binPath= $binPath DisplayName= 'Volume Assistant Service' start= auto

Start-Sleep -Seconds 1
Start-Service -Name $serviceName

Remove-Item -Path $tempZip -Force

Write-Output "Service $serviceName installed and started."
