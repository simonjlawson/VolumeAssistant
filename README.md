# VolumeAssistant

A Windows serivce that exposes the Windows master volume as a **Matter** smart home device on the local network for other devices to match. A Home Assistant, Google Home, or Apple Home controller can discover, commission, and control the Windows PC volume as if it were a dimmable light — where the *brightness* (level 0–254) maps directly to *volume* (0–100%).

## Features

- **Windows Service** – runs in the background without a UI, starts automatically with Windows.
- **Real-time volume sync** – whenever the master volume changes in Windows, the change is immediately reported to all subscribed Matter controllers.
- **Two-way control** – Matter controllers can set the volume (Level Control cluster) or mute it (On/Off cluster).
- **mDNS advertisement** – the device is automatically discoverable via DNS-SD (`_matterc._udp` + `_matter._tcp`).
- **Matter protocol** – UDP server on port 5540 with standard Interaction Model: Read, Write, Subscribe, and Command operations.
- **Cambridge Audio** - Direct intergration of Cambridge Audio API for Windows -> CA volume sync and source/output/power control.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                  VolumeAssistant.Service                    │
│                                                             │
│  ┌───────────────────┐      ┌─────────────────────────────┐ │
│  │ WindowsAudio       │      │ MatterDevice                │ │
│  │ Controller         │◄────►│  Endpoint 0 (Root)          │ │
│  │ (NAudio WASAPI)    │      │    BasicInformation cluster │ │
│  └───────────────────┘      │  Endpoint 1 (Volume)        │ │
│                              │    OnOff cluster            │ │
│  ┌───────────────────┐      │    LevelControl cluster     │ │
│  │ Worker             │◄────►└─────────────────────────────┘ │
│  │ (BackgroundService)│             │                         │
│  └───────────────────┘      ┌──────▼──────────────────────┐ │
│                              │ MatterServer                 │ │
│  ┌───────────────────┐      │ (UDP:5540 Interaction Model) │ │
│  │ MdnsAdvertiser     │      └─────────────────────────────┘ │
│  │ (_matterc._udp     │                                       │
│  │  _matter._tcp)     │                                       │
│  └───────────────────┘                                       │
└─────────────────────────────────────────────────────────────┘
```

## Matter Device Mapping

| Windows concept | Matter concept |
|---|---|
| Master volume (0–100%) | Level Control – `CurrentLevel` (0–254) |
| Muted | On/Off – `OnOff` = `false` |
| Unmuted | On/Off – `OnOff` = `true` |
| Volume change event | Attribute subscription report |

## Requirements

- Windows 10 / Server 2019 or later
- .NET 8.0 Runtime

## Build

```bash
dotnet build VolumeAssistant.slnx
```

## Run (console / development)

```bash
dotnet run --project src/VolumeAssistant.Service
```

## Install as a Windows Service

```powershell
# Publish a self-contained executable
dotnet publish src/VolumeAssistant.Service -c Release -r win-x64 --self-contained -o publish/

# Install the Windows Service (run as Administrator)
sc.exe create VolumeAssistant binPath="C:\path\to\publish\VolumeAssistant.Service.exe" start=auto DisplayName="VolumeAssistant Matter Bridge"
sc.exe description VolumeAssistant "Exposes Windows master volume as a Matter device"
sc.exe start VolumeAssistant
```

To uninstall:

```powershell
sc.exe stop VolumeAssistant
sc.exe delete VolumeAssistant
```

## Commission into Matter Fabric

After starting the service, open your Matter controller (e.g., Home Assistant) and add a new Matter device.  
Use the default commissioning PIN: **`20202021`** and discriminator **`3840`**.

The device appears as a **Dimmable Light** where the level (0–100%) controls the Windows volume.

## Cambridge Audio Integration

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
- Cambridge Audio volume changes (e.g. hardware knob) → immediately applied to Windows master volume
- Matter controller commands → applied to both Windows and Cambridge Audio

### Configuration

Set the `CambridgeAudio:Host` value in `appsettings.json` (or via environment variable / Windows Service secrets):

```json
{
  "CambridgeAudio": {
    "Host": "192.168.1.10",
    "Port": 80,
    "Zone": "ZONE1"
  }
}
```

Leave `Host` empty to disable Cambridge Audio integration entirely.

### Reconnection

The client automatically reconnects on disconnect using exponential backoff (default: 0.5 s initial, 30 s maximum), matching the Python aiostreammagic reconnect behaviour.



```bash
dotnet test tests/VolumeAssistant.Tests
```
