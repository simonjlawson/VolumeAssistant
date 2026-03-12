namespace VolumeAssistant.Service.Matter.Clusters;

/// <summary>
/// Well-known Matter cluster IDs as defined in the Matter Application Cluster Specification.
/// </summary>
public static class ClusterId
{
    public const ushort BasicInformation = 0x0028;
    public const ushort OnOff = 0x0006;
    public const ushort LevelControl = 0x0008;
    public const ushort Descriptor = 0x001D;
}

/// <summary>
/// Matter attribute access interface for reading and writing cluster attributes.
/// </summary>
public interface ICluster
{
    ushort ClusterId { get; }

    /// <summary>
    /// Reads a single attribute by attribute ID.
    /// Returns null if the attribute is not found.
    /// </summary>
    object? ReadAttribute(ushort attributeId);

    /// <summary>
    /// Writes a single attribute by attribute ID.
    /// Returns false if the attribute is not writeable or not found.
    /// </summary>
    bool WriteAttribute(ushort attributeId, object value);
}
