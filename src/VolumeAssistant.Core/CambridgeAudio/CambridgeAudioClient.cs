using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace VolumeAssistant.Service.CambridgeAudio;

/// <summary>
/// WebSocket client for Cambridge Audio StreamMagic-enabled devices.
/// Ports the Python aiostreammagic library to C# for use in the VolumeAssistant Windows Service.
///
/// Protocol overview (from aiostreammagic):
///   - Connect to ws://{host}/smoip
///   - Requests: {"path": "/endpoint", "params": {...}}
///   - Responses: {"path": "...", "type": "response", "result": 200, "message": "...", "params": {"data": {...}}}
///   - Subscriptions push: {"path": "...", "type": "update", "params": {"data": {...}}}
/// </summary>
public sealed class CambridgeAudioClient : ICambridgeAudioClient
{
    // StreamMagic API endpoint paths (mirrors aiostreammagic/endpoints.py)
    private const string EndpointInfo = "/system/info";
    private const string EndpointSources = "/system/sources";
    private const string EndpointZoneState = "/zone/state";
    private const string EndpointPower = "/system/power";
    private const string EndpointPlayControl = "/zone/play_control";
    private const string EndpointAudio = "/zone/audio";

    private readonly CambridgeAudioOptions _options;
    private readonly ILogger<CambridgeAudioClient> _logger;

    // Pending request-response futures, keyed by endpoint path.
    // Multiple concurrent requests to the same path are queued as a list.
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TaskCompletionSource<JsonElement>>> _pendingRequests
        = new();

    // Active subscriptions: path -> handler delegate
    private readonly Dictionary<string, Func<JsonElement, Task>> _subscriptions = new();

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _connectCts;
    private Task? _receiveTask;
    private bool _attemptReconnection;
    private bool _disposed;
    // Used to signal the initial successful connection to callers of ConnectAsync.
    private TaskCompletionSource<bool>? _initialConnectedTcs;

    private CambridgeAudioInfo? _info;
    private IReadOnlyList<CambridgeAudioSource> _sources = Array.Empty<CambridgeAudioSource>();
    private CambridgeAudioState? _state;

    /// <inheritdoc />
    public event EventHandler<CambridgeAudioStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<CambridgeAudioConnectionChangedEventArgs>? ConnectionChanged;

    /// <inheritdoc />
    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    /// <inheritdoc />
    public CambridgeAudioInfo? Info => _info;

    /// <inheritdoc />
    public IReadOnlyList<CambridgeAudioSource> Sources => _sources;

    /// <inheritdoc />
    public CambridgeAudioState? State => _state;

    public CambridgeAudioClient(IOptions<CambridgeAudioOptions> options, ILogger<CambridgeAudioClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Allow the reconnect loop to run in the background while ensuring
        // this ConnectAsync returns as soon as an initial successful
        // connection is established so callers can continue.
        _attemptReconnection = true;

        var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _initialConnectedTcs = readyTcs;

        EventHandler<CambridgeAudioConnectionChangedEventArgs>? handler = null;
        handler = (s, e) =>
        {
            if (e.IsConnected)
            {
                readyTcs.TrySetResult(true);
                ConnectionChanged -= handler!;
            }
        };

        ConnectionChanged += handler;

        // Start the reconnect loop in the background. It will attempt
        // connection and keep the receive loop running; ConnectAsync will
        // complete once the ConnectionChanged event indicates success.
        _ = Task.Run(async () =>
        {
            try
            {
                await ConnectWithReconnectAsync(_connectCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_connectCts.IsCancellationRequested)
            {
                // ignore cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background Cambridge Audio reconnect loop faulted.");
            }
        });

        // Wait until initial connection is established or the caller cancels
        await readyTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ConnectWithReconnectAsync(CancellationToken cancellationToken)
    {
        int reconnectDelayMs = _options.InitialReconnectDelayMs;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await EstablishConnectionAsync(cancellationToken);
                reconnectDelayMs = _options.InitialReconnectDelayMs; // reset on success
                _attemptReconnection = true;

                // Wait for receive loop to end (disconnect / error)
                if (_receiveTask != null)
                    await _receiveTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection to Cambridge Audio device at {Host} failed.", _options.Host);
            }

            if (!_attemptReconnection || cancellationToken.IsCancellationRequested)
                break;

            ConnectionChanged?.Invoke(this, new CambridgeAudioConnectionChangedEventArgs(false));

            _logger.LogInformation(
                "Reconnecting to Cambridge Audio device in {Delay}ms…",
                reconnectDelayMs);

            await Task.Delay(reconnectDelayMs, cancellationToken).ConfigureAwait(false);
            reconnectDelayMs = Math.Min(reconnectDelayMs * 2, _options.MaxReconnectDelayMs);
        }
    }

    private async Task EstablishConnectionAsync(CancellationToken cancellationToken)
    {
        _webSocket?.Dispose();
        _webSocket = new ClientWebSocket();
        _webSocket.Options.SetRequestHeader("Origin", $"ws://{_options.Host}");

        var uri = new Uri($"ws://{_options.Host}:{_options.Port}/smoip");
        _logger.LogInformation("Connecting to Cambridge Audio device at {Uri}…", uri);

        await _webSocket.ConnectAsync(uri, cancellationToken);
        _logger.LogInformation("Connected to Cambridge Audio device at {Uri}.", uri);

        // Start receive loop before making any requests (responses arrive async)
        _receiveTask = ReceiveLoopAsync(_webSocket, cancellationToken);

        // Fetch initial state in parallel, mirroring Python's asyncio.gather()
        var infoTask = GetInfoAsync(cancellationToken);
        var sourcesTask = GetSourcesAsync(cancellationToken);
        var stateTask = GetStateAsync(cancellationToken);
        await Task.WhenAll(infoTask, sourcesTask, stateTask);

        _info = infoTask.Result;
        _sources = sourcesTask.Result;
        _state = stateTask.Result;

        _logger.LogInformation(
            "Cambridge Audio device ready: {Name} ({Model}), Source: {Source}, Volume: {Volume}%, Muted: {Muted}",
            _info.Name, _info.Model, _state.Source, _state.VolumePercent, _state.Mute);

        // Subscribe to zone state changes for live updates
        await SubscribeAsync(EndpointZoneState, HandleZoneStateUpdateAsync, cancellationToken);

        ConnectionChanged?.Invoke(this, new CambridgeAudioConnectionChangedEventArgs(true));
    }

    private async Task SubscribeAsync(
        string path,
        Func<JsonElement, Task> handler,
        CancellationToken cancellationToken)
    {
        _subscriptions[path] = handler;
        await SendAsync(path, new Dictionary<string, object?>
        {
            ["update"] = 100,
            ["zone"] = _options.Zone,
        }, cancellationToken);
    }

    private async Task HandleZoneStateUpdateAsync(JsonElement data)
    {
        var updated = data.Deserialize<CambridgeAudioState>(JsonOptions.Default);
        if (updated == null)
            return;

        _state = updated;
        _logger.LogDebug(
            "Cambridge Audio state update: Source={Source}, Volume={Volume}%, Mute={Mute}",
            updated.Source, updated.VolumePercent, updated.Mute);

        StateChanged?.Invoke(this, new CambridgeAudioStateChangedEventArgs(updated));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Public API – mirrors Python StreamMagicClient methods
    // ────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<CambridgeAudioInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(EndpointInfo, null, cancellationToken);
        var data = response.GetProperty("params").GetProperty("data");
        return data.Deserialize<CambridgeAudioInfo>(JsonOptions.Default)
            ?? throw new CambridgeAudioException("Failed to deserialize device info.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CambridgeAudioSource>> GetSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(EndpointSources, null, cancellationToken);
        var data = response.GetProperty("params").GetProperty("data").GetProperty("sources");
        return data.Deserialize<List<CambridgeAudioSource>>(JsonOptions.Default)
            ?? new List<CambridgeAudioSource>();
    }

    /// <inheritdoc />
    public async Task<CambridgeAudioState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(
            EndpointZoneState,
            new Dictionary<string, object?> { ["zone"] = _options.Zone },
            cancellationToken);

        var data = response.GetProperty("params").GetProperty("data");
        return data.Deserialize<CambridgeAudioState>(JsonOptions.Default)
            ?? throw new CambridgeAudioException("Failed to deserialize zone state.");
    }

    /// <inheritdoc />
    public async Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(volumePercent, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volumePercent, 100);

        await RequestAsync(
            EndpointZoneState,
            new Dictionary<string, object?> { ["zone"] = _options.Zone, ["volume_percent"] = volumePercent },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetMuteAsync(bool muted, CancellationToken cancellationToken = default)
    {
        await RequestAsync(
            EndpointZoneState,
            new Dictionary<string, object?> { ["zone"] = _options.Zone, ["mute"] = muted },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceId);

        await RequestAsync(
            EndpointZoneState,
            new Dictionary<string, object?> { ["zone"] = _options.Zone, ["source"] = sourceId },
            cancellationToken);
    }

    public async Task SetAudioOutputAsync(string output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);

        await RequestAsync(
            EndpointZoneState,
            new Dictionary<string, object?> { ["zone"] = _options.Zone, ["audio_output"] = output },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task PowerOnAsync(CancellationToken cancellationToken = default)
    {
        await RequestAsync(
            EndpointPower,
            new Dictionary<string, object?> { ["power"] = "ON" },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task PowerOffAsync(CancellationToken cancellationToken = default)
    {
        await RequestAsync(
            EndpointPower,
            new Dictionary<string, object?> { ["power"] = "NETWORK" },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task PlayPauseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RequestAsync(
                EndpointPlayControl,
                new Dictionary<string, object?> { ["match"] = "none", ["zone"] = _options.Zone, ["action"] = "toggle" },
                cancellationToken);
        }
        catch (CambridgeAudioException ex) when (ex.Message.Contains("Device returned error 400"))
        {
            _logger.LogInformation("Audio Source unable to perform PlayPause");
            return;
        }
    }

    /// <inheritdoc />
    public async Task NextTrackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RequestAsync(
                EndpointPlayControl,
                new Dictionary<string, object?> { ["match"] = "none", ["zone"] = _options.Zone, ["skip_track"] = 1 },
                cancellationToken);
        }
        catch (CambridgeAudioException ex) when (ex.Message.Contains("Device returned error 400"))
        {
            _logger.LogInformation("Audio Source unable to perform NextTrack");
            return;
        }
    }

    /// <inheritdoc />
    public async Task PreviousTrackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RequestAsync(
                EndpointPlayControl,
                new Dictionary<string, object?> { ["match"] = "none", ["zone"] = _options.Zone, ["skip_track"] = -1 },
                cancellationToken);
        }
        catch (CambridgeAudioException ex) when (ex.Message.Contains("Device returned error 400"))
        {
            _logger.LogInformation("Audio Source unable to perform PreviousTrack");
            return;
        }
    }

    /// <inheritdoc />
    public async Task SetBalanceAsync(int balance, CancellationToken cancellationToken = default)
    {
        if (balance < -15 || balance > 15)
            throw new ArgumentOutOfRangeException(nameof(balance), "Balance must be between -15 and 15.");

        await RequestAsync(
            EndpointAudio,
            new Dictionary<string, object?> { ["zone"] = _options.Zone, ["balance"] = balance },
            cancellationToken);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Disconnect / Dispose
    // ────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        _attemptReconnection = false;
        _connectCts?.Cancel();

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnecting",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WebSocket close handshake failed (non-fatal).");
            }
        }

        if (_receiveTask != null)
        {
            try { await _receiveTask; }
            catch { /* ignore */ }
        }

        ConnectionChanged?.Invoke(this, new CambridgeAudioConnectionChangedEventArgs(false));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectAsync();
        _webSocket?.Dispose();
        _connectCts?.Dispose();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Transport: request/response and receive loop
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a request and waits for the matching response.
    /// Mirrors the Python StreamMagicClient.request() method.
    /// </summary>
    private async Task<JsonElement> RequestAsync(
        string path,
        Dictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        EnsureConnected();

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        var queue = _pendingRequests.GetOrAdd(path, _ => new ConcurrentQueue<TaskCompletionSource<JsonElement>>());
        queue.Enqueue(tcs);

        try
        {
            await SendAsync(path, parameters, cancellationToken);
        }
        catch
        {
            // Remove our TCS from the queue since we won't be awaiting it
            CancelPendingRequestsForPath(path);
            throw;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.RequestTimeoutMs);

        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CambridgeAudioException(
                $"Request to {path} timed out after {_options.RequestTimeoutMs}ms.");
        }
    }

    /// <summary>
    /// Serializes and sends a JSON message over the WebSocket.
    /// Mirrors the Python StreamMagicClient._send() method.
    /// </summary>
    private async Task SendAsync(
        string path,
        Dictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        EnsureConnected();

        var message = new JsonObject
        {
            ["path"] = path,
            ["params"] = parameters != null
                ? BuildParamsNode(parameters)
                : new JsonObject(),
        };

        string json = message.ToJsonString();
        _logger.LogDebug("Sending to {Host}: {Message}", _options.Host, json);

        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket!.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    /// <summary>
    /// Receive loop – reads WebSocket messages and routes them to pending futures or subscription handlers.
    /// Mirrors the Python StreamMagicClient.consumer_handler() method.
    /// </summary>
    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];

        try
        {
            while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                string? rawMessage = await ReceiveFullMessageAsync(ws, buffer, cancellationToken);
                if (rawMessage == null)
                    break;

                _logger.LogDebug("Received from {Host}: {Message}", _options.Host, rawMessage);

                try
                {
                    using var doc = JsonDocument.Parse(rawMessage);
                    await RouteMessageAsync(doc.RootElement.Clone());
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse message from Cambridge Audio device.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket connection to Cambridge Audio device closed unexpectedly.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Cambridge Audio receive loop.");
        }
        finally
        {
            // Fail all pending requests so callers don't hang
            CancelAllPendingRequests(new CambridgeAudioException("WebSocket connection closed."));
        }
    }

    /// <summary>
    /// Reads a complete WebSocket message (potentially spanning multiple frames) into a string.
    /// </summary>
    private static async Task<string?> ReceiveFullMessageAsync(
        ClientWebSocket ws,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Routes an incoming JSON message to either a pending request TCS or a subscription handler.
    /// Mirrors the Python consumer_handler() dispatch logic.
    /// </summary>
    private async Task RouteMessageAsync(JsonElement msg)
    {
        if (!msg.TryGetProperty("path", out var pathElement))
            return;

        string path = pathElement.GetString() ?? string.Empty;

        bool isResponse = msg.TryGetProperty("type", out var typeElement)
            && typeElement.GetString() == "response";

        bool isUpdate = msg.TryGetProperty("type", out typeElement)
            && typeElement.GetString() == "update";

        // Route responses to pending request futures
        if (isResponse
            && _pendingRequests.TryGetValue(path, out var queue)
            && queue.TryDequeue(out var tcs))
        {
            if (msg.TryGetProperty("result", out var resultEl) && resultEl.GetInt32() != 200)
            {
                string errorMsg = msg.TryGetProperty("message", out var msgEl)
                    ? msgEl.GetString() ?? "Unknown error"
                    : "Unknown error";
                tcs.TrySetException(new CambridgeAudioException($"Device returned error {resultEl.GetInt32()}: {errorMsg}"));
            }
            else
            {
                tcs.TrySetResult(msg);
            }
        }

        // Route subscription updates to handlers
        if (isUpdate
            && _subscriptions.TryGetValue(path, out var handler)
            && msg.TryGetProperty("params", out var paramsEl)
            && paramsEl.TryGetProperty("data", out var dataEl))
        {
            try
            {
                await handler(dataEl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in subscription handler for path {Path}.", path);
            }
        }
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new CambridgeAudioException("Not connected to Cambridge Audio device.");
    }

    private void CancelPendingRequestsForPath(string path)
    {
        if (_pendingRequests.TryRemove(path, out var queue))
        {
            while (queue.TryDequeue(out var tcs))
                tcs.TrySetCanceled();
        }
    }

    private void CancelAllPendingRequests(Exception reason)
    {
        foreach (var kvp in _pendingRequests)
        {
            while (kvp.Value.TryDequeue(out var tcs))
                tcs.TrySetException(reason);
        }
        _pendingRequests.Clear();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────

    private static JsonObject BuildParamsNode(Dictionary<string, object?> parameters)
    {
        var node = new JsonObject();
        foreach (var (key, value) in parameters)
        {
            node[key] = value switch
            {
                null => null,
                bool b => JsonValue.Create(b),
                int i => JsonValue.Create(i),
                long l => JsonValue.Create(l),
                double d => JsonValue.Create(d),
                float f => JsonValue.Create(f),
                string s => JsonValue.Create(s),
                _ => JsonValue.Create(value.ToString()),
            };
        }
        return node;
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        static JsonOptions()
        {
            // Register a flexible boolean converter globally to tolerate device responses
            // that use strings like "on"/"off" or numeric values for boolean fields.
            Default.Converters.Add(new FlexibleBooleanConverter());
        }
    }
}
