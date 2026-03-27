# VolumeAssistant

## Summary

Directly syncs Windows volume with external Matter devices and integrates a Cambridge Audio StreamMagic device with the PC.

A Windows service/Tray App that can expose the Windows master volume as a **Matter** smart home device on the local network for other devices to match. Connects to Cambridge Audio StreamMagic devices and directly syncs Windows volume. A Home Assistant, Google Home, or Apple Home controller can discover, commission, and control the Windows PC volume as if it were a dimmable light — where the *brightness* (level 0–254) maps directly to *volume* (0–100%).

## Features

- **Windows Service** (`VolumeAssistant.Service`) – runs in the background without a UI, starts automatically with Windows, limited to volume handling, no key presses can be intercepted.
- **System Tray App** (`VolumeAssistant.App`) – a lightweight **Native AOT** Forms app able to intercept media keys and Shift+SCRLK for source switching.
- **Real-time volume sync** – whenever the master volume changes in Windows, the change is immediately reported to all subscribed Matter controllers.
- **Two-way control** – Matter controllers can set the volume (Level Control cluster) or mute it (On/Off cluster).
- **mDNS advertisement** – the device is automatically discoverable via DNS-SD.
- **Cambridge Audio** - Direct integration of Cambridge Audio API for Windows → CA volume sync and configurable source/output/power control.
  - Power On on login/wakeup; Power Off on shutdown/sleep
  - Switch to USB source on login
  - Windows volume controls Amp volume (with configurable maximum)
  - Mute, Play/Pause, Next, Previous keys sent to device
  - Shift+SCRLK cycles Amp source; Shift+PRTSCR toggles Amp L/R balance

For architecture details, development setup, protocol internals, and Cambridge Audio technical information see [TECHNICAL.md](TECHNICAL.md).

## Installation

### App Install

- Right click Save this link [install script](https://raw.githubusercontent.com/simonjlawson/VolumeAssistant/refs/heads/main/scripts/install-app.ps1)
- Run Powershell as Administrator
- Execute ``` powershell -ExecutionPolicy Bypass -File .\install-app.ps1 ```

### Service Install

- Right click Save this link [install script](https://raw.githubusercontent.com/simonjlawson/VolumeAssistant/refs/heads/main/scripts/install.ps1)
- Run Powershell as Administrator
- Execute ``` powershell -ExecutionPolicy Bypass -File .\scripts\install-service.ps1 ```

Notes
- The installer scripts fetch the latest GitHub release whose tag starts with `App-` or `Service-` and install the matching zip.
- The service installer registers a Windows service named `VolumeAssistantService` that runs the framework-dependent `VolumeAssistant.Service.dll` with the system dotnet. For self-contained deployments adjust the script to point to the executable.

### Manual install

If you prefer to publish and install manually:

```powershell
# Publish a self-contained executable for the tray app
dotnet publish src/VolumeAssistant.App -c Release -r win-x64 -o publish/App

# Publish the service
dotnet publish src/VolumeAssistant.Service -c Release -r win-x64 -o publish/Service

# Install the Windows Service (run as Administrator)
sc.exe create VolumeAssistant binPath="C:\path\to\publish\VolumeAssistant.Service.exe" start=auto DisplayName="VolumeAssistant Matter Bridge"
sc.exe start VolumeAssistant
```

## Configuration

VolumeAssistant is configured via `appsettings.json`, located in the install directory.

Enable Cambridge Audio integration with `CambridgeAudio:Enable`. If `Host` is left empty, the service will automatically discover StreamMagic devices on your local network at startup using SSDP.

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
  },
  "App": {
    "UseSourcePopup": true,
    "BalanceOffset": -35
  }
}
```

| Setting | Description |
|---|---|
| **Enable** | Set to `true` to enable Cambridge Audio integration. When `true` and `Host` is empty, automatic SSDP device discovery is used on startup. |
| **Host** | Hostname or IP address of the device. Leave empty to use automatic discovery. |
| **StartPower** | Optional initial power state on startup. `true` for on, `false` for standby. If not specified, retains current state. |
| **ClosePower** | Power off the amplifier when the service stops. `true` to power off, `false` to leave on. Default is `false`. |
| **StartVolume** | Optional initial volume level (0–100%) on startup. If not specified, retains current amplifier volume. |
| **StartSourceName** | Optional initial source name on startup. Must match a valid source name on the device. If not specified, retains current source. |
| **StartOutput** | Optional initial output name on startup. Must match a valid output name on the device. If not specified, retains current output. |
| **MaxVolume** | Maximum volume level (0–100) that 100% Windows master volume maps to on the Cambridge Audio device. For example, `80` means Windows 100% → Cambridge Audio 80%. Leave `null` for a 1:1 mapping. |
| **MediaKeysEnabled** | When `true`, intercepts Windows media key presses (Play/Pause, Next Track, Previous Track) and forwards them to the Cambridge Audio device. Default is `false`. |
| **SourceSwitchingEnabled** | When `true`, Shift+SCRLK cycles through the sources listed in `SourceSwitchingNames`. Default is `false`. |
| **SourceSwitchingNames** | Comma-separated list of source names to cycle through when `SourceSwitchingEnabled` is `true`. Each name must match a source name on the device (case-insensitive). Example: `"PC,TV,Spotify"`. |
| **BalanceOffset** | An int from -100 to +100 controlling the L/R balance (-100 = 100% Left). Shift+PrtScr toggles this balance. |

To edit settings interactively using a script:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Configure-AppSettings.ps1
```

After updating `appsettings.json`, restart the service or tray app for changes to take effect.

## Attribution

The Cambridge Audio integration is a partial C# port of the following Python projects:

- [aiostreammagic](https://github.com/noahhusby/aiostreammagic)
- [stream_magic](https://github.com/sebk-666/stream_magic)
