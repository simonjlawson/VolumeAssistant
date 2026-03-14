using Makaretu.Dns;

namespace VolumeAssistant.Service.Matter;

/// <summary>
/// Advertises the Matter device on the local network using mDNS/DNS-SD.
/// Matter devices advertise using:
///   - Service type: _matter._tcp (for commissioned devices)
///   - Service type: _matterc._udp (for uncommissioned/commissioning window open)
/// Matter Core Specification §4.3.1.
/// </summary>
public sealed class MdnsAdvertiser : IDisposable
{
    private readonly MatterDevice _device;
    private readonly ILogger<MdnsAdvertiser> _logger;
    private MulticastService? _mdnsService;
    private ServiceDiscovery? _serviceDiscovery;
    private ServiceProfile? _commissioningProfile;
    private ServiceProfile? _operationalProfile;
    private bool _disposed;

    /// <summary>UDP port Matter devices listen on.</summary>
    public const int MatterPort = 5540;

    /// <summary>Service type for uncommissioned/commissioning Matter devices.</summary>
    public const string CommissioningServiceType = "_matterc._udp";

    /// <summary>Service type for commissioned Matter devices.</summary>
    public const string OperationalServiceType = "_matter._tcp";

    public MdnsAdvertiser(MatterDevice device, ILogger<MdnsAdvertiser> logger)
    {
        _device = device;
        _logger = logger;
    }

    /// <summary>
    /// Starts mDNS advertising for both commissioning discovery and operational discovery.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _mdnsService = new MulticastService();
        _serviceDiscovery = new ServiceDiscovery(_mdnsService);

        // Commissioning advertisement (_matterc._udp)
        // TXT records per Matter Core Specification §4.3.1.1
        _commissioningProfile = new ServiceProfile(
            _device.InstanceName,
            CommissioningServiceType,
            MatterPort);

        _commissioningProfile.AddProperty("D", _device.Discriminator.ToString());
        _commissioningProfile.AddProperty("CM", "1");           // Commissioning Mode
        _commissioningProfile.AddProperty("DT", "256");         // Device Type: Dimmable Light (0x0100)
        _commissioningProfile.AddProperty("DN", "VolumeAssistant"); // Device Name
        _commissioningProfile.AddProperty("VP", $"{_device.VendorId}+{_device.ProductId}");
        _commissioningProfile.AddProperty("RI", "0");           // Rotating ID (0 = not used)
        _commissioningProfile.AddProperty("PI", "");            // Pairing Hint (empty = PIN)
        _commissioningProfile.AddProperty("PH", "33");          // Pairing Hint flags

        // Operational advertisement (_matter._tcp) - advertise with fabric and node info
        // For unprovisioned state we use the discriminator as a placeholder node ID
        string fabricNodeId = $"{_device.Discriminator:X16}-{_device.Discriminator:X16}";
        _operationalProfile = new ServiceProfile(
            fabricNodeId,
            OperationalServiceType,
            MatterPort);

        _operationalProfile.AddProperty("SII", "500");  // Subscription Interval Idle (ms)
        _operationalProfile.AddProperty("SAI", "300");  // Subscription Interval Active (ms)
        _operationalProfile.AddProperty("T", "0");      // TCP support: no

        _serviceDiscovery.Advertise(_commissioningProfile);
        _serviceDiscovery.Advertise(_operationalProfile);

        _mdnsService.Start();

        _logger.LogInformation(
            "mDNS advertisement started. Instance: {InstanceName}, Discriminator: {Discriminator}, Port: {Port}",
            _device.InstanceName, _device.Discriminator, MatterPort);
    }

    /// <summary>
    /// Stops the mDNS advertisement.
    /// </summary>
    public void Stop()
    {
        if (_serviceDiscovery != null && _commissioningProfile != null)
            _serviceDiscovery.Unadvertise(_commissioningProfile);
        if (_serviceDiscovery != null && _operationalProfile != null)
            _serviceDiscovery.Unadvertise(_operationalProfile);

        _mdnsService?.Stop();
        _logger.LogInformation("mDNS advertisement stopped.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _serviceDiscovery?.Dispose();
        _mdnsService?.Dispose();
    }
}
