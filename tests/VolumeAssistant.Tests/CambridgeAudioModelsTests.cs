using System.Text.Json;
using VolumeAssistant.Service.CambridgeAudio;
using Xunit;

namespace VolumeAssistant.Tests;

/// <summary>
/// Tests for Cambridge Audio model deserialization and option classes.
/// These tests verify the JSON mapping from StreamMagic API responses to C# models,
/// matching the Python aiostreammagic models.py field definitions.
/// </summary>
public class CambridgeAudioModelsTests
{
    // ── CambridgeAudioInfo ────────────────────────────────────────────────────

    [Fact]
    public void CambridgeAudioInfo_Deserializes_FromStreamMagicResponse()
    {
        const string json = """
            {
                "name": "Living Room Amp",
                "model": "CXA81",
                "unit_id": "ABC123",
                "api": "1.4.0",
                "udn": "uuid:abc-123",
                "timezone": "Europe/London",
                "locale": "en_GB"
            }
            """;

        var info = JsonSerializer.Deserialize<CambridgeAudioInfo>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(info);
        Assert.Equal("Living Room Amp", info.Name);
        Assert.Equal("CXA81", info.Model);
        Assert.Equal("ABC123", info.UnitId);
        Assert.Equal("1.4.0", info.ApiVersion);
        Assert.Equal("uuid:abc-123", info.Udn);
    }

    // ── CambridgeAudioSource ─────────────────────────────────────────────────

    [Fact]
    public void CambridgeAudioSource_Deserializes_AllFields()
    {
        const string json = """
            {
                "id": "usb_audio",
                "name": "USB Audio",
                "default_name": "USB Audio",
                "nameable": true,
                "ui_selectable": true,
                "description": "USB Audio Device",
                "preferred_order": 1
            }
            """;

        var source = JsonSerializer.Deserialize<CambridgeAudioSource>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(source);
        Assert.Equal("usb_audio", source.Id);
        Assert.Equal("USB Audio", source.Name);
        Assert.True(source.Nameable);
        Assert.True(source.UiSelectable);
        Assert.Equal(1, source.PreferredOrder);
    }

    [Fact]
    public void CambridgeAudioSource_PreferredOrder_NullableWhenAbsent()
    {
        const string json = """
            {
                "id": "hdmi_arc",
                "name": "HDMI ARC",
                "default_name": "HDMI ARC",
                "nameable": false,
                "ui_selectable": true,
                "description": "HDMI ARC input"
            }
            """;

        var source = JsonSerializer.Deserialize<CambridgeAudioSource>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(source);
        Assert.Null(source.PreferredOrder);
    }

    [Fact]
    public void CambridgeAudioSource_ToString_ContainsNameAndId()
    {
        var source = new CambridgeAudioSource { Id = "usb_audio", Name = "USB Audio" };
        string str = source.ToString();
        Assert.Contains("USB Audio", str);
        Assert.Contains("usb_audio", str);
    }

    // ── CambridgeAudioState ──────────────────────────────────────────────────

    [Fact]
    public void CambridgeAudioState_Deserializes_WithVolume()
    {
        const string json = """
            {
                "source": "usb_audio",
                "power": true,
                "volume_percent": 42,
                "volume_db": -20,
                "mute": false,
                "pre_amp_mode": true,
                "pre_amp_state": true
            }
            """;

        var state = JsonSerializer.Deserialize<CambridgeAudioState>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(state);
        Assert.Equal("usb_audio", state.Source);
        Assert.True(state.Power);
        Assert.Equal(42, state.VolumePercent);
        Assert.Equal(-20, state.VolumeDb);
        Assert.False(state.Mute);
        Assert.True(state.PreAmpMode);
    }

    [Fact]
    public void CambridgeAudioState_Deserializes_WithNullVolume()
    {
        const string json = """
            {
                "source": "hdmi_arc",
                "power": true,
                "mute": true,
                "pre_amp_mode": false,
                "pre_amp_state": false
            }
            """;

        var state = JsonSerializer.Deserialize<CambridgeAudioState>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(state);
        Assert.Null(state.VolumePercent);
        Assert.True(state.Mute);
    }

    [Fact]
    public void CambridgeAudioState_Deserializes_PowerOffState()
    {
        const string json = """
            {
                "source": "",
                "power": false,
                "mute": false,
                "pre_amp_mode": false,
                "pre_amp_state": false
            }
            """;

        var state = JsonSerializer.Deserialize<CambridgeAudioState>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(state);
        Assert.False(state.Power);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"on\"", true)]
    [InlineData("\"off\"", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("\"1\"", true)]
    [InlineData("\"0\"", false)]
    public void CambridgeAudioState_Deserializes_PreAmpState_VariousFormats(string preAmpValue, bool expected)
    {
        string json = "{ " +
            "\"source\": \"test\"," +
            "\"power\": true," +
            "\"mute\": false," +
            "\"pre_amp_mode\": true," +
            "\"pre_amp_state\": " + preAmpValue +
            " }";

        var state = JsonSerializer.Deserialize<CambridgeAudioState>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(state);
        Assert.Equal(expected, state.PreAmpState);
    }

    // ── CambridgeAudioOptions ────────────────────────────────────────────────

    [Fact]
    public void CambridgeAudioOptions_IsEnabled_WhenHostSet()
    {
        var options = new CambridgeAudioOptions { Host = "192.168.1.10" };
        Assert.True(options.IsEnabled);
    }

    [Fact]
    public void CambridgeAudioOptions_IsDisabled_WhenHostEmpty()
    {
        var options = new CambridgeAudioOptions { Host = "" };
        Assert.False(options.IsEnabled);
    }

    [Fact]
    public void CambridgeAudioOptions_IsDisabled_WhenHostNull()
    {
        var options = new CambridgeAudioOptions { Host = null };
        Assert.False(options.IsEnabled);
    }

    [Fact]
    public void CambridgeAudioOptions_DefaultValues_AreReasonable()
    {
        var options = new CambridgeAudioOptions();
        Assert.Equal(80, options.Port);
        Assert.Equal("ZONE1", options.Zone);
        Assert.Equal(500, options.InitialReconnectDelayMs);
        Assert.Equal(30_000, options.MaxReconnectDelayMs);
        Assert.Equal(5_000, options.RequestTimeoutMs);
    }

    // ── Event Args ───────────────────────────────────────────────────────────

    [Fact]
    public void CambridgeAudioStateChangedEventArgs_StoresState()
    {
        var state = new CambridgeAudioState { Source = "usb_audio", VolumePercent = 50, Mute = false };
        var args = new CambridgeAudioStateChangedEventArgs(state);

        Assert.Same(state, args.State);
        Assert.Equal("usb_audio", args.State.Source);
        Assert.Equal(50, args.State.VolumePercent);
    }

    [Fact]
    public void CambridgeAudioConnectionChangedEventArgs_StoresConnectionState()
    {
        var connectedArgs = new CambridgeAudioConnectionChangedEventArgs(true);
        var disconnectedArgs = new CambridgeAudioConnectionChangedEventArgs(false);

        Assert.True(connectedArgs.IsConnected);
        Assert.False(disconnectedArgs.IsConnected);
    }

    // ── CambridgeAudioException ──────────────────────────────────────────────

    [Fact]
    public void CambridgeAudioException_HasMessage()
    {
        var ex = new CambridgeAudioException("Test error");
        Assert.Equal("Test error", ex.Message);
    }

    [Fact]
    public void CambridgeAudioException_HasInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new CambridgeAudioException("outer", inner);
        Assert.Equal(inner, ex.InnerException);
    }

    // ── NullCambridgeAudioClient ─────────────────────────────────────────────

    // ── NullCambridgeAudioClient (interface contract via local stub) ─────────

    [Fact]
    public async Task NullClient_IsNotConnected()
    {
        ICambridgeAudioClient client = new TestNullClient();
        await using (client as IAsyncDisposable)
        {
            Assert.False(client.IsConnected);
        }
    }

    [Fact]
    public async Task NullClient_ConnectAsync_DoesNotThrow()
    {
        ICambridgeAudioClient client = new TestNullClient();
        await client.ConnectAsync(); // should complete without exception
        await client.DisposeAsync();
    }

    [Fact]
    public async Task NullClient_SetVolumeAsync_DoesNotThrow()
    {
        ICambridgeAudioClient client = new TestNullClient();
        await client.SetVolumeAsync(50); // should be a no-op
        await client.DisposeAsync();
    }

    [Fact]
    public async Task NullClient_SetSourceAsync_DoesNotThrow()
    {
        ICambridgeAudioClient client = new TestNullClient();
        await client.SetSourceAsync("usb_audio");
        await client.DisposeAsync();
    }

    [Fact]
    public async Task NullClient_GetSourcesAsync_ReturnsEmpty()
    {
        ICambridgeAudioClient client = new TestNullClient();
        var sources = await client.GetSourcesAsync();
        Assert.Empty(sources);
        await client.DisposeAsync();
    }
}

/// <summary>
/// Minimal no-op ICambridgeAudioClient for testing interface contracts.
/// Mirrors the NullCambridgeAudioClient behaviour without accessing internals.
/// </summary>
file sealed class TestNullClient : ICambridgeAudioClient
{
    public event EventHandler<CambridgeAudioStateChangedEventArgs>? StateChanged { add { } remove { } }
    public event EventHandler<CambridgeAudioConnectionChangedEventArgs>? ConnectionChanged { add { } remove { } }

    public bool IsConnected => false;
    public CambridgeAudioInfo? Info => null;
    public IReadOnlyList<CambridgeAudioSource> Sources => Array.Empty<CambridgeAudioSource>();
    public CambridgeAudioState? State => null;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DisconnectAsync() => Task.CompletedTask;
    public Task SetVolumeAsync(int v, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetMuteAsync(bool m, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetSourceAsync(string sourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetAudioOutputAsync(string output, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PowerOnAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task PowerOffAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<CambridgeAudioState> GetStateAsync(CancellationToken ct = default)
        => Task.FromResult(new CambridgeAudioState());

    public Task<IReadOnlyList<CambridgeAudioSource>> GetSourcesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CambridgeAudioSource>>(Array.Empty<CambridgeAudioSource>());

    public Task<CambridgeAudioInfo> GetInfoAsync(CancellationToken ct = default)
        => Task.FromResult(new CambridgeAudioInfo());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
