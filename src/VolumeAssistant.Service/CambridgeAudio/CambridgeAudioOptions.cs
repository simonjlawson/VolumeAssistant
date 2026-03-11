namespace VolumeAssistant.Service.CambridgeAudio;

/// <summary>
/// Configuration options for connecting to a Cambridge Audio StreamMagic device.
/// </summary>
public sealed class CambridgeAudioOptions
{
    public const string SectionName = "CambridgeAudio";

    /// <summary>
    /// Hostname or IP address of the Cambridge Audio device (e.g. "192.168.1.10" or "cambridge-audio.local").
    /// Leave empty to disable Cambridge Audio integration.
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
    /// Returns true if a host has been configured and Cambridge Audio integration is enabled.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Host);
}
