using VolumeAssistant.Service.Matter.Clusters;

namespace VolumeAssistant.Service.Matter;

/// <summary>
/// Represents a Matter device endpoint with its associated clusters.
/// An endpoint corresponds to a logical device function.
/// </summary>
public sealed class MatterEndpoint
{
    /// <summary>Endpoint ID (0 = root/aggregator, 1+ = device endpoints).</summary>
    public byte EndpointId { get; }

    /// <summary>Matter device type ID for this endpoint.</summary>
    public uint DeviceTypeId { get; }

    /// <summary>Device type revision.</summary>
    public ushort DeviceTypeRevision { get; }

    private readonly Dictionary<ushort, ICluster> _clusters = new();

    /// <summary>
    /// Standard Matter device type ID for a Dimmable Light (0x0100).
    /// Matter Application Cluster Specification §1.1.
    /// </summary>
    public const uint DeviceTypeDimmableLight = 0x0100;

    /// <summary>
    /// Standard Matter device type ID for the Root Node.
    /// </summary>
    public const uint DeviceTypeRootNode = 0x0016;

    public MatterEndpoint(byte endpointId, uint deviceTypeId, ushort deviceTypeRevision = 1)
    {
        EndpointId = endpointId;
        DeviceTypeId = deviceTypeId;
        DeviceTypeRevision = deviceTypeRevision;
    }

    /// <summary>Adds a cluster to this endpoint.</summary>
    public void AddCluster(ICluster cluster)
    {
        _clusters[cluster.ClusterId] = cluster;
    }

    /// <summary>Gets a cluster by ID, or null if not present.</summary>
    public ICluster? GetCluster(ushort clusterId)
        => _clusters.TryGetValue(clusterId, out var cluster) ? cluster : null;

    /// <summary>Gets all cluster IDs on this endpoint.</summary>
    public IEnumerable<ushort> ClusterIds => _clusters.Keys;

    /// <summary>Gets all clusters on this endpoint.</summary>
    public IEnumerable<ICluster> Clusters => _clusters.Values;
}

/// <summary>
/// Represents the Matter device with all its endpoints and clusters.
/// This models the Windows master volume as a dimmable light device
/// (endpoint 1) where the level maps to volume percentage.
/// </summary>
public sealed class MatterDevice
{
    private readonly Dictionary<byte, MatterEndpoint> _endpoints = new();

    /// <summary>Gets the unique device discriminator (12-bit value, 0–4095).</summary>
    public ushort Discriminator { get; }

    /// <summary>Gets the device passcode for commissioning.</summary>
    public uint Passcode { get; }

    /// <summary>Gets the device vendor ID.</summary>
    public ushort VendorId { get; }

    /// <summary>Gets the device product ID.</summary>
    public ushort ProductId { get; }

    /// <summary>Gets the instance name for mDNS (12 hex chars of discriminator + random).</summary>
    public string InstanceName { get; }

    public OnOffCluster OnOffCluster { get; }
    public LevelControlCluster LevelControlCluster { get; }
    public BasicInformationCluster BasicInformationCluster { get; }

    /// <summary>Raised when either the volume level or mute state changes from within the device model.</summary>
    public event EventHandler<(byte Level, bool IsOn)>? DeviceStateChanged;

    public MatterDevice(ushort discriminator = 3840, uint passcode = 20202021,
        ushort vendorId = 0xFFF1, ushort productId = 0x8001)
    {
        Discriminator = discriminator;
        Passcode = passcode;
        VendorId = vendorId;
        ProductId = productId;

        // Generate a stable instance name from discriminator
        InstanceName = GenerateInstanceName(discriminator);

        BasicInformationCluster = new BasicInformationCluster
        {
            VendorId = vendorId,
            ProductId = productId,
        };
        OnOffCluster = new OnOffCluster();
        LevelControlCluster = new LevelControlCluster();

        // Root endpoint (0) - required by Matter spec
        var rootEndpoint = new MatterEndpoint(0, MatterEndpoint.DeviceTypeRootNode);
        rootEndpoint.AddCluster(BasicInformationCluster);
        AddEndpoint(rootEndpoint);

        // Device endpoint (1) - volume control device
        var deviceEndpoint = new MatterEndpoint(1, MatterEndpoint.DeviceTypeDimmableLight);
        deviceEndpoint.AddCluster(OnOffCluster);
        deviceEndpoint.AddCluster(LevelControlCluster);
        AddEndpoint(deviceEndpoint);

        OnOffCluster.OnOffChanged += (_, isOn) =>
        {
            DeviceStateChanged?.Invoke(this, (LevelControlCluster.CurrentLevel ?? 0, isOn));
        };

        LevelControlCluster.CurrentLevelChanged += (_, level) =>
        {
            DeviceStateChanged?.Invoke(this, (level ?? 0, OnOffCluster.OnOff));
        };
    }

    /// <summary>Adds an endpoint to the device.</summary>
    public void AddEndpoint(MatterEndpoint endpoint)
    {
        _endpoints[endpoint.EndpointId] = endpoint;
    }

    /// <summary>Gets an endpoint by ID, or null if not present.</summary>
    public MatterEndpoint? GetEndpoint(byte endpointId)
        => _endpoints.TryGetValue(endpointId, out var ep) ? ep : null;

    /// <summary>Gets all endpoints.</summary>
    public IEnumerable<MatterEndpoint> Endpoints => _endpoints.Values;

    /// <summary>
    /// Updates the device level from the current Windows volume.
    /// </summary>
    public void UpdateFromVolume(float volumePercent, bool isMuted)
    {
        byte matterLevel = LevelControlCluster.VolumePercentToMatterLevel(volumePercent);

        // Suppress internal change events by directly setting underlying state
        LevelControlCluster.CurrentLevel = matterLevel;
        OnOffCluster.OnOff = !isMuted;
    }

    private static string GenerateInstanceName(ushort discriminator)
    {
        // Matter instance names are 16 hex chars using the node ID or random bytes
        byte[] bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        // Embed discriminator in first 2 bytes for stability within session
        bytes[0] = (byte)(discriminator & 0xFF);
        bytes[1] = (byte)((discriminator >> 8) & 0xFF);
        return Convert.ToHexString(bytes).ToUpperInvariant();
    }
}
