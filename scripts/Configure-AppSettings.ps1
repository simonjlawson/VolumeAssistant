<##
Configure-AppSettings.ps1

Interactively update installed appsettings.json for the VolumeAssistant service.
Prompts:
 - Matter.Enabled (bool)
 - CambridgeAudio.Host (string)
 - CambridgeAudio.StartSourceName (string)
 - CambridgeAudio.StartPower (bool)
 - CambridgeAudio.ClosePower (bool)

Behavior: pressing Enter sets the value to a sensible default: for boolean
prompts Enter => false. For string prompts Enter => null (property will be set to
null).

Run as Administrator when the install folder is under Program Files.

Usage:
    .\scripts\Configure-AppSettings.ps1
    .\scripts\Configure-AppSettings.ps1 -InstallDir "C:\Program Files\VolumeAssistant"
#>

param(
    [string]$InstallDir = "$env:ProgramFiles\VolumeAssistant",
    [string]$FileName = 'appsettings.json'
)

function Ensure-Administrator {
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
    if (-not $isAdmin) {
        Write-Warning "Running without Administrator privileges. Writing to Program Files may fail."
    }
}

function Write-Info([string]$text) { Write-Host $text -ForegroundColor Cyan }

Ensure-Administrator

if (-not (Test-Path $InstallDir)) {
    Write-Error "Install directory '$InstallDir' not found."
    exit 1
}

$filePath = Join-Path $InstallDir $FileName

# Load existing or create a minimal structure
if (Test-Path $filePath) {
    try {
        $raw = Get-Content -Raw -Path $filePath -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) { $json = [pscustomobject]@{} } else { $json = $raw | ConvertFrom-Json -ErrorAction Stop }
    } catch {
        Write-Warning "Failed to parse existing JSON, starting from empty object. Error: $_"
        $json = [pscustomobject]@{}
    }
} else {
    Write-Info "No appsettings.json found at $filePath. A new one will be created."
    $json = [pscustomobject]@{}
}

# Ensure section objects exist
if (-not $json.PSObject.Properties.Match('Matter')) { $json | Add-Member -MemberType NoteProperty -Name Matter -Value ([pscustomobject]@{}) }
if (-not $json.PSObject.Properties.Match('CambridgeAudio')) { $json | Add-Member -MemberType NoteProperty -Name CambridgeAudio -Value ([pscustomobject]@{}) }

# Helper to set value at path
function Set-Value([psobject]$root, [string[]]$path, $value) {
    $current = $root
    for ($i = 0; $i -lt ($path.Length - 1); $i++) {
        $seg = $path[$i]
        # If the property is missing or its value is null, replace it with a new object
        if (-not $current.PSObject.Properties.Match($seg) -or $null -eq $current.$seg) {
            if ($current.PSObject.Properties.Match($seg)) { $current.PSObject.Properties.Remove($seg) }
            $current | Add-Member -MemberType NoteProperty -Name $seg -Value ([pscustomobject]@{})
        }
        $current = $current.$seg
    }
    $last = $path[-1]
    if ($current.PSObject.Properties.Match($last)) {
        $current.PSObject.Properties.Remove($last)
    }
    # Add property with the desired value (allows null)
    $current | Add-Member -MemberType NoteProperty -Name $last -Value $value
}

# Prompt helpers
function Prompt-Bool($label, $current) {
    $curText = if ($null -ne $current) { $current } else { 'null' }
    $input = Read-Host "$label (current: $curText) [Enter = false] - enter true/false"
    if ([string]::IsNullOrWhiteSpace($input)) { return $false }
    switch ($input.Trim().ToLower()) {
        'true' { return $true }
        't' { return $true }
        '1' { return $true }
        default { return $false }
    }
}

function Prompt-String($label, $current) {
    $curText = if ($null -ne $current) { "'$current'" } else { 'null' }
    $input = Read-Host "$label (current: $curText) [Enter = null] - enter new value"
    if ([string]::IsNullOrWhiteSpace($input)) { return $null }
    return $input
}

# Ask the user about each setting
$val = Prompt-Bool 'Matter.Enabled' ($json.Matter.Enabled -as [bool])
Set-Value -root $json -path @('Matter','Enabled') -value $val

$val = Prompt-String 'CambridgeAudio.Host' ($json.CambridgeAudio.Host)
Set-Value -root $json -path @('CambridgeAudio','Host') -value $val

$val = Prompt-String 'CambridgeAudio.StartSourceName' ($json.CambridgeAudio.StartSourceName)
Set-Value -root $json -path @('CambridgeAudio','StartSourceName') -value $val

$val = Prompt-Bool 'CambridgeAudio.StartPower' ($json.CambridgeAudio.StartPower -as [bool])
Set-Value -root $json -path @('CambridgeAudio','StartPower') -value $val

$val = Prompt-Bool 'CambridgeAudio.ClosePower' ($json.CambridgeAudio.ClosePower -as [bool])
Set-Value -root $json -path @('CambridgeAudio','ClosePower') -value $val

# Backup original file
$timestamp = Get-Date -Format "yyyyMMddHHmmss"
if (Test-Path $filePath) {
    $bak = "$filePath.$timestamp.bak"
    Copy-Item -Path $filePath -Destination $bak -Force
    Write-Info "Backup of existing file saved to: $bak"
}

# Write JSON with reasonable depth
try {
    $json | ConvertTo-Json -Depth 10 | Out-File -FilePath $filePath -Encoding UTF8
    Write-Info "Updated appsettings written to: $filePath"
} catch {
    Write-Error "Failed to write appsettings: $_"
    exit 1
}

Write-Info "Done. Restart the service if needed."
