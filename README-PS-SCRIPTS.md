PowerShell helper scripts
=========================

This repository includes several PowerShell helper scripts to build, install and
manage the `VolumeAssistant` Windows service when working from source.

Files
-----

- `scripts/Install-VolumeAssistant.ps1`
  - Publishes the service project, copies the published files to an install
    directory (defaults to `%ProgramFiles%\VolumeAssistant`), copies
    `appsettings*` files only when they do not already exist, creates/replaces
    the Windows service and starts it.
  - Run elevated (Administrator).
  - Example:
    `powershell -ExecutionPolicy Bypass -File .\scripts\Install-VolumeAssistant.ps1`

- `scripts/Start-VolumeAssistant.ps1`
  - Starts the installed service, waits for it to reach `Running`, and prints
    diagnostics (including the tail of `service-start.log` if present).
  - Run elevated.
  - Example:
    `powershell -ExecutionPolicy Bypass -File .\scripts\Start-VolumeAssistant.ps1`

- `scripts/Stop-VolumeAssistant.ps1`
  - Stops the installed service, waits for it to reach `Stopped`, and prints
    diagnostics if it fails to stop.
  - Run elevated.
  - Example:
    `powershell -ExecutionPolicy Bypass -File .\scripts\Stop-VolumeAssistant.ps1`

- `scripts/Configure-AppSettings.ps1`
  - Interactive prompt to update `appsettings.json` in the install directory.
  - Press Enter to accept the defaults: for booleans Enter => `false`; for
    strings Enter => `null` (property set to null).
  - Backs up any existing `appsettings.json` before writing.
  - Run elevated if the install directory is under Program Files.
  - Example:
    `powershell -ExecutionPolicy Bypass -File .\scripts\Configure-AppSettings.ps1`

Notes
-----

- All scripts default to the install directory `%ProgramFiles%\VolumeAssistant`.
- Scripts should be run from an elevated PowerShell prompt when they need to
  write to Program Files or manage Windows services.
- The install script attempts to detect whether the publish output contains a
  self-contained executable or a framework-dependent DLL and configures the
  service accordingly.

If you need the scripts adjusted (different defaults, service account, or
logging), open an issue or submit a pull request.
