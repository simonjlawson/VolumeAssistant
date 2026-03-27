using System;
using System.Collections.Generic;
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
            public int SetBalanceCallCount { get; private set; }

            public float GetVolumePercent() => LastSetVolume;
            public bool GetMuted() => LastSetMuted;
            public void SetVolumePercent(float v) { LastSetVolume = v; }
            public void SetMuted(bool m) { LastSetMuted = m; }
            public void SetBalance(float balanceOffset) { LastSetBalance = balanceOffset; SetBalanceCallCount++; }
            public float GetBalance() => LastSetBalance;
            public void Dispose() { }
        }

        private sealed class TestCambridgeAudioClient : ICambridgeAudioClient
        {
            public event EventHandler<CambridgeAudioStateChangedEventArgs>? StateChanged { add { } remove { } }
            public event EventHandler<CambridgeAudioConnectionChangedEventArgs>? ConnectionChanged { add { } remove { } }
            public bool IsConnected { get; set; } = true;
            public CambridgeAudioInfo? Info => null;
            public IReadOnlyList<CambridgeAudioSource> Sources => Array.Empty<CambridgeAudioSource>();
            public CambridgeAudioState? State => null;
            public List<int> SetBalanceCalls { get; } = new();

            public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task DisconnectAsync() => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public Task<CambridgeAudioInfo> GetInfoAsync(CancellationToken ct = default) => Task.FromResult(new CambridgeAudioInfo());
            public Task<IReadOnlyList<CambridgeAudioSource>> GetSourcesAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<CambridgeAudioSource>>(Array.Empty<CambridgeAudioSource>());
            public Task<CambridgeAudioState> GetStateAsync(CancellationToken ct = default) => Task.FromResult(new CambridgeAudioState());
            public Task SetVolumeAsync(int v, CancellationToken ct = default) => Task.CompletedTask;
            public Task SetMuteAsync(bool m, CancellationToken ct = default) => Task.CompletedTask;
            public Task SetSourceAsync(string s, CancellationToken ct = default) => Task.CompletedTask;
            public Task SetAudioOutputAsync(string o, CancellationToken ct = default) => Task.CompletedTask;
            public Task PowerOnAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task PowerOffAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task PlayPauseAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task NextTrackAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task PreviousTrackAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task SetBalanceAsync(int balance, CancellationToken ct = default)
            {
                SetBalanceCalls.Add(balance);
                return Task.CompletedTask;
            }
        }

        private static VolumeSyncCoordinator CreateCoordinator(
            TestAudioController audio,
            float balanceOffset = -20f,
            bool adjustWindowsBalance = false,
            bool adjustCambridgeAudioBalance = true,
            ICambridgeAudioClient? cambridgeAudio = null,
            bool applyBalanceOnStartup = false)
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
                cambridgeAudio: cambridgeAudio,
                new CambridgeAudioOptions(),
                new MatterOptions(),
                balanceOffset,
                adjustWindowsBalance,
                adjustCambridgeAudioBalance,
                applyBalanceOnStartup);
        }

        // ── Windows balance ───────────────────────────────────────────────────────

        [Fact]
        public void WindowsBalance_DisabledByDefault_DoesNotSetBalance()
        {
            var audio = new TestAudioController();
            // adjustWindowsBalance defaults to false
            var coordinator = CreateCoordinator(audio, -20f);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);

            Assert.Equal(0, audio.SetBalanceCallCount); // SetBalance was never called
        }

        [Fact]
        public void WindowsBalance_Enabled_FirstPress_AppliesConfiguredOffset()
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, -20f, adjustWindowsBalance: true, adjustCambridgeAudioBalance: false);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);

            Assert.Equal(-20f, audio.LastSetBalance);
        }

        [Fact]
        public void WindowsBalance_Enabled_SecondPress_ResetsToCenter()
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, -20f, adjustWindowsBalance: true, adjustCambridgeAudioBalance: false);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);

            Assert.Equal(0f, audio.LastSetBalance);
        }

        [Fact]
        public void WindowsBalance_Enabled_ThirdPress_ReappliesOffset()
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, -20f, adjustWindowsBalance: true, adjustCambridgeAudioBalance: false);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);

            Assert.Equal(-20f, audio.LastSetBalance);
        }

        [Theory]
        [InlineData(-20f, -20f)]   // within range - stored as-is
        [InlineData(-150f, -100f)] // clamped to min
        [InlineData(150f, 100f)]   // clamped to max
        public void WindowsBalance_Enabled_OffsetClamped(float input, float expected)
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, input, adjustWindowsBalance: true, adjustCambridgeAudioBalance: false);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);

            Assert.Equal(expected, audio.LastSetBalance);
        }

        // ── Cambridge Audio balance ───────────────────────────────────────────────

        [Fact]
        public async Task CambridgeAudioBalance_EnabledByDefault_FirstPress_SendsMappedBalance()
        {
            var audio = new TestAudioController();
            var cam = new TestCambridgeAudioClient { IsConnected = true };
            // adjustCambridgeAudioBalance defaults to true
            var coordinator = CreateCoordinator(audio, -100f, adjustWindowsBalance: false, cambridgeAudio: cam);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            await Task.Delay(50); // let the fire-and-forget task complete

            Assert.Single(cam.SetBalanceCalls);
            Assert.Equal(-15, cam.SetBalanceCalls[0]); // -100 * 15/100 = -15
        }

        [Fact]
        public async Task CambridgeAudioBalance_SecondPress_SendsZero()
        {
            var audio = new TestAudioController();
            var cam = new TestCambridgeAudioClient { IsConnected = true };
            var coordinator = CreateCoordinator(audio, -100f, adjustWindowsBalance: false, cambridgeAudio: cam);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            await Task.Delay(50);
            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            await Task.Delay(50);

            Assert.Equal(2, cam.SetBalanceCalls.Count);
            Assert.Equal(-15, cam.SetBalanceCalls[0]);
            Assert.Equal(0, cam.SetBalanceCalls[1]);
        }

        [Theory]
        [InlineData(-100f, -15)]  // full left
        [InlineData(-20f, -3)]    // default offset: -20 * 15/100 = -3
        [InlineData(0f, 0)]       // centre
        [InlineData(100f, 15)]    // full right
        [InlineData(50f, 8)]      // 50 * 15/100 = 7.5 -> rounds to 8
        public async Task CambridgeAudioBalance_MapsAppOffsetToDeviceRange(float appOffset, int expectedCambridgeBalance)
        {
            var audio = new TestAudioController();
            var cam = new TestCambridgeAudioClient { IsConnected = true };
            var coordinator = CreateCoordinator(audio, appOffset, adjustWindowsBalance: false, cambridgeAudio: cam);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            await Task.Delay(50);

            Assert.Single(cam.SetBalanceCalls);
            Assert.Equal(expectedCambridgeBalance, cam.SetBalanceCalls[0]);
        }

        [Fact]
        public async Task CambridgeAudioBalance_Disabled_DoesNotSendBalance()
        {
            var audio = new TestAudioController();
            var cam = new TestCambridgeAudioClient { IsConnected = true };
            var coordinator = CreateCoordinator(audio, -20f, adjustWindowsBalance: false,
                adjustCambridgeAudioBalance: false, cambridgeAudio: cam);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            await Task.Delay(50);

            Assert.Empty(cam.SetBalanceCalls);
        }

        [Fact]
        public async Task CambridgeAudioBalance_NotConnected_DoesNotSendBalance()
        {
            var audio = new TestAudioController();
            var cam = new TestCambridgeAudioClient { IsConnected = false };
            var coordinator = CreateCoordinator(audio, -20f, adjustWindowsBalance: false, cambridgeAudio: cam);

            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            await Task.Delay(50);

            Assert.Empty(cam.SetBalanceCalls);
        }

        // ── Apply balance on startup ──────────────────────────────────────────────

        [Fact]
        public async Task ApplyBalanceOnStartup_Windows_AppliesOffsetImmediately()
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, -20f, adjustWindowsBalance: true,
                adjustCambridgeAudioBalance: false, applyBalanceOnStartup: true);

            await coordinator.StartAsync(CancellationToken.None);

            Assert.Equal(-20f, audio.LastSetBalance);
            Assert.Equal(1, audio.SetBalanceCallCount);
        }

        [Fact]
        public async Task ApplyBalanceOnStartup_Disabled_DoesNotSetWindowsBalance()
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, -20f, adjustWindowsBalance: true,
                adjustCambridgeAudioBalance: false, applyBalanceOnStartup: false);

            await coordinator.StartAsync(CancellationToken.None);

            Assert.Equal(0, audio.SetBalanceCallCount);
        }

        [Fact]
        public async Task ApplyBalanceOnStartup_Windows_ToggleThenPressResetsToCenter()
        {
            var audio = new TestAudioController();
            var coordinator = CreateCoordinator(audio, -20f, adjustWindowsBalance: true,
                adjustCambridgeAudioBalance: false, applyBalanceOnStartup: true);

            await coordinator.StartAsync(CancellationToken.None);

            // Startup sets _balanceActive=true; the toggle handler flips the current state,
            // so the first key press deactivates balance and resets to centre (0).
            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);

            Assert.Equal(0f, audio.LastSetBalance);
        }

        [Fact]
        public async Task ApplyBalanceOnStartup_CambridgeAudio_SendsMappedBalance()
        {
            var audio = new TestAudioController();
            var cam = new TestCambridgeAudioClient { IsConnected = true };
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);
            var coordinator = new VolumeSyncCoordinator(
                audio,
                matterDevice,
                matterServer,
                mdns,
                NullLogger<VolumeSyncCoordinator>.Instance,
                cambridgeAudio: cam,
                new CambridgeAudioOptions(),
                new MatterOptions(),
                balanceOffset: -100f,
                adjustWindowsBalance: false,
                adjustCambridgeAudioBalance: true,
                applyBalanceOnStartup: true);

            await coordinator.StartAsync(CancellationToken.None);
            await Task.Delay(100); // let the background Task.Run complete

            Assert.Contains(-15, cam.SetBalanceCalls); // -100 * 15/100 = -15
        }
    }
}
