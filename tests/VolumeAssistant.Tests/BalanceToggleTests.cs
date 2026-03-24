using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using VolumeAssistant.Service;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;
using Xunit;

namespace VolumeAssistant.Tests
{
    public class BalanceToggleTests
    {
        private sealed class TestAudioController : IAudioController
        {
            public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
            public float LastSetVolume { get; private set; } = 50f;
            public bool LastSetMuted { get; private set; }
            public float LastSetBalance { get; private set; }

            public float GetVolumePercent() => LastSetVolume;
            public bool GetMuted() => LastSetMuted;
            public void SetVolumePercent(float v) { LastSetVolume = v; }
            public void SetMuted(bool m) { LastSetMuted = m; }
            public void SetBalance(float balanceOffset) { LastSetBalance = balanceOffset; }
            public float GetBalance() => LastSetBalance;
            public void Dispose() { }
        }

        private static VolumeSyncCoordinator CreateCoordinator(
            TestAudioController audio, float balanceOffset = -20f)
        {
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);
            return new VolumeSyncCoordinator(
                audio,
                matterDevice,
                matterServer,
                mdns,
                NullLogger<VolumeSyncCoordinator>.Instance,
                cambridgeAudio: null,
                new CambridgeAudioOptions(),
                new MatterOptions(),
                balanceOffset);
        }

        [Fact]
        public void BalanceToggle_FirstPress_AppliesConfiguredOffset()
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, -20f);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);

            Assert.Equal(-20f, audio.LastSetBalance);
        }

        [Fact]
        public void BalanceToggle_SecondPress_ResetsToCenter()
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, -20f);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);

            Assert.Equal(0f, audio.LastSetBalance);
        }

        [Fact]
        public void BalanceToggle_ThirdPress_ReappliesOffset()
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, -20f);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);

            Assert.Equal(-20f, audio.LastSetBalance);
        }

        [Fact]
        public void BalanceToggle_PositiveOffset_AppliesCorrectly()
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, 30f);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);

            Assert.Equal(30f, audio.LastSetBalance);
        }

        [Theory]
        [InlineData(-20f, -20f)]   // within range - stored as-is
        [InlineData(-150f, -100f)] // clamped to min
        [InlineData(150f, 100f)]   // clamped to max
        public void BalanceOffset_ClampedToValidRange(float input, float expected)
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, input);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);

            Assert.Equal(expected, audio.LastSetBalance);
        }
    }
}
