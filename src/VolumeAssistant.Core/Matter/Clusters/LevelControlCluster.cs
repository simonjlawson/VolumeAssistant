namespace VolumeAssistant.Service.Matter.Clusters;

/// <summary>
/// Matter Level Control Cluster (0x0008).
/// Maps volume level (0–100%) to Matter level (0–254).
/// Matter Application Cluster Specification §1.6.
/// </summary>
public sealed class LevelControlCluster : ICluster
{
    public ushort ClusterId => Clusters.ClusterId.LevelControl;

    // Attribute IDs
    private const ushort AttrCurrentLevel = 0x0000;
    private const ushort AttrRemainingTime = 0x0001;
    private const ushort AttrMinLevel = 0x0002;
    private const ushort AttrMaxLevel = 0x0003;
    private const ushort AttrCurrentFrequency = 0x0004;
    private const ushort AttrMinFrequency = 0x0005;
    private const ushort AttrMaxFrequency = 0x0006;
    private const ushort AttrOnOffTransitionTime = 0x0010;
    private const ushort AttrOnLevel = 0x0011;
    private const ushort AttrOnTransitionTime = 0x0012;
    private const ushort AttrOffTransitionTime = 0x0013;
    private const ushort AttrDefaultMoveRate = 0x0014;
    private const ushort AttrOptions = 0x000F;
    private const ushort AttrStartUpCurrentLevel = 0x4000;
    private const ushort AttrClusterRevision = 0xFFFD;

    // Command IDs
    public const byte CmdMoveToLevel = 0x00;
    public const byte CmdMove = 0x01;
    public const byte CmdStep = 0x02;
    public const byte CmdStop = 0x03;
    public const byte CmdMoveToLevelWithOnOff = 0x04;
    public const byte CmdMoveWithOnOff = 0x05;
    public const byte CmdStepWithOnOff = 0x06;
    public const byte CmdStopWithOnOff = 0x07;

    /// <summary>
    /// Matter level value range: 0–254 (null = not set / undefined).
    /// </summary>
    public const byte MinMatterLevel = 0;
    public const byte MaxMatterLevel = 254;

    private byte? _currentLevel;

    /// <summary>
    /// Gets or sets the current level in Matter range (0–254).
    /// </summary>
    public byte? CurrentLevel
    {
        get => _currentLevel;
        set
        {
            if (_currentLevel != value)
            {
                _currentLevel = value;
                CurrentLevelChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>Raised when the current level changes.</summary>
    public event EventHandler<byte?>? CurrentLevelChanged;

    /// <summary>
    /// Converts a volume percentage (0–100) to a Matter level (0–254).
    /// </summary>
    public static byte VolumePercentToMatterLevel(float volumePercent)
    {
        float clamped = Math.Clamp(volumePercent, 0f, 100f);
        return (byte)Math.Round(clamped / 100f * MaxMatterLevel);
    }

    /// <summary>
    /// Converts a Matter level (0–254) to a volume percentage (0–100).
    /// </summary>
    public static float MatterLevelToVolumePercent(byte matterLevel)
    {
        return (float)matterLevel / MaxMatterLevel * 100f;
    }

    public object? ReadAttribute(ushort attributeId) => attributeId switch
    {
        AttrCurrentLevel => CurrentLevel,
        AttrMinLevel => (byte)MinMatterLevel,
        AttrMaxLevel => (byte)MaxMatterLevel,
        AttrRemainingTime => (ushort)0,
        AttrOptions => (byte)0,
        AttrOnLevel => (byte?)null,
        AttrClusterRevision => (ushort)5,
        _ => null
    };

    public bool WriteAttribute(ushort attributeId, object value)
    {
        if (attributeId == AttrCurrentLevel)
        {
            CurrentLevel = value is byte b ? b : null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Processes a Level Control cluster command payload.
    /// Returns the target level to move to, or null if no level change.
    /// </summary>
    public byte? InvokeMoveToLevel(byte level)
    {
        byte clamped = (byte)Math.Clamp(level, MinMatterLevel, MaxMatterLevel);
        CurrentLevel = clamped;
        return clamped;
    }
}
