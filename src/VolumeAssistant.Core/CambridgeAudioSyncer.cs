using System;
using System.Threading.Tasks;
using VolumeAssistant.Service.CambridgeAudio;
using Microsoft.Extensions.Logging;

namespace VolumeAssistant.Service;

/// <summary>
/// Encapsulates logic to coalesce rapid Windows volume change events and
/// apply them to a Cambridge Audio device in a single, debounced operation.
/// </summary>
internal sealed class CambridgeAudioSyncer : IAsyncDisposable
{
    private readonly ICambridgeAudioClient _client;
    private readonly CambridgeAudioOptions _options;
    private readonly ILogger _logger;

    private readonly object _lock = new object();
    private int? _pendingVolume;
    private bool? _pendingMute;
    private Task? _workerTask;
    private bool _disposed;

    public CambridgeAudioSyncer(
        ICambridgeAudioClient client,
        CambridgeAudioOptions options,
        ILogger logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Enqueue a desired volume/mute to be applied to the device. Rapid calls
    /// will be coalesced so only the latest values are applied.
    /// </summary>
    public void Enqueue(int? volumePercent, bool? mute)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CambridgeAudioSyncer));

        lock (_lock)
        {
            if (volumePercent.HasValue)
                _pendingVolume = volumePercent.Value;
            if (mute.HasValue)
                _pendingMute = mute.Value;

            if (_workerTask == null || _workerTask.IsCompleted)
            {
                _workerTask = Task.Run(ProcessAsync);
            }
        }
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            int? vol;
            bool? m;
            lock (_lock)
            {
                vol = _pendingVolume;
                m = _pendingMute;
                _pendingVolume = null;
                _pendingMute = null;
            }

            if (vol == null && m == null)
            {
                lock (_lock)
                {
                    _workerTask = null;
                }
                return;
            }

            try
            {
                if (_client != null && _client.IsConnected)
                {
                    if (vol.HasValue)
                        await _client.SetVolumeAsync(vol.Value).ConfigureAwait(false);
                    if (m.HasValue)
                        await _client.SetMuteAsync(m.Value).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync to Cambridge Audio device.");
            }

            // Loop to pick up any coalesced updates
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Task? t;
        lock (_lock)
        {
            t = _workerTask;
        }
        if (t != null)
        {
            try { await t.ConfigureAwait(false); } catch { }
        }
    }
}
