using System;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VolumeAssistant.Service;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;
using Xunit;

namespace VolumeAssistant.Tests
{
    public class MaxVolumeOptionsTests
    {
        private sealed class TestAudioController : IAudioController
        {
            public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
            public float LastSetVolume { get; private set; } = 50f;
            public bool LastSetMuted { get; private set; }

            public float GetVolumePercent() => LastSetVolume;
            public bool GetMuted() => LastSetMuted;
            public void SetVolumePercent(float volumePercent)
            {
                LastSetVolume = volumePercent;
                VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(volumePercent, LastSetMuted));
            }
            public void SetMuted(bool muted)
            {
                LastSetMuted = muted;
                VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(LastSetVolume, muted));
            }
            public void SetBalance(float balanceOffset) { }
            public float GetBalance() => 0f;
            public void Dispose() { }
        }

        private static Worker CreateWorkerWithMaxVolume(int? maxVolume)
        {
            var audio = new TestAudioController();
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);

            var camOptions = Options.Create(new CambridgeAudioOptions { MaxVolume = maxVolume });

            return new Worker(audio, matterDevice, matterServer, mdns, NullLogger<Worker>.Instance, cambridgeAudio: null, cambridgeOptions: camOptions);
        }

        [Theory]
        [InlineData(100f, 80, 80f)]
        [InlineData(50f,  80, 40f)]
        [InlineData(25f,  60, 15f)]
        public void WindowsToCambridge_AppliesMaxVolumeScaling(float windowsPercent, int maxVolume, float expected)
        {
            var worker = CreateWorkerWithMaxVolume(maxVolume);
            var mi = typeof(Worker).GetMethod("WindowsToCambridgeVolume", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = (float)mi.Invoke(worker, new object[] { windowsPercent })!;
            Assert.Equal(expected, result, 3);
        }

        [Fact]
        public void CambridgeToWindows_WithNullMaxVolume_PassesThrough()
        {
            var worker = CreateWorkerWithMaxVolume(null);
            var mi = typeof(Worker).GetMethod("CambridgeToWindowsVolume", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = (float)mi.Invoke(worker, new object[] { 42f })!;
            Assert.Equal(42f, result, 3);
        }

        [Theory]
        [InlineData(100f, 0, 0f)]
        [InlineData(50f, 0, 0f)]
        [InlineData(30f, -10, 0f)]
        public void CambridgeToWindows_WithNonPositiveMaxVolume_ReturnsZero(float camPercent, int maxVolume, float expected)
        {
            var worker = CreateWorkerWithMaxVolume(maxVolume);
            var mi = typeof(Worker).GetMethod("CambridgeToWindowsVolume", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var result = (float)mi.Invoke(worker, new object[] { camPercent })!;
            Assert.Equal(expected, result, 3);
        }
    }
}
