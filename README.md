# VolumeAssistant

## Summary
Directly syncs Windows volume with external sources allowing them to used as traditional USB audio due to highend HiFis enforcing maximum volume. 

## Functionality
A Windows serivce that can expose the Windows master volume as a **Matter** smart home device on the local network for other devices to match. Connects to CambridgeAudio StreamMagic devices and directly syncs Windows volume.A Home Assistant, Google Home, or Apple Home controller can discover, commission, and control the Windows PC volume as if it were a dimmable light — where the *brightness* (level 0–254) maps directly to *volume* (0–100%).

- **Windows Service** – runs in the background without a UI, starts automatically with Windows.
- **Real-time volume sync** – whenever the master volume changes in Windows, the change is immediately reported to all subscribed Matter controllers.
- **Two-way control** – Matter controllers can set the volume (Level Control cluster) or mute it (On/Off cluster).
- **mDNS advertisement** – the device is automatically discoverable via DNS-SD (`_matterc._udp` + `_matter._tcp`).
- **Matter protocol** – UDP server on port 5540 with standard Interaction Model: Read, Write, Subscribe, and Command operations.
- **Cambridge Audio** - Direct intergration of Cambridge Audio API for Windows -> CA volume sync and configurable source/output/power control.

## Installation

Run the install convenience script, then configure the appsettings.json file
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install-VolumeAssistant.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Configure-AppSettings.ps1
```

## Usage

### Requirements

- Windows 10
- .NET 10.0 Runtime

### Build

Standard dotnet CLI commands to build, run, and test the solution:
```bash
dotnet build VolumeAssistant.slnx
dotnet run --project src/VolumeAssistant.Service
dotnet test tests/VolumeAssistant.Tests
```

### Scripts

PowerShell Helper scripts `README-PS-SCRIPTS.md`.

Install
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install-VolumeAssistant.ps1
```

Configure
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Configure-AppSettings.ps1
```

Start
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-VolumeAssistant.ps1
```

Stop
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Stop-VolumeAssistant.ps1
```

### Manual Service Commands

To Install
```powershell
# Publish a self-contained executable
dotnet publish src/VolumeAssistant.Service -c Release -r win-x64 --self-contained -o publish/

# Install the Windows Service (run as Administrator)
sc.exe create VolumeAssistant binPath="C:\path\to\publish\VolumeAssistant.Service.exe" start=auto DisplayName="VolumeAssistant Matter Bridge"
sc.exe description VolumeAssistant "Exposes Windows master volume as a Matter device"
sc.exe start VolumeAssistant
```

Uninstall
```powershell
sc.exe stop VolumeAssistant
sc.exe delete VolumeAssistant
```

## Matter

```
┌──────────────────────────────────────────────────────────────┐
│                  VolumeAssistant.Service                     │
│                                                              │
│  ┌────────────────────┐      ┌─────────────────────────────┐ │
│  │ WindowsAudio       │      │ MatterDevice                │ │
│  │ Controller         │◄────►│  Endpoint 0 (Root)          │ │
│  │ (NAudio WASAPI)    │      │    BasicInformation cluster │ │
│  └────────────────────┘      │  Endpoint 1 (Volume)        │ │
│                              │    OnOff cluster            │ │
│  ┌────────────────────┐      │    LevelControl cluster     │ │
│  │ Worker             │◄────►└─────────────────────────────┘ │
│  │ (BackgroundService)│             │                        │
│  └────────────────────┘      ┌──────▼──────────────────────┐ │
│                              │ MatterServer                │ │
│  ┌───────────────────┐       │ (UDP:5540 Interaction Model │ │
│  │ MdnsAdvertiser    │       └─────────────────────────────┘ │
│  │ (_matterc._udp    │                                       │
│  │  _matter._tcp)    │                                       │
│  └───────────────────┘                                       │
└──────────────────────────────────────────────────────────────┘
```

### Commission into Matter Fabric

After starting the service, open your Matter controller (e.g., Home Assistant) and add a new Matter device.  
Use the default commissioning PIN: **`20202021`** and discriminator **`3840`**.

The device appears as a **Dimmable Light** where the level (0–100%) controls the Windows volume.

## Cambridge Audio

VolumeAssistant can optionally connect to a [Cambridge Audio StreamMagic](https://www.cambridgeaudio.com/streammagic) amplifier over your local network, providing volume and source control.

This integration is a partial C# port of the Python [aiostreammagic](https://github.com/noahhusby/aiostreammagic) library.

### How it works

The service connects to the Cambridge Audio device's built-in WebSocket server at `ws://{host}/smoip` and exchanges JSON messages using the StreamMagic API:

```
Request:  {"path": "/zone/state", "params": {"zone": "ZONE1", "volume_percent": 50}}
Response: {"path": "/zone/state", "type": "response", "result": 200, "params": {"data": {...}}}
Update:   {"path": "/zone/state", "type": "update", "params": {"data": {...}}}
```

### Supported operations

| Operation | Method |
|---|---|
| Get device info | `GetInfoAsync()` |
| Get available sources | `GetSourcesAsync()` |
| Get zone state (volume, mute, source) | `GetStateAsync()` |
| Set volume (0–100%) | `SetVolumeAsync(int)` |
| Set mute | `SetMuteAsync(bool)` |
| Select input source | `SetSourceAsync(string sourceId)` |
| Power on | `PowerOnAsync()` |
| Network standby | `PowerOffAsync()` |

### Volume synchronisation

When Cambridge Audio integration is enabled, the service keeps the Windows master volume and the Cambridge Audio amplifier volume in sync:

- Windows volume changes → immediately applied to Cambridge Audio amplifier
- (WIP - This causes a horrible loop) - Cambridge Audio volume changes (e.g. hardware knob) → immediately applied to Windows master volume
- Matter controller commands → applied to both Windows (and therefore Cambridge Audio if enabled)

### Configuration

Enable the integration with `CambridgeAudio:Enable` in `appsettings.json`. If `Host` is left empty, the service will automatically discover StreamMagic devices on your local network at startup using SSDP.

```json
{
  "CambridgeAudio": {
    "Enable": true,
    "Host": "",
    "Port": 80,
    "Zone": "ZONE1"
  }
}
```

Specify `Host` to connect to a particular device directly (skipping discovery):

```json
{
  "CambridgeAudio": {
    "Enable": true,
    "Host": "192.168.1.10",
    "Port": 80,
    "Zone": "ZONE1",
    "StartPower": false,
    "ClosePower": false,
    "StartVolume": "10",
    "StartSourceName": "PC",
    "StartOutput": null,
    "MaxVolume": "30"
  }
}
```
* **Enable** - Set to `true` to enable Cambridge Audio integration. When `true` and `Host` is empty the service will attempt automatic SSDP device discovery on startup.
* **Host** - Hostname or IP address of the device. Leave empty to use automatic discovery.
* **StartPower** - Optional initial power state to set on startup. `true` for on, `false` for standby. If not specified, retains current power state.
* **ClosePower** - Optional setting to power off the amplifier when the service stops. `true` to power off, `false` to leave on. Default is `false`.
* **StartVolume** - Optional initial volume level (0–100%) to set on startup. If not specified, retains current amplifier volume.
* **StartSourceName** - Optional initial source name to select on startup. Must match a valid source from `GetSourcesAsync()`. If not specified, retains current source.
* **StartOutput** - Optional initial output name to select on startup. Must match a valid output from `GetOutputsAsync()`. If not specified, retains current output.
* **MaxVolume** - Optional maximum volume level (0–100) that 100% Windows master volume maps to on the Cambridge Audio device. For example, setting `MaxVolume` to `80` means Windows 100% → Cambridge Audio 80%, Windows 50% → Cambridge Audio 40%, etc. Cambridge Audio volume changes are also scaled back proportionally to Windows volume. Leave `null` (default) to use a 1:1 mapping where Windows 100% = Cambridge Audio 100%.

### Device Discovery

When `Enable` is `true` and `Host` is not set, VolumeAssistant will send an SSDP M-SEARCH multicast to `239.255.255.250:1900` and connect to the first Cambridge Audio StreamMagic device that responds. If no device is found within the discovery timeout, the integration is silently disabled for that session.

### Configuration (Not working yet)

```
    "RelativeVolume": false,
```

* **RelativeVolume** - Optional setting to treat volume changes as relative adjustments instead of absolute levels. It gets weird using this but might be useful, `true` to enable relative volume (e.g., +5% or -10%), `false` for absolute volume levels. Default is `false`.


### Reconnection

The client automatically reconnects on disconnect using exponential backoff (default: 0.5 s initial, 30 s maximum), matching the Python aiostreammagic reconnect behaviour.
