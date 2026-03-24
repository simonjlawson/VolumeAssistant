namespace VolumeAssistant.Service.CambridgeAudio;

/// <summary>
/// No-op implementation of <see cref="ICambridgeAudioClient"/> used when Cambridge Audio
/// integration is disabled (no host configured). All methods are no-ops.
/// </summary>
public sealed class NullCambridgeAudioClient : ICambridgeAudioClient
{
    public event EventHandler<CambridgeAudioStateChangedEventArgs>? StateChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<CambridgeAudioConnectionChangedEventArgs>? ConnectionChanged
    {
        add { }
        remove { }
    }

    public bool IsConnected => false;
    public CambridgeAudioInfo? Info => null;
    public IReadOnlyList<CambridgeAudioSource> Sources => Array.Empty<CambridgeAudioSource>();
    public CambridgeAudioState? State => null;

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DisconnectAsync() => Task.CompletedTask;
    public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetMuteAsync(bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetSourceAsync(string sourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetAudioOutputAsync(string output, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PowerOnAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PowerOffAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task NextTrackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PreviousTrackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetBalanceAsync(int balance, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<CambridgeAudioState> GetStateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new CambridgeAudioState());

    public Task<IReadOnlyList<CambridgeAudioSource>> GetSourcesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CambridgeAudioSource>>(Array.Empty<CambridgeAudioSource>());

    public Task<CambridgeAudioInfo> GetInfoAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new CambridgeAudioInfo());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
