namespace VolumeAssistant.Service.CambridgeAudio;

/// <summary>
/// Client for communicating with a Cambridge Audio StreamMagic device over WebSocket.
/// Provides volume and source control mapped from the Python aiostreammagic library.
/// </summary>
public interface ICambridgeAudioClient : IAsyncDisposable
{
    /// <summary>Raised when the zone state changes (volume, mute, source).</summary>
    event EventHandler<CambridgeAudioStateChangedEventArgs>? StateChanged;

    /// <summary>Raised when the device connection state changes.</summary>
    event EventHandler<CambridgeAudioConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>Returns true when the WebSocket connection is active.</summary>
    bool IsConnected { get; }

    /// <summary>The cached device info (populated after connect).</summary>
    CambridgeAudioInfo? Info { get; }

    /// <summary>Available sources (populated after connect).</summary>
    IReadOnlyList<CambridgeAudioSource> Sources { get; }

    /// <summary>Current zone state (populated after connect, updated via subscription).</summary>
    CambridgeAudioState? State { get; }

    /// <summary>
    /// Connects to the device and starts subscription-based state monitoring.
    /// Reconnects automatically on disconnection until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the device and stops reconnection attempts.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sets the zone volume (0–100 percent).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If volume is outside 0–100.</exception>
    /// <exception cref="CambridgeAudioException">If not connected or the device returns an error.</exception>
    Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the zone mute state.
    /// </summary>
    Task SetMuteAsync(bool muted, CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects a source by its source ID (e.g. "usb_audio", "hdmi1").
    /// </summary>
    Task SetSourceAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the zone audio output (optional parameter supported by some devices).
    /// </summary>
    Task SetAudioOutputAsync(string output, CancellationToken cancellationToken = default);

    /// <summary>
    /// Powers on the device.
    /// </summary>
    Task PowerOnAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the device in network standby (soft power off).
    /// </summary>
    Task PowerOffAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current zone state from the device.
    /// </summary>
    Task<CambridgeAudioState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the list of available sources from the device.
    /// </summary>
    Task<IReadOnlyList<CambridgeAudioSource>> GetSourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves device identity information.
    /// </summary>
    Task<CambridgeAudioInfo> GetInfoAsync(CancellationToken cancellationToken = default);
}
