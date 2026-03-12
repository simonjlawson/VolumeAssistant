namespace VolumeAssistant.Service.Audio;

/// <summary>
/// Interface for controlling and monitoring the system master volume.
/// </summary>
public interface IAudioController : IDisposable
{
    /// <summary>
    /// Raised when the master volume or mute state changes.
    /// </summary>
    event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    /// <summary>
    /// Gets the current master volume as a percentage (0.0 to 100.0).
    /// </summary>
    float GetVolumePercent();

    /// <summary>
    /// Gets whether the master audio is currently muted.
    /// </summary>
    bool GetMuted();

    /// <summary>
    /// Sets the master volume as a percentage (0.0 to 100.0).
    /// </summary>
    void SetVolumePercent(float volumePercent);

    /// <summary>
    /// Sets the mute state.
    /// </summary>
    void SetMuted(bool muted);
}
