namespace VolumeAssistant.Service.Matter.Protocol;

/// <summary>
/// Matter message flags as defined in the Matter specification.
/// </summary>
[Flags]
public enum MessageFlags : byte
{
    None = 0x00,
    VersionMask = 0xF0,
    SourceNodeIdPresent = 0x04,
    DestinationNodeIdPresent = 0x01,
    DestinationGroupIdPresent = 0x02,
}

/// <summary>
/// Matter security flags as defined in the Matter specification.
/// </summary>
[Flags]
public enum SecurityFlags : byte
{
    None = 0x00,
    PrivacyEnhanced = 0x80,
    ControlMessage = 0x40,
    MessageExtensionPresent = 0x20,
    SessionTypeMask = 0x03,
    GroupSession = 0x01,
    UnicastSession = 0x00,
}

/// <summary>
/// Represents a Matter protocol message header.
/// Encodes/decodes according to Matter Core Specification §4.6.
/// </summary>
public sealed class MatterMessageHeader
{
    /// <summary>Message flags byte.</summary>
    public MessageFlags Flags { get; set; }

    /// <summary>Session identifier (0 = unsecured unicast).</summary>
    public ushort SessionId { get; set; }

    /// <summary>Security flags byte.</summary>
    public SecurityFlags SecurityFlags { get; set; }

    /// <summary>Message counter for deduplication and replay protection.</summary>
    public uint MessageCounter { get; set; }

    /// <summary>Optional source Node ID (present when SourceNodeIdPresent flag is set).</summary>
    public ulong? SourceNodeId { get; set; }

    /// <summary>Optional destination Node ID (present when DestinationNodeIdPresent flag is set).</summary>
    public ulong? DestinationNodeId { get; set; }

    /// <summary>The message payload bytes (after the header).</summary>
    public ReadOnlyMemory<byte> Payload { get; set; } = ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// Deserializes a Matter message header from raw bytes.
    /// </summary>
    public static MatterMessageHeader Decode(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 8)
            throw new ArgumentException("Data too short for Matter message header.", nameof(data));
        var header = new MatterMessageHeader();
        var span = data.Span;
        int pos = 0;

        header.Flags = (MessageFlags)span[pos++];
        header.SessionId = (ushort)(span[pos] | (span[pos + 1] << 8));
        pos += 2;
        header.SecurityFlags = (SecurityFlags)span[pos++];
        header.MessageCounter = (uint)(span[pos] | (span[pos + 1] << 8)
            | (span[pos + 2] << 16) | (span[pos + 3] << 24));
        pos += 4;

        if (header.Flags.HasFlag(MessageFlags.SourceNodeIdPresent))
        {
            header.SourceNodeId = ReadUInt64(span, pos);
            pos += 8;
        }

        if (header.Flags.HasFlag(MessageFlags.DestinationNodeIdPresent))
        {
            header.DestinationNodeId = ReadUInt64(span, pos);
            pos += 8;
        }

        header.Payload = data.Slice(pos);
        return header;
    }

    /// <summary>
    /// Serializes the Matter message header to bytes.
    /// </summary>
    public byte[] Encode()
    {
        var buffer = new List<byte>(32 + Payload.Length);

        MessageFlags flags = Flags;
        if (SourceNodeId.HasValue)
            flags |= MessageFlags.SourceNodeIdPresent;
        if (DestinationNodeId.HasValue)
            flags |= MessageFlags.DestinationNodeIdPresent;

        buffer.Add((byte)flags);
        buffer.Add((byte)(SessionId & 0xFF));
        buffer.Add((byte)((SessionId >> 8) & 0xFF));
        buffer.Add((byte)SecurityFlags);
        buffer.Add((byte)(MessageCounter & 0xFF));
        buffer.Add((byte)((MessageCounter >> 8) & 0xFF));
        buffer.Add((byte)((MessageCounter >> 16) & 0xFF));
        buffer.Add((byte)((MessageCounter >> 24) & 0xFF));

        if (SourceNodeId.HasValue)
            AppendUInt64(buffer, SourceNodeId.Value);

        if (DestinationNodeId.HasValue)
            AppendUInt64(buffer, DestinationNodeId.Value);

        // Payload is ReadOnlyMemory<byte>; copy its contents into the buffer
        if (!Payload.IsEmpty)
            buffer.AddRange(Payload.Span.ToArray());

        return buffer.ToArray();
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, int pos)
    {
        return (ulong)data[pos]
            | ((ulong)data[pos + 1] << 8)
            | ((ulong)data[pos + 2] << 16)
            | ((ulong)data[pos + 3] << 24)
            | ((ulong)data[pos + 4] << 32)
            | ((ulong)data[pos + 5] << 40)
            | ((ulong)data[pos + 6] << 48)
            | ((ulong)data[pos + 7] << 56);
    }

    private static void AppendUInt64(List<byte> buffer, ulong value)
    {
        buffer.Add((byte)(value & 0xFF));
        buffer.Add((byte)((value >> 8) & 0xFF));
        buffer.Add((byte)((value >> 16) & 0xFF));
        buffer.Add((byte)((value >> 24) & 0xFF));
        buffer.Add((byte)((value >> 32) & 0xFF));
        buffer.Add((byte)((value >> 40) & 0xFF));
        buffer.Add((byte)((value >> 48) & 0xFF));
        buffer.Add((byte)((value >> 56) & 0xFF));
    }
}

/// <summary>
/// Matter Interaction Model Protocol Data Unit (PDU) types.
/// Corresponds to Matter Core Specification §10.6.
/// </summary>
public enum InteractionModelOpCode : byte
{
    StatusResponse = 0x01,
    ReadRequest = 0x02,
    SubscribeRequest = 0x03,
    SubscribeResponse = 0x04,
    ReportData = 0x05,
    WriteRequest = 0x06,
    WriteResponse = 0x07,
    InvokeCommandRequest = 0x08,
    InvokeCommandResponse = 0x09,
    TimedRequest = 0x0A,
}

/// <summary>
/// Matter common protocol message types (exchange protocol).
/// </summary>
public enum ExchangeProtocol : ushort
{
    SecureChannel = 0x0000,
    InteractionModel = 0x0001,
}

/// <summary>
/// Represents an exchange PDU in the Matter protocol.
/// </summary>
public sealed class ExchangeHeader
{
    public byte ExchangeFlags { get; set; }
    public byte Opcode { get; set; }
    public ushort ExchangeId { get; set; }
    public ushort ProtocolId { get; set; }
    public byte[]? AckMessageCounter { get; set; }
    public ReadOnlyMemory<byte> ApplicationPayload { get; set; } = ReadOnlyMemory<byte>.Empty;

    public const byte FlagInitiator = 0x01;
    public const byte FlagAckMsg = 0x02;
    public const byte FlagReliable = 0x04;
    public const byte FlagSecuredExtension = 0x08;
    public const byte FlagVendorPresent = 0x10;

    public static ExchangeHeader Decode(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 6)
            throw new ArgumentException("Data too short for exchange header.", nameof(data));
        var header = new ExchangeHeader();
        var span = data.Span;
        int pos = 0;

        header.ExchangeFlags = span[pos++];
        header.Opcode = span[pos++];
        header.ExchangeId = (ushort)(span[pos] | (span[pos + 1] << 8));
        pos += 2;
        header.ProtocolId = (ushort)(span[pos] | (span[pos + 1] << 8));
        pos += 2;

        if ((header.ExchangeFlags & FlagVendorPresent) != 0)
            pos += 2; // Skip vendor ID

        if ((header.ExchangeFlags & FlagAckMsg) != 0)
        {
            header.AckMessageCounter = span.Slice(pos, 4).ToArray();
            pos += 4;
        }

        header.ApplicationPayload = data.Slice(pos);
        return header;
    }

    public byte[] Encode()
    {
        var buffer = new List<byte>(16 + ApplicationPayload.Length);
        buffer.Add(ExchangeFlags);
        buffer.Add(Opcode);
        buffer.Add((byte)(ExchangeId & 0xFF));
        buffer.Add((byte)((ExchangeId >> 8) & 0xFF));
        buffer.Add((byte)(ProtocolId & 0xFF));
        buffer.Add((byte)((ProtocolId >> 8) & 0xFF));

        if (AckMessageCounter != null)
            buffer.AddRange(AckMessageCounter);

        if (!ApplicationPayload.IsEmpty)
            buffer.AddRange(ApplicationPayload.Span.ToArray());

        return buffer.ToArray();
    }
}
