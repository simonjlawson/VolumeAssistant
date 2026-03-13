using VolumeAssistant.Service.CambridgeAudio;
using Xunit;

namespace VolumeAssistant.Tests;

/// <summary>
/// Tests for <see cref="CambridgeAudioDiscovery"/> — specifically the header parsing logic
/// and the static class structure (network calls are not tested here).
/// </summary>
public class CambridgeAudioDiscoveryTests
{
    // ── ParseSsdpHeaders ─────────────────────────────────────────────────────

    [Fact]
    public void ParseSsdpHeaders_ParsesStandardSsdpResponse()
    {
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "CACHE-CONTROL: max-age=1800\r\n" +
            "DATE: Fri, 01 Jan 2021 00:00:00 GMT\r\n" +
            "EXT: \r\n" +
            "LOCATION: http://192.168.1.10:49152/description.xml\r\n" +
            "SERVER: StreamMagic/1.0 UPnP/1.0 MediaServer/1.0\r\n" +
            "ST: upnp:rootdevice\r\n" +
            "USN: uuid:abc-123::upnp:rootdevice\r\n";

        var headers = CambridgeAudioDiscovery.ParseSsdpHeaders(response);

        Assert.True(headers.ContainsKey("server"));
        Assert.Equal("StreamMagic/1.0 UPnP/1.0 MediaServer/1.0", headers["server"]);
        Assert.True(headers.ContainsKey("location"));
        Assert.Equal("http://192.168.1.10:49152/description.xml", headers["location"]);
    }

    [Fact]
    public void ParseSsdpHeaders_HeaderKeysAreCaseInsensitive()
    {
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "SERVER: StreamMagic/1.0\r\n";

        var headers = CambridgeAudioDiscovery.ParseSsdpHeaders(response);

        // Should be accessible regardless of case
        Assert.True(headers.ContainsKey("SERVER"));
        Assert.True(headers.ContainsKey("server"));
        Assert.True(headers.ContainsKey("Server"));
    }

    [Fact]
    public void ParseSsdpHeaders_SkipsFirstLine_StatusLine()
    {
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "SERVER: StreamMagic/1.0\r\n";

        var headers = CambridgeAudioDiscovery.ParseSsdpHeaders(response);

        // The status line "HTTP/1.1 200 OK" should not appear as a header key
        Assert.False(headers.ContainsKey("HTTP/1.1 200 OK"));
        Assert.False(headers.ContainsKey("HTTP/1.1 200 OK\r\n"));
    }

    [Fact]
    public void ParseSsdpHeaders_ReturnsEmptyDict_ForEmptyResponse()
    {
        var headers = CambridgeAudioDiscovery.ParseSsdpHeaders(string.Empty);
        Assert.Empty(headers);
    }

    [Fact]
    public void ParseSsdpHeaders_ReturnsEmptyDict_ForStatusLineOnly()
    {
        var headers = CambridgeAudioDiscovery.ParseSsdpHeaders("HTTP/1.1 200 OK");
        Assert.Empty(headers);
    }

    [Fact]
    public void ParseSsdpHeaders_HandlesLfOnlyLineEndings()
    {
        const string response =
            "HTTP/1.1 200 OK\n" +
            "SERVER: StreamMagic/2.0\n";

        var headers = CambridgeAudioDiscovery.ParseSsdpHeaders(response);

        Assert.True(headers.ContainsKey("server"));
        Assert.Equal("StreamMagic/2.0", headers["server"]);
    }

    [Fact]
    public void ParseSsdpHeaders_TrimsWhitespaceFromValues()
    {
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "SERVER:   StreamMagic/1.0   \r\n";

        var headers = CambridgeAudioDiscovery.ParseSsdpHeaders(response);

        Assert.Equal("StreamMagic/1.0", headers["server"]);
    }

    // ── IsStreamMagicDevice ──────────────────────────────────────────────────

    [Theory]
    [InlineData("StreamMagic/1.0 UPnP/1.0")]
    [InlineData("StreamMagic/2.0")]
    [InlineData("streammagic/1.0")]   // case-insensitive match
    public void ParseSsdpHeaders_StreamMagicServerHeader_StartsWithStreamMagic(string serverValue)
    {
        string response = $"HTTP/1.1 200 OK\r\nSERVER: {serverValue}\r\n";
        var headers = CambridgeAudioDiscovery.ParseSsdpHeaders(response);

        Assert.StartsWith("StreamMagic", headers["server"], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Linux/2.6 UPnP/1.0 MediaServer/1.0")]
    [InlineData("Microsoft-Windows/10.0 UPnP/1.0")]
    [InlineData("")]
    public void ParseSsdpHeaders_NonStreamMagicServerHeader_DoesNotStartWithStreamMagic(string serverValue)
    {
        string response = $"HTTP/1.1 200 OK\r\nSERVER: {serverValue}\r\n";
        var headers = CambridgeAudioDiscovery.ParseSsdpHeaders(response);

        Assert.DoesNotContain("StreamMagic", headers["server"], StringComparison.OrdinalIgnoreCase);
    }

    // ── DiscoverFirstAsync / DiscoverAsync ───────────────────────────────────
    // Network tests are excluded; only verifies the API exists and returns a valid Task.

    [Fact]
    public void DiscoverFirstAsync_ReturnsTask()
    {
        // Just verify the method exists and returns a Task (does not throw synchronously)
        using var cts = new CancellationTokenSource(millisecondsDelay: 1);
        var task = CambridgeAudioDiscovery.DiscoverFirstAsync(cts.Token);
        Assert.NotNull(task);
    }

    [Fact]
    public void DiscoverAsync_ReturnsTask()
    {
        using var cts = new CancellationTokenSource(millisecondsDelay: 1);
        var task = CambridgeAudioDiscovery.DiscoverAsync(cts.Token);
        Assert.NotNull(task);
    }
}
