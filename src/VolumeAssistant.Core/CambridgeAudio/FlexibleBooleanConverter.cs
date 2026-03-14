using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VolumeAssistant.Service.CambridgeAudio
{
    public sealed class FlexibleBooleanConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out var n))
                        return n != 0;
                    if (reader.TryGetDouble(out var d))
                        return Math.Abs(d) > double.Epsilon;
                    throw new JsonException($"Cannot convert number token to boolean: {reader.GetDouble()}");
                case JsonTokenType.String:
                    var s = reader.GetString() ?? string.Empty;
                    if (bool.TryParse(s, out var b))
                        return b;
                    s = s.Trim();
                    var lower = s.ToLowerInvariant();
                    if (lower == "on" || lower == "1")
                        return true;
                    if (lower == "off" || lower == "0")
                        return false;
                    throw new JsonException($"Cannot convert string '{s}' to boolean.");
                case JsonTokenType.Null:
                    throw new JsonException("Cannot convert null to boolean.");
                default:
                    throw new JsonException($"Unexpected token parsing boolean: {reader.TokenType}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            writer.WriteBooleanValue(value);
        }
    }
}
