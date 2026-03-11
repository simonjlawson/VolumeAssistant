namespace VolumeAssistant.Service.Matter.Clusters;

/// <summary>
/// Matter On/Off Cluster (0x0006).
/// Maps to the audio mute/unmute state.
/// Matter Application Cluster Specification §1.5.
/// </summary>
public sealed class OnOffCluster : ICluster
{
    public ushort ClusterId => Clusters.ClusterId.OnOff;

    // Attribute IDs
    private const ushort AttrOnOff = 0x0000;
    private const ushort AttrGlobalSceneControl = 0x4000;
    private const ushort AttrOnTime = 0x4001;
    private const ushort AttrOffWaitTime = 0x4002;
    private const ushort AttrStartUpOnOff = 0x4003;
    private const ushort AttrClusterRevision = 0xFFFD;

    // Command IDs
    public const byte CmdOff = 0x00;
    public const byte CmdOn = 0x01;
    public const byte CmdToggle = 0x02;

    private bool _onOff = true;

    /// <summary>
    /// Gets or sets the on/off state (true = on/unmuted, false = off/muted).
    /// </summary>
    public bool OnOff
    {
        get => _onOff;
        set
        {
            if (_onOff != value)
            {
                _onOff = value;
                OnOffChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>Raised when the on/off state changes.</summary>
    public event EventHandler<bool>? OnOffChanged;

    public object? ReadAttribute(ushort attributeId) => attributeId switch
    {
        AttrOnOff => OnOff,
        AttrClusterRevision => (ushort)5,
        _ => null
    };

    public bool WriteAttribute(ushort attributeId, object value)
    {
        if (attributeId == AttrOnOff && value is bool boolVal)
        {
            OnOff = boolVal;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Processes an On/Off cluster command.
    /// </summary>
    public bool InvokeCommand(byte commandId)
    {
        switch (commandId)
        {
            case CmdOff:
                OnOff = false;
                return true;
            case CmdOn:
                OnOff = true;
                return true;
            case CmdToggle:
                OnOff = !OnOff;
                return true;
            default:
                return false;
        }
    }
}
