namespace VolumeAssistant.Service.Matter.Protocol;

/// <summary>
/// Matter TLV (Tag-Length-Value) element types as defined in the Matter specification.
/// </summary>
public enum TlvType : byte
{
    SignedInt1 = 0x00,
    SignedInt2 = 0x01,
    SignedInt4 = 0x02,
    SignedInt8 = 0x03,
    UnsignedInt1 = 0x04,
    UnsignedInt2 = 0x05,
    UnsignedInt4 = 0x06,
    UnsignedInt8 = 0x07,
    Boolean = 0x08,
    BooleanTrue = 0x09,
    Float = 0x0A,
    Double = 0x0B,
    Utf8String1 = 0x0C,
    Utf8String2 = 0x0D,
    Utf8String4 = 0x0E,
    Utf8String8 = 0x0F,
    ByteString1 = 0x10,
    ByteString2 = 0x11,
    ByteString4 = 0x12,
    ByteString8 = 0x13,
    Null = 0x14,
    Structure = 0x15,
    Array = 0x16,
    List = 0x17,
    EndOfContainer = 0x18,
}

/// <summary>
/// Matter TLV tag control byte encoding.
/// </summary>
public static class TlvTagControl
{
    public const byte Anonymous = 0x00;
    public const byte ContextSpecific = 0x20;
    public const byte CommonProfile2Byte = 0x40;
    public const byte CommonProfile4Byte = 0x60;
    public const byte ImplicitProfile2Byte = 0x80;
    public const byte ImplicitProfile4Byte = 0xA0;
    public const byte FullyQualified6Byte = 0xC0;
    public const byte FullyQualified8Byte = 0xE0;
}

/// <summary>
/// Writes Matter TLV encoded data into a buffer.
/// </summary>
public sealed class TlvWriter
{
    private readonly List<byte> _buffer = new();

    public byte[] ToArray() => _buffer.ToArray();

    public void WriteUInt8(byte tag, byte value)
    {
        _buffer.Add((byte)(TlvTagControl.ContextSpecific | (byte)TlvType.UnsignedInt1));
        _buffer.Add(tag);
        _buffer.Add(value);
    }

    public void WriteUInt16(byte tag, ushort value)
    {
        _buffer.Add((byte)(TlvTagControl.ContextSpecific | (byte)TlvType.UnsignedInt2));
        _buffer.Add(tag);
        _buffer.Add((byte)(value & 0xFF));
        _buffer.Add((byte)((value >> 8) & 0xFF));
    }

    public void WriteUInt32(byte tag, uint value)
    {
        _buffer.Add((byte)(TlvTagControl.ContextSpecific | (byte)TlvType.UnsignedInt4));
        _buffer.Add(tag);
        _buffer.Add((byte)(value & 0xFF));
        _buffer.Add((byte)((value >> 8) & 0xFF));
        _buffer.Add((byte)((value >> 16) & 0xFF));
        _buffer.Add((byte)((value >> 24) & 0xFF));
    }

    public void WriteBoolean(byte tag, bool value)
    {
        byte typeId = value
            ? (byte)(TlvTagControl.ContextSpecific | (byte)TlvType.BooleanTrue)
            : (byte)(TlvTagControl.ContextSpecific | (byte)TlvType.Boolean);
        _buffer.Add(typeId);
        _buffer.Add(tag);
    }

    public void WriteString(byte tag, string value)
    {
        byte[] encoded = System.Text.Encoding.UTF8.GetBytes(value);
        _buffer.Add((byte)(TlvTagControl.ContextSpecific | (byte)TlvType.Utf8String1));
        _buffer.Add(tag);
        _buffer.Add((byte)encoded.Length);
        _buffer.AddRange(encoded);
    }

    public void WriteBytes(byte tag, byte[] value)
    {
        _buffer.Add((byte)(TlvTagControl.ContextSpecific | (byte)TlvType.ByteString1));
        _buffer.Add(tag);
        _buffer.Add((byte)value.Length);
        _buffer.AddRange(value);
    }

    public void StartStructure(byte tag)
    {
        _buffer.Add((byte)(TlvTagControl.ContextSpecific | (byte)TlvType.Structure));
        _buffer.Add(tag);
    }

    public void StartAnonymousStructure()
    {
        _buffer.Add((byte)(TlvTagControl.Anonymous | (byte)TlvType.Structure));
    }

    public void EndContainer()
    {
        _buffer.Add((byte)(TlvTagControl.Anonymous | (byte)TlvType.EndOfContainer));
    }

    public void WriteAnonymousUInt8(byte value)
    {
        _buffer.Add((byte)(TlvTagControl.Anonymous | (byte)TlvType.UnsignedInt1));
        _buffer.Add(value);
    }

    public void WriteAnonymousUInt16(ushort value)
    {
        _buffer.Add((byte)(TlvTagControl.Anonymous | (byte)TlvType.UnsignedInt2));
        _buffer.Add((byte)(value & 0xFF));
        _buffer.Add((byte)((value >> 8) & 0xFF));
    }

    public void WriteAnonymousUInt32(uint value)
    {
        _buffer.Add((byte)(TlvTagControl.Anonymous | (byte)TlvType.UnsignedInt4));
        _buffer.Add((byte)(value & 0xFF));
        _buffer.Add((byte)((value >> 8) & 0xFF));
        _buffer.Add((byte)((value >> 16) & 0xFF));
        _buffer.Add((byte)((value >> 24) & 0xFF));
    }
}

/// <summary>
/// Reads Matter TLV encoded data from a buffer.
/// </summary>
public sealed class TlvReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _position;

    public TlvReader(ReadOnlyMemory<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public bool HasData => _position < _data.Length;

    public TlvElement ReadElement()
    {
        if (_position >= _data.Length)
            throw new InvalidOperationException("No more TLV elements to read.");

        byte controlByte = _data.Span[_position++];
        byte tagControl = (byte)(controlByte & 0xE0);
        byte elementType = (byte)(controlByte & 0x1F);

        ulong? tag = ReadTag(tagControl);
        object? value = ReadValue((TlvType)elementType);

        return new TlvElement(tagControl, tag, (TlvType)elementType, value);
    }

    private ulong? ReadTag(byte tagControl)
    {
        return tagControl switch
        {
            TlvTagControl.Anonymous => null,
            TlvTagControl.ContextSpecific => _data.Span[_position++],
            TlvTagControl.CommonProfile2Byte => ReadUInt16(),
            TlvTagControl.CommonProfile4Byte => ReadUInt32(),
            TlvTagControl.ImplicitProfile2Byte => ReadUInt16(),
            TlvTagControl.ImplicitProfile4Byte => ReadUInt32(),
            _ => null
        };
    }

    private object? ReadValue(TlvType type)
    {
        return type switch
        {
            TlvType.UnsignedInt1 => _data.Span[_position++],
            TlvType.UnsignedInt2 => ReadUInt16(),
            TlvType.UnsignedInt4 => ReadUInt32(),
            TlvType.SignedInt1 => (sbyte)_data.Span[_position++],
            TlvType.Boolean => false,
            TlvType.BooleanTrue => true,
            TlvType.Utf8String1 => ReadString(),
            TlvType.ByteString1 => ReadBytes(),
            TlvType.Structure => null,
            TlvType.Array => null,
            TlvType.EndOfContainer => null,
            TlvType.Null => null,
            _ => null
        };
    }

    private ushort ReadUInt16()
    {
        ushort value = (ushort)(_data.Span[_position] | (_data.Span[_position + 1] << 8));
        _position += 2;
        return value;
    }

    private uint ReadUInt32()
    {
        uint value = (uint)(_data.Span[_position]
            | (_data.Span[_position + 1] << 8)
            | (_data.Span[_position + 2] << 16)
            | (_data.Span[_position + 3] << 24));
        _position += 4;
        return value;
    }

    private string ReadString()
    {
        int length = _data.Span[_position++];
        string value = System.Text.Encoding.UTF8.GetString(_data.Span.Slice(_position, length));
        _position += length;
        return value;
    }

    private byte[] ReadBytes()
    {
        int length = _data.Span[_position++];
        byte[] value = _data.Span.Slice(_position, length).ToArray();
        _position += length;
        return value;
    }
}

/// <summary>
/// Represents a single decoded TLV element.
/// </summary>
public sealed class TlvElement
{
    public byte TagControl { get; }
    public ulong? Tag { get; }
    public TlvType Type { get; }
    public object? Value { get; }

    public TlvElement(byte tagControl, ulong? tag, TlvType type, object? value)
    {
        TagControl = tagControl;
        Tag = tag;
        Type = type;
        Value = value;
    }

    public byte AsByte() => (byte)(Value ?? throw new InvalidCastException());
    public ushort AsUInt16() => (ushort)(Value ?? throw new InvalidCastException());
    public uint AsUInt32() => (uint)(Value ?? throw new InvalidCastException());
    public bool AsBoolean() => (bool)(Value ?? throw new InvalidCastException());
    public string AsString() => (string)(Value ?? throw new InvalidCastException());
    public byte[] AsBytes() => (byte[])(Value ?? throw new InvalidCastException());
}
