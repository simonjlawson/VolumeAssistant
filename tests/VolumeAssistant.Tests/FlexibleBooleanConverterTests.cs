using System;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VolumeAssistant.Service.CambridgeAudio;
using Xunit;

namespace VolumeAssistant.Tests
{
    public class FlexibleBooleanConverterTests
    {
        private static JsonSerializerOptions Options()
        {
            var o = new JsonSerializerOptions();
            o.Converters.Add(new FlexibleBooleanConverter());
            return o;
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("\"true\"", true)]
        [InlineData("\"false\"", false)]
        [InlineData("\"on\"", true)]
        [InlineData("\"off\"", false)]
        [InlineData("1", true)]
        [InlineData("0", false)]
        [InlineData("1.0", true)]
        public void Read_ValidValues_ReturnsExpected(string json, bool expected)
        {
            var o = Options();
            bool result = JsonSerializer.Deserialize<bool>(json, o);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Read_InvalidString_Throws()
        {
            var o = Options();
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<bool>("\"maybe\"", o));
        }

        [Fact]
        public void Read_Null_Throws()
        {
            var o = Options();
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<bool>("null", o));
        }
    }
}
