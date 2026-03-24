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

    /// <summary>
    /// Sets the left-right stereo balance.
    /// <para>
    /// A value of 0 is centred. Negative values shift audio to the left channel
    /// (e.g. -20 reduces the right channel by 20 %). Positive values shift to the
    /// right channel (e.g. +20 reduces the left channel by 20 %).
    /// The accepted range is -100 to +100.
    /// </para>
    /// </summary>
    void SetBalance(float balanceOffset);

    /// <summary>
    /// Gets the current left-right stereo balance offset in the range -100 to +100.
    /// A value of 0 means centred.
    /// </summary>
    float GetBalance();
}
