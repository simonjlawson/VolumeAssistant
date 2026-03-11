using VolumeAssistant.Service.Matter.Protocol;

namespace VolumeAssistant.Tests;

/// <summary>
/// Tests for TLV encoding and decoding.
/// </summary>
public class TlvEncodingTests
{
    [Fact]
    public void TlvWriter_WriteUInt8_ProducesCorrectBytes()
    {
        var writer = new TlvWriter();
        writer.WriteUInt8(tag: 0x02, value: 42);
        byte[] result = writer.ToArray();

        // Control byte: ContextSpecific (0x20) | UnsignedInt1 (0x04) = 0x24
        Assert.Equal(3, result.Length);
        Assert.Equal(0x24, result[0]);
        Assert.Equal(0x02, result[1]); // tag
        Assert.Equal(42, result[2]);   // value
    }

    [Fact]
    public void TlvWriter_WriteUInt16_ProducesCorrectLittleEndianBytes()
    {
        var writer = new TlvWriter();
        writer.WriteUInt16(tag: 0x01, value: 0x0F0A);
        byte[] result = writer.ToArray();

        Assert.Equal(4, result.Length);
        Assert.Equal(0x0A, result[2]); // low byte
        Assert.Equal(0x0F, result[3]); // high byte
    }

    [Fact]
    public void TlvWriter_WriteUInt32_ProducesCorrectLittleEndianBytes()
    {
        var writer = new TlvWriter();
        writer.WriteUInt32(tag: 0x05, value: 0x01020304);
        byte[] result = writer.ToArray();

        Assert.Equal(6, result.Length);
        Assert.Equal(0x04, result[2]);
        Assert.Equal(0x03, result[3]);
        Assert.Equal(0x02, result[4]);
        Assert.Equal(0x01, result[5]);
    }

    [Fact]
    public void TlvWriter_WriteBoolean_True_ProducesBooleanTrueType()
    {
        var writer = new TlvWriter();
        writer.WriteBoolean(tag: 0x00, value: true);
        byte[] result = writer.ToArray();

        Assert.Equal(2, result.Length);
        // Control byte: ContextSpecific (0x20) | BooleanTrue (0x09) = 0x29
        Assert.Equal(0x29, result[0]);
    }

    [Fact]
    public void TlvWriter_WriteBoolean_False_ProducesBooleanFalseType()
    {
        var writer = new TlvWriter();
        writer.WriteBoolean(tag: 0x00, value: false);
        byte[] result = writer.ToArray();

        Assert.Equal(2, result.Length);
        // Control byte: ContextSpecific (0x20) | Boolean (0x08) = 0x28
        Assert.Equal(0x28, result[0]);
    }

    [Fact]
    public void TlvWriter_WriteString_EncodesUtf8WithLengthPrefix()
    {
        var writer = new TlvWriter();
        writer.WriteString(tag: 0x03, value: "Hello");
        byte[] result = writer.ToArray();

        // Control + tag + length + 5 UTF8 bytes = 8 bytes
        Assert.Equal(8, result.Length);
        Assert.Equal(5, result[2]);    // length
        Assert.Equal((byte)'H', result[3]);
        Assert.Equal((byte)'e', result[4]);
    }

    [Fact]
    public void TlvWriter_StartAnonymousStructure_Then_EndContainer_Works()
    {
        var writer = new TlvWriter();
        writer.StartAnonymousStructure();
        writer.WriteUInt8(0x01, 99);
        writer.EndContainer();
        byte[] result = writer.ToArray();

        // Structure control + UInt8 (3 bytes) + EndContainer = 5 bytes
        Assert.Equal(5, result.Length);
        // First byte: Anonymous (0x00) | Structure (0x15) = 0x15
        Assert.Equal(0x15, result[0]);
        // Last byte: Anonymous (0x00) | EndOfContainer (0x18) = 0x18
        Assert.Equal(0x18, result[result.Length - 1]);
    }

    [Fact]
    public void TlvReader_ReadUInt8_DecodesCorrectly()
    {
        var writer = new TlvWriter();
        writer.WriteUInt8(tag: 0x07, value: 123);
        byte[] encoded = writer.ToArray();

        var reader = new TlvReader(encoded);
        TlvElement element = reader.ReadElement();

        Assert.Equal(TlvType.UnsignedInt1, element.Type);
        Assert.Equal((ulong)0x07, element.Tag);
        Assert.Equal((byte)123, element.AsByte());
    }

    [Fact]
    public void MatterMessageHeader_Encode_Decode_RoundTrip()
    {
        var original = new MatterMessageHeader
        {
            Flags = MessageFlags.None,
            SessionId = 0x1234,
            SecurityFlags = SecurityFlags.None,
            MessageCounter = 0xABCD1234,
            Payload = new byte[] { 0x01, 0x02, 0x03 }
        };

        byte[] encoded = original.Encode();
        MatterMessageHeader decoded = MatterMessageHeader.Decode(encoded);

        Assert.Equal(original.SessionId, decoded.SessionId);
        Assert.Equal(original.MessageCounter, decoded.MessageCounter);
        Assert.Equal(original.Payload, decoded.Payload);
    }

    [Fact]
    public void MatterMessageHeader_WithSourceNodeId_Encode_Decode_RoundTrip()
    {
        var original = new MatterMessageHeader
        {
            Flags = MessageFlags.SourceNodeIdPresent,
            SessionId = 0,
            SecurityFlags = SecurityFlags.None,
            MessageCounter = 1,
            SourceNodeId = 0xDEADBEEFCAFEBABEUL,
            Payload = Array.Empty<byte>()
        };

        byte[] encoded = original.Encode();
        MatterMessageHeader decoded = MatterMessageHeader.Decode(encoded);

        Assert.Equal(original.SourceNodeId, decoded.SourceNodeId);
        Assert.Null(decoded.DestinationNodeId);
    }

    [Fact]
    public void ExchangeHeader_Encode_Decode_RoundTrip()
    {
        var original = new ExchangeHeader
        {
            ExchangeFlags = ExchangeHeader.FlagInitiator | ExchangeHeader.FlagReliable,
            Opcode = (byte)InteractionModelOpCode.ReadRequest,
            ExchangeId = 42,
            ProtocolId = (ushort)ExchangeProtocol.InteractionModel,
            ApplicationPayload = new byte[] { 0xAA, 0xBB }
        };

        byte[] encoded = original.Encode();
        ExchangeHeader decoded = ExchangeHeader.Decode(encoded);

        Assert.Equal(original.Opcode, decoded.Opcode);
        Assert.Equal(original.ExchangeId, decoded.ExchangeId);
        Assert.Equal(original.ProtocolId, decoded.ProtocolId);
        Assert.Equal(original.ApplicationPayload, decoded.ApplicationPayload);
    }
}
