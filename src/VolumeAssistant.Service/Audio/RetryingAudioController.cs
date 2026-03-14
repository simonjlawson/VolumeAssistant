using Microsoft.Extensions.Logging;

namespace VolumeAssistant.Service.Audio;

/// <summary>
/// An audio controller that will repeatedly attempt to create a
/// WindowsAudioController until successful. Calls into this controller
/// will block until the underlying WindowsAudioController is available.
/// This implements an efficient retry loop with a 1 minute delay between
/// attempts and supports graceful disposal to cancel retries.
/// </summary>
public sealed class RetryingAudioController : IAudioController
{
    private readonly TaskCompletionSource<IAudioController> _readyTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger? _logger;
    private bool _disposed;

    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    public RetryingAudioController(ILogger? logger = null)
    {
        _logger = logger;
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var ctrl = new WindowsAudioController();
                ctrl.VolumeChanged += OnInnerVolumeChanged;
                if (_readyTcs.TrySetResult(ctrl))
                {
                    _logger?.LogInformation("WindowsAudioController created and ready.");
                }
                return;
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                _logger?.LogWarning(ex, "Failed to create WindowsAudioController; will retry in 1 minute.");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unexpected error creating WindowsAudioController; will retry in 1 minute.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        // If we exit without creating the controller, ensure awaiting calls fail
        _readyTcs.TrySetException(new ObjectDisposedException(nameof(RetryingAudioController)));
    }

    private void OnInnerVolumeChanged(object? sender, VolumeChangedEventArgs e)
    {
        VolumeChanged?.Invoke(this, e);
    }

    private IAudioController Inner => _readyTcs.Task.GetAwaiter().GetResult();

    public float GetVolumePercent()
    {
        ThrowIfDisposed();
        return Inner.GetVolumePercent();
    }

    public bool GetMuted()
    {
        ThrowIfDisposed();
        return Inner.GetMuted();
    }

    public void SetVolumePercent(float volumePercent)
    {
        ThrowIfDisposed();
        Inner.SetVolumePercent(volumePercent);
    }

    public void SetMuted(bool muted)
    {
        ThrowIfDisposed();
        Inner.SetMuted(muted);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts.Cancel();

        if (_readyTcs.Task.IsCompletedSuccessfully)
        {
            try
            {
                var inner = _readyTcs.Task.Result;
                inner.VolumeChanged -= OnInnerVolumeChanged;
                inner.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        _cts.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RetryingAudioController));
    }
}
