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

Write-Output "Installed App to $targetDir"
Remove-Item -Path $tempZip -Force

Write-Output "Done."
