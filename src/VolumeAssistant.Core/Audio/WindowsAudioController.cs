using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;

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

        // Try to get the default render endpoint. Some environments (service sessions,
        // virtual machines, or when no endpoint is set) can cause GetDefaultAudioEndpoint
        // to throw COMException (Element not found). Attempt fallbacks and finally
        // enumerate active render devices.
        try
        {
            try
            {
                _device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch (COMException)
            {
                // Try other roles
                foreach (Role role in new[] { Role.Console, Role.Multimedia, Role.Communications })
                {
                    try
                    {
                        _device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                        if (_device != null)
                            break;
                    }
                    catch (COMException)
                    {
                        // ignore and try next
                    }
                }

                // If still not found, try enumerating active render endpoints and pick the first
                if (_device == null)
                {
                    var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active);
                    if (devices != null && devices.Count > 0)
                    {
                        _device = devices[0];
                    }
                }
            }

            if (_device == null)
            {
                throw new InvalidOperationException("No audio render endpoint was found on the system.");
            }

            _notificationDelegate = OnVolumeNotification;
            _device.AudioEndpointVolume.OnVolumeNotification += _notificationDelegate;
        }
        catch (COMException ex)
        {
            // Wrap and provide a clearer message for callers
            throw new InvalidOperationException("Failed to initialize Windows audio controller. No audio endpoint available.", ex);
        }
    }

    private void EnsureDeviceInitialized()
    {
        if (_device == null)
            throw new InvalidOperationException("Audio device is not initialized.");
    }

    /// <inheritdoc />
    public float GetVolumePercent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureDeviceInitialized();
        return _device!.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
    }

    /// <inheritdoc />
    public bool GetMuted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureDeviceInitialized();
        return _device!.AudioEndpointVolume.Mute;
    }

    /// <inheritdoc />
    public void SetVolumePercent(float volumePercent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureDeviceInitialized();
        float clamped = Math.Clamp(volumePercent, 0f, 100f);
        _device!.AudioEndpointVolume.MasterVolumeLevelScalar = clamped / 100f;
    }

    /// <inheritdoc />
    public void SetMuted(bool muted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureDeviceInitialized();
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
