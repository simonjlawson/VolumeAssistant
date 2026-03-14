using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VolumeAssistant.Service.CambridgeAudio;

/// <summary>
/// Discovers Cambridge Audio StreamMagic devices on the local network
/// using SSDP (Simple Service Discovery Protocol) multicast.
/// Based on https://github.com/sebk-666/stream_magic/blob/master/stream_magic/discovery.py
/// </summary>
public static class CambridgeAudioDiscovery
{
    private const string SsdpMulticastAddress = "239.255.255.250";
    private const int SsdpPort = 1900;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private static readonly byte[] MSearchMessage = Encoding.ASCII.GetBytes(
        "M-SEARCH * HTTP/1.1\r\n" +
        "HOST:239.255.255.250:1900\r\n" +
        "ST:upnp:rootdevice\r\n" +
        "MX:2\r\n" +
        "MAN:\"ssdp:discover\"\r\n" +
        "\r\n");

    /// <summary>
    /// Discovers the first StreamMagic device on the local network.
    /// Returns the IP address of the discovered device, or <c>null</c> if none found within the timeout.
    /// </summary>
    public static async Task<string?> DiscoverFirstAsync(CancellationToken cancellationToken = default)
    {
        var devices = await DiscoverAsync(cancellationToken);
        return devices.Count > 0 ? devices[0] : null;
    }

    /// <summary>
    /// Discovers all StreamMagic devices on the local network via SSDP multicast.
    /// Returns a list of IP addresses of discovered devices.
    /// </summary>
    public static async Task<IReadOnlyList<string>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var discovered = new List<string>();

        using var udpClient = new UdpClient();
        udpClient.Client.ReceiveTimeout = (int)DefaultTimeout.TotalMilliseconds;

        var multicastEndpoint = new IPEndPoint(IPAddress.Parse(SsdpMulticastAddress), SsdpPort);

        try
        {
            await udpClient.SendAsync(MSearchMessage, MSearchMessage.Length, multicastEndpoint);
        }
        catch (Exception)
        {
            return discovered;
        }

        var deadline = DateTimeOffset.UtcNow.Add(DefaultTimeout);

        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) break;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(remaining);

                var result = await udpClient.ReceiveAsync(cts.Token);
                var responseText = Encoding.UTF8.GetString(result.Buffer);

                var headers = ParseSsdpHeaders(responseText);

                // Only include StreamMagic devices (matches Python: data['server'].startswith("StreamMagic"))
                if (headers.TryGetValue("server", out var server) &&
                    server.StartsWith("StreamMagic", StringComparison.OrdinalIgnoreCase))
                {
                    var ip = result.RemoteEndPoint.Address.ToString();
                    if (!discovered.Contains(ip))
                        discovered.Add(ip);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }

        return discovered;
    }

    /// <summary>
    /// Parses HTTP-like SSDP response headers into a case-insensitive dictionary.
    /// Mirrors the header parsing in the Python reference implementation.
    /// </summary>
    internal static Dictionary<string, string> ParseSsdpHeaders(string response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Skip the first line (HTTP status line, e.g. "HTTP/1.1 200 OK")
        foreach (var line in lines.Skip(1))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();
                headers[key] = value;
            }
        }

        return headers;
    }
}
