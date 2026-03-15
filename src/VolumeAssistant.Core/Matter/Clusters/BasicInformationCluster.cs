namespace VolumeAssistant.Service.Matter.Clusters;

/// <summary>
/// Matter Basic Information Cluster (0x0028).
/// Provides identifying information about the device.
/// Matter Application Cluster Specification §11.1.
/// </summary>
public sealed class BasicInformationCluster : ICluster
{
    public ushort ClusterId => Clusters.ClusterId.BasicInformation;

    // Attribute IDs
    private const ushort AttrDataModelRevision = 0x0000;
    private const ushort AttrVendorName = 0x0001;
    private const ushort AttrVendorId = 0x0002;
    private const ushort AttrProductName = 0x0003;
    private const ushort AttrProductId = 0x0004;
    private const ushort AttrNodeLabel = 0x0005;
    private const ushort AttrLocation = 0x0006;
    private const ushort AttrHardwareVersion = 0x0007;
    private const ushort AttrHardwareVersionString = 0x0008;
    private const ushort AttrSoftwareVersion = 0x0009;
    private const ushort AttrSoftwareVersionString = 0x000A;
    private const ushort AttrCapabilityMinima = 0x0013;
    private const ushort AttrSpecificationVersion = 0x0015;

    public ushort DataModelRevision { get; set; } = 1;
    public string VendorName { get; set; } = "VolumeAssistant";
    public ushort VendorId { get; set; } = 0xFFF1; // Test vendor ID
    public string ProductName { get; set; } = "Windows Volume Controller";
    public ushort ProductId { get; set; } = 0x8001;
    public string NodeLabel { get; set; } = "Master Volume";
    public string Location { get; set; } = "XX";
    public ushort HardwareVersion { get; set; } = 0;
    public string HardwareVersionString { get; set; } = "1.0";
    public uint SoftwareVersion { get; set; } = 1;
    public string SoftwareVersionString { get; set; } = "1.0.0";
    public uint SpecificationVersion { get; set; } = 0x01030000; // Matter 1.3.0.0

    public object? ReadAttribute(ushort attributeId) => attributeId switch
    {
        AttrDataModelRevision => DataModelRevision,
        AttrVendorName => VendorName,
        AttrVendorId => VendorId,
        AttrProductName => ProductName,
        AttrProductId => ProductId,
        AttrNodeLabel => NodeLabel,
        AttrLocation => Location,
        AttrHardwareVersion => HardwareVersion,
        AttrHardwareVersionString => HardwareVersionString,
        AttrSoftwareVersion => SoftwareVersion,
        AttrSoftwareVersionString => SoftwareVersionString,
        AttrSpecificationVersion => SpecificationVersion,
        _ => null
    };

    public bool WriteAttribute(ushort attributeId, object value)
    {
        // Only NodeLabel and Location are writable
        if (attributeId == AttrNodeLabel && value is string label)
        {
            NodeLabel = label;
            return true;
        }
        if (attributeId == AttrLocation && value is string loc)
        {
            Location = loc;
            return true;
        }
        return false;
    }
}
