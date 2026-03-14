namespace VolumeAssistant.Service.Matter;

public sealed class MatterOptions
{
    public const string SectionName = "VolumeAssistant:Matter";

    /// <summary>
    /// When true the Matter UDP server and mDNS advertiser are started. Defaults to false.
    /// </summary>
    public bool Enabled { get; set; } = false;
}
