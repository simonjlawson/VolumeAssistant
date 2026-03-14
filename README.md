# VolumeAssistant

## Summary
Directly syncs Windows volume with external Matter devices and integrates a Cambridge Audio StreamMagic device with the PC.

## Functionality
A Windows service/Tray App that can expose the Windows master volume as a **Matter** smart home device on the local network for other devices to match. Connects to Cambridge Audio StreamMagic devices and directly syncs Windows volume. A Home Assistant, Google Home, or Apple Home controller can discover, commission, and control the Windows PC volume as if it were a dimmable light — where the *brightness* (level 0–254) maps directly to *volume* (0–100%).

- **Windows Service** (`VolumeAssistant.Service`) – runs in the background without a UI, starts automatically with Windows, limited to volume handling, no key presses can be intercepted.
- **System Tray App** (`VolumeAssistant.App`) – a tiny app running the same code but able to intercept media keys and Shift+SCRLK for source switching.
- **Real-time volume sync** – whenever the master volume changes in Windows, the change is immediately reported to all subscribed Matter controllers.
- **Two-way control** – Matter controllers can set the volume (Level Control cluster) or mute it (On/Off cluster).
- **mDNS advertisement** – the device is automatically discoverable via DNS-SD (`_matterc._udp` + `_matter._tcp`).
- **Matter protocol** – UDP server on port 5540 with standard Interaction Model: Read, Write, Subscribe, and Command operations.
- **Cambridge Audio** - Direct integration of Cambridge Audio API for Windows → CA volume sync and configurable source/output/power control.
  - Power On on login/wakeup
  - Power Off on shutdown/sleep
  - Switch to USB source on login
  - Windows volume effects Amped volume
  - 100% Windows volume can only be 30% in Amp
  - Mute, Play/Pause, Next, Previous keys sent to device
  - Shift+SCRLK cycles Amp source

This integration is a partial C# port of the Python projects:
[aiostreammagic](https://github.com/noahhusby/aiostreammagic)
[stream_magic](https://github.com/sebk-666/stream_magic)

## Architecture

The solution is split into three projects sharing common code:

```
VolumeAssistant.Core     — Shared library: Audio, Cambridge Audio, Matter, VolumeSyncCoordinator
VolumeAssistant.Service  — Windows Service (headless, starts automatically)
VolumeAssistant.App      — WPF System Tray App (UI window with connection info / config / logs)
```

Both `VolumeAssistant.Service` and `VolumeAssistant.App` reference `VolumeAssistant.Core` for all volume-sync logic.

## Compatibility

StreamMagic is as simple as integrations get so the service should be universal, please do get in contact to confirm/deny other devices work.

| Device | Verified |
|---|---|
| Evo 150 SE | Yes |

## Installation

### App (recommended)

Install the tray-app, Windows service not required.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install-VolumeAssistantApp.ps1 -AddStartup $true
```

### Windows Service (for headless / server use)

Install the service, then configure the appsettings.json file:

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
dotnet run --project src/VolumeAssistant.App
dotnet test tests/VolumeAssistant.Tests
```

### System Tray App Window

Double-click the speaker tray icon (or right-click → Open) to open the window. It shows three tabs:

- **Connection** — Cambridge Audio device status (connected/disconnected, device name, current zone, volume) and Windows audio (current volume, mute state).
- **Configuration** — Current Cambridge Audio settings loaded from `appsettings.json`, plus the path to the settings file.
- **Logs** — Live log output from the app with a **Clear Logs** button.

### Scripts

PowerShell Helper scripts: see `README-PS-SCRIPTS.md`.

Install System Tray App
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install-VolumeAssistantApp.ps1
```

Install Windows Service
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install-VolumeAssistant.ps1
```

Configure
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Configure-AppSettings.ps1
```

Start Service
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-VolumeAssistant.ps1
```

Stop Service
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
| Toggle play/pause | `PlayPauseAsync()` |
| Next track | `NextTrackAsync()` |
| Previous track | `PreviousTrackAsync()` |

### Volume synchronisation

When Cambridge Audio integration is enabled, the service keeps the Windows master volume and the Cambridge Audio amplifier volume in sync:

- Windows volume changes → immediately applied to Cambridge Audio amplifier
- Cambridge Audio volume changes (e.g. hardware knob) → immediately applied to Windows master volume **(Disabled - This causes a horrible loop)**
- Matter controller commands → applied to both Windows (and therefore Cambridge Audio if enabled)

### Media key transport control

When `MediaKeysEnabled` is `true`, the service installs a low-level Windows keyboard hook and intercepts the following media keys, forwarding each as a StreamMagic transport command to the Cambridge Audio device:

| Key | Action |
|---|---|
| Play/Pause | Toggle play/pause (`/zone/play_control` action=toggle) |
| Next Track | Skip to next track (`/zone/play_control` skip_track=1) |
| Previous Track | Skip to previous track (`/zone/play_control` skip_track=-1) |

This allows the keyboard media keys to control playback on the Cambridge Audio device rather than (or as well as) any local media player.

When `SourceSwitchingEnabled` is also `true`, the key specified by `SourceSwitchingKey` will cycle through the configured source list instead of sending a transport command.

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
    "MaxVolume": "30",
    "MediaKeysEnabled": false,
    "SourceSwitchingEnabled": false,
    "SourceSwitchingNames": "PC,AirPlay,Internet Radio"
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
* **MediaKeysEnabled** - When `true`, the service intercepts Windows media key presses (Play/Pause, Next Track, Previous Track) and forwards them as transport control commands to the Cambridge Audio device. Default is `false`.
* **SourceSwitchingEnabled** - When `true`, Shift+SCRLK will cycle through the sources listed in `SourceSwitchingNames`. Default is `false`.
* **SourceSwitchingNames** - Comma-separated list of source names to cycle through when `SourceSwitchingEnabled` is `true`. Each name must match a source name on the device (case-insensitive). Example: `"PC,TV,Spotify"`. On each key press the service advances to the next source in the list, wrapping around from the last back to the first. If the current source is not in the list, it switches to the first entry.

### Device Discovery

When `CambridgeAudio:Enable` is `true` and `Host` is not set, VolumeAssistant will send an SSDP M-SEARCH multicast to `239.255.255.250:1900` and connect to the first Cambridge Audio StreamMagic device that responds. If no device is found within the discovery timeout, the integration is silently disabled for that session.

### WIP Configuration

```
    "RelativeVolume": false,
```

* **RelativeVolume** - Optional setting to treat volume changes as relative adjustments instead of absolute levels. It gets weird using this but might be useful, `true` to enable relative volume (e.g., +5% or -10%), `false` for absolute volume levels. Default is `false`.


### Reconnection

The client automatically reconnects on disconnect using exponential backoff (default: 0.5 s initial, 30 s maximum), matching the Python aiostreammagic reconnect behaviour.
