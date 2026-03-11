using NAudio.CoreAudioApi;

namespace VolumeAssistant.Service.Audio;

/// <summary>
/// Controls and monitors the Windows master volume using the WASAPI Core Audio API.
/// </summary>
public sealed class WindowsAudioController : IAudioController
{
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private MMDevice? _device;
    private AudioEndpointVolumeNotificationDelegate? _notificationDelegate;
    private bool _disposed;

    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    public WindowsAudioController()
    {
        _deviceEnumerator = new MMDeviceEnumerator();
        _device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        _notificationDelegate = OnVolumeNotification;
        _device.AudioEndpointVolume.OnVolumeNotification += _notificationDelegate;
    }

    /// <inheritdoc />
    public float GetVolumePercent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _device!.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
    }

    /// <inheritdoc />
    public bool GetMuted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _device!.AudioEndpointVolume.Mute;
    }

    /// <inheritdoc />
    public void SetVolumePercent(float volumePercent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        float clamped = Math.Clamp(volumePercent, 0f, 100f);
        _device!.AudioEndpointVolume.MasterVolumeLevelScalar = clamped / 100f;
    }

    /// <inheritdoc />
    public void SetMuted(bool muted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _device!.AudioEndpointVolume.Mute = muted;
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        var args = new VolumeChangedEventArgs(
            data.MasterVolume * 100f,
            data.Muted);

        VolumeChanged?.Invoke(this, args);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_device != null && _notificationDelegate != null)
        {
            _device.AudioEndpointVolume.OnVolumeNotification -= _notificationDelegate;
        }

        _device?.Dispose();
        _deviceEnumerator.Dispose();
        _device = null;
        _notificationDelegate = null;
    }
}
