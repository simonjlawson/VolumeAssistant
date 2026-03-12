namespace VolumeAssistant.Service.Audio;

/// <summary>
/// Event arguments for volume change notifications.
/// </summary>
public sealed class VolumeChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the new volume level as a percentage (0.0 to 100.0).
    /// </summary>
    public float VolumePercent { get; }

    /// <summary>
    /// Gets whether the audio is muted.
    /// </summary>
    public bool IsMuted { get; }

    public VolumeChangedEventArgs(float volumePercent, bool isMuted)
    {
        VolumePercent = volumePercent;
        IsMuted = isMuted;
    }
}
