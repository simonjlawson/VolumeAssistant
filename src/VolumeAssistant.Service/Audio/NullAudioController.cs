namespace VolumeAssistant.Service.Audio;

/// <summary>
/// A no-op audio controller used when Windows WASAPI is unavailable
/// (for example when the process is running as a Windows Service in
/// session 0). This implementation intentionally does nothing and
/// returns sensible defaults so the rest of the application can run.
/// </summary>
public sealed class NullAudioController : IAudioController
{
    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    public float GetVolumePercent() => 0f;

    public bool GetMuted() => true;

    public void SetVolumePercent(float volumePercent) { }

    public void SetMuted(bool muted) { }

    public void Dispose() { }
}
