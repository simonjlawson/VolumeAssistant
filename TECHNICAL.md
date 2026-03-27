# VolumeAssistant — Technical Reference

## Architecture

The solution is split into three projects sharing common code:

```
VolumeAssistant.Core     — Shared library: Audio, Cambridge Audio, Matter, VolumeSyncCoordinator
VolumeAssistant.Service  — Windows Service (headless, starts automatically)
VolumeAssistant.App      — Native AOT System Tray App (Windows Forms, no .NET runtime required when published)
```

Both `VolumeAssistant.Service` and `VolumeAssistant.App` reference `VolumeAssistant.Core` for all volume-sync logic.

## Compatibility

StreamMagic is as simple as integrations get so the service should be universal, please do get in contact to confirm/deny other devices work.

| Device | Verified |
|---|---|
| Evo 150 SE | Yes |

## Development

### Requirements

- Windows 10 or later (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — required to **build** the project
- MSVC / Visual C++ Build Tools — required to **publish** the Native AOT tray app
  (install via Visual Studio Installer → *Desktop development with C++*, or the standalone *Build Tools for Visual Studio*)
- No .NET runtime is required to **run** the published tray app — the Native AOT executable is fully self-contained

Standard dotnet CLI commands to build, run, and test the solution:
```bash
dotnet build VolumeAssistant.slnx
dotnet run --project src/VolumeAssistant.Service
dotnet run --project src/VolumeAssistant.App
dotnet test tests/VolumeAssistant.Tests
```

Publish the tray app as a Native AOT self-contained executable (requires .NET SDK + MSVC build tools):
```bash
dotnet publish src/VolumeAssistant.App -c Release -r win-x64 -o publish/App
```

## System Tray App Window

Double-click the speaker tray icon (or right-click → Open) to open the window. It shows three tabs:

- **Connection** — Cambridge Audio device status (connected/disconnected, device name, current zone, volume) and Windows audio (current volume, mute state).
- **Configuration** — Current Cambridge Audio settings loaded from `appsettings.json`, plus the path to the settings file.  Changes are saved back to `appsettings.json`; restart the app to apply them.
- **Logs** — Live log output from the app with a **Clear Logs** button.

> **Note:** The tray app is built with Windows Forms and published as a Native AOT executable.
> All forms are created programmatically (no WPF/XAML required at runtime).

## Scripts

PowerShell helper scripts: see `README-PS-SCRIPTS.md`.

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

## Manual Service Commands

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

## Matter Protocol

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

### Device Discovery

When `CambridgeAudio:Enable` is `true` and `Host` is not set, VolumeAssistant will send an SSDP M-SEARCH multicast to `239.255.255.250:1900` and connect to the first Cambridge Audio StreamMagic device that responds. If no device is found within the discovery timeout, the integration is silently disabled for that session.

### WIP Configuration

```
    "RelativeVolume": false,
```

* **RelativeVolume** - Optional setting to treat volume changes as relative adjustments instead of absolute levels. It gets weird using this but might be useful, `true` to enable relative volume (e.g., +5% or -10%), `false` for absolute volume levels. Default is `false`.

### Reconnection

The client automatically reconnects on disconnect using exponential backoff (default: 0.5 s initial, 30 s maximum), matching the Python aiostreammagic reconnect behaviour.
