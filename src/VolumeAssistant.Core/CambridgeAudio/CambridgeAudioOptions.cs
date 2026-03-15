namespace VolumeAssistant.Service.CambridgeAudio;

/// <summary>
/// Configuration options for connecting to a Cambridge Audio StreamMagic device.
/// </summary>
public sealed class CambridgeAudioOptions
{
    public const string SectionName = "CambridgeAudio";

    /// <summary>
    /// Set to true to enable Cambridge Audio integration.
    /// When true and <see cref="Host"/> is empty, SSDP device discovery will be attempted automatically.
    /// Defaults to false.
    /// </summary>
    public bool Enable { get; set; } = false;

    /// <summary>
    /// Hostname or IP address of the Cambridge Audio device (e.g. "192.168.1.10" or "cambridge-audio.local").
    /// Leave empty to use automatic SSDP device discovery (requires <see cref="Enable"/> to be true).
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// WebSocket port. Defaults to 80 (the StreamMagic default).
    /// </summary>
    public int Port { get; set; } = 80;

    /// <summary>
    /// Zone identifier sent with all zone commands. Defaults to "ZONE1".
    /// </summary>
    public string Zone { get; set; } = "ZONE1";

    /// <summary>
    /// Initial delay in milliseconds before the first reconnection attempt.
    /// Doubles on each failed attempt up to <see cref="MaxReconnectDelayMs"/>.
    /// </summary>
    public int InitialReconnectDelayMs { get; set; } = 500;

    /// <summary>
    /// Maximum delay in milliseconds between reconnection attempts.
    /// </summary>
    public int MaxReconnectDelayMs { get; set; } = 30_000;

    /// <summary>
    /// Timeout in milliseconds for individual request/response round trips.
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 5_000;

    /// <summary>
    /// Returns true if Cambridge Audio integration is enabled (i.e. <see cref="Enable"/> is true).
    /// When enabled but <see cref="Host"/> is empty, SSDP discovery will be attempted at startup.
    /// </summary>
    public bool IsEnabled => Enable;

    /// <summary>
    /// Optional source name to select when the service starts and connects to the device.
    /// If set, the client will lookup the source by name and call SetSourceAsync on startup.
    /// </summary>
    public string? StartSourceName { get; set; }

    /// <summary>
    /// Optional volume (0-100) to set after switching source on startup.
    /// </summary>
    public int? StartVolume { get; set; }

    /// <summary>
    /// Optional audio_output identifier/value to set after switching source and changing volume at startup.
    /// </summary>
    public string? StartOutput { get; set; }

    /// <summary>
    /// When true the service will send a power-on command to the device when it starts and connects.
    /// Defaults to false.
    /// </summary>
    public bool StartPower { get; set; } = false;

    /// <summary>
    /// When true the service will request the device to power on when a Windows volume
    /// change is received and the device is connected but powered off. Defaults to true
    /// (behaviour can be opted-out by setting this to false).
    /// </summary>
    public bool StartPowerOnVolumeChange { get; set; } = true;

    /// <summary>
    /// When true the service will send a power-off command to the device before shutting down.
    /// Defaults to false.
    /// </summary>
    public bool ClosePower { get; set; } = false;

    /// <summary>
    /// When true (default) Windows volume changes are applied as relative increments/decrements to the
    /// Cambridge Audio device volume instead of setting absolute values.
    /// </summary>
    public bool RelativeVolume { get; set; } = true;

    /// <summary>
    /// Optional maximum volume (0–100) that 100% Windows master volume maps to on the Cambridge Audio
    /// device. When set, the full Windows volume range (0–100%) is scaled proportionally to
    /// 0–<see cref="MaxVolume"/>% on the Cambridge Audio device, and Cambridge Audio volume changes
    /// are scaled back to the corresponding Windows volume percentage.
    /// Leave null (default) to map Windows 100% to Cambridge Audio 100%.
    /// </summary>
    public int? MaxVolume { get; set; }

    /// <summary>
    /// When true, the service will intercept Windows media key presses (Play/Pause, Next Track,
    /// Previous Track) and forward them to the Cambridge Audio device as transport control commands.
    /// Defaults to false.
    /// </summary>
    public bool MediaKeysEnabled { get; set; } = false;

    /// <summary>
    /// When true, Shift+SCRLK will cycle through
    /// the sources listed in <see cref="SourceSwitchingNames"/> instead of sending a transport
    /// control command. Requires <see cref="MediaKeysEnabled"/> to be true.
    /// Defaults to false.
    /// </summary>
    public bool SourceSwitchingEnabled { get; set; } = false;

    /// <summary>
    /// Comma-separated list of source names to cycle through when <see cref="SourceSwitchingEnabled"/>
    /// is true. Each name must match a source name returned by the device (case-insensitive).
    /// Example: "PC,TV,Spotify".
    /// </summary>
    public string? SourceSwitchingNames { get; set; }
}
