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
    public class HeadphonesBalanceTests
    {
        // ── Test doubles ─────────────────────────────────────────────────────────

        private sealed class TestAudioController : IAudioController
        {
            public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
            public float LastSetVolume { get; private set; } = 50f;
            public bool LastSetMuted { get; private set; }
            public float GetVolumePercent() => LastSetVolume;
            public bool GetMuted() => LastSetMuted;
            public void SetVolumePercent(float v) { LastSetVolume = v; }
            public void SetMuted(bool m) { LastSetMuted = m; }
            public void SetBalance(float b) { }
            public float GetBalance() => 0f;
            public void Dispose() { }
        }

        private sealed class TestCambridgeAudioClient : ICambridgeAudioClient
        {
            private EventHandler<CambridgeAudioStateChangedEventArgs>? _stateChanged;
            public event EventHandler<CambridgeAudioStateChangedEventArgs>? StateChanged
            {
                add => _stateChanged += value;
                remove => _stateChanged -= value;
            }

            public event EventHandler<CambridgeAudioConnectionChangedEventArgs>? ConnectionChanged
            {
                add { }
                remove { }
            }

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

            /// <summary>Raise a StateChanged event with the given audio output value.</summary>
            public void RaiseOutputChanged(string audioOutput)
            {
                _stateChanged?.Invoke(this, new CambridgeAudioStateChangedEventArgs(
                    new CambridgeAudioState { AudioOutput = audioOutput }));
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static VolumeSyncCoordinator CreateCoordinator(
            TestCambridgeAudioClient cam,
            float balanceOffset = -20f,
            string? headphonesOutput = "headphones",
            bool applyBalanceOnStartup = false)
        {
            var audio = new TestAudioController();
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);
            var opts = new CambridgeAudioOptions { HeadphonesOutput = headphonesOutput };
            return new VolumeSyncCoordinator(
                audio,
                matterDevice,
                matterServer,
                mdns,
                NullLogger<VolumeSyncCoordinator>.Instance,
                cambridgeAudio: cam,
                opts,
                new MatterOptions(),
                balanceOffset: balanceOffset,
                adjustWindowsBalance: false,
                adjustCambridgeAudioBalance: true,
                applyBalanceOnStartup: applyBalanceOnStartup);
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task HeadphonesPluggedIn_SetsBalanceToZero()
        {
            var cam = new TestCambridgeAudioClient();
            var coordinator = CreateCoordinator(cam, balanceOffset: -20f);

            coordinator.OnCambridgeAudioOutputChanged(null,
                new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState { AudioOutput = "headphones" }));
            await Task.Delay(50);

            Assert.Single(cam.SetBalanceCalls);
            Assert.Equal(0, cam.SetBalanceCalls[0]);
        }

        [Fact]
        public async Task HeadphonesUnplugged_BalanceNotActive_RestoresZero()
        {
            var cam = new TestCambridgeAudioClient();
            var coordinator = CreateCoordinator(cam, balanceOffset: -20f);

            // Plug in
            coordinator.OnCambridgeAudioOutputChanged(null,
                new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState { AudioOutput = "headphones" }));
            await Task.Delay(50);

            // Unplug
            coordinator.OnCambridgeAudioOutputChanged(null,
                new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState { AudioOutput = "speakers" }));
            await Task.Delay(50);

            Assert.Equal(2, cam.SetBalanceCalls.Count);
            Assert.Equal(0, cam.SetBalanceCalls[0]); // centre on plug-in
            Assert.Equal(0, cam.SetBalanceCalls[1]); // restore 0 (balance not active)
        }

        [Fact]
        public async Task HeadphonesUnplugged_BalanceActive_RestoresConfiguredOffset()
        {
            var cam = new TestCambridgeAudioClient();
            // balanceOffset = -100 → Cambridge balance = -15
            var coordinator = CreateCoordinator(cam, balanceOffset: -100f);

            // Activate the balance toggle first (so _balanceActive = true)
            coordinator.OnMediaKeyBalanceToggleRequestedInternal(null, EventArgs.Empty);
            await Task.Delay(50);
            cam.SetBalanceCalls.Clear(); // reset to isolate headphone calls

            // Plug in headphones
            coordinator.OnCambridgeAudioOutputChanged(null,
                new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState { AudioOutput = "headphones" }));
            await Task.Delay(50);

            // Unplug headphones
            coordinator.OnCambridgeAudioOutputChanged(null,
                new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState { AudioOutput = "speakers" }));
            await Task.Delay(50);

            Assert.Equal(2, cam.SetBalanceCalls.Count);
            Assert.Equal(0, cam.SetBalanceCalls[0]);   // centre on plug-in
            Assert.Equal(-15, cam.SetBalanceCalls[1]); // restore configured offset (-100 * 15/100 = -15)
        }

        [Fact]
        public async Task HeadphonesOutput_EmptyString_DisablesFeature()
        {
            var cam = new TestCambridgeAudioClient();
            var coordinator = CreateCoordinator(cam, headphonesOutput: "");

            coordinator.OnCambridgeAudioOutputChanged(null,
                new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState { AudioOutput = "headphones" }));
            await Task.Delay(50);

            Assert.Empty(cam.SetBalanceCalls);
        }

        [Fact]
        public async Task HeadphonesOutput_NullOption_DisablesFeature()
        {
            var cam = new TestCambridgeAudioClient();
            var coordinator = CreateCoordinator(cam, headphonesOutput: null);

            coordinator.OnCambridgeAudioOutputChanged(null,
                new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState { AudioOutput = "headphones" }));
            await Task.Delay(50);

            Assert.Empty(cam.SetBalanceCalls);
        }

        [Fact]
        public async Task DuplicateHeadphonesEvents_OnlySetBalanceOnce()
        {
            var cam = new TestCambridgeAudioClient();
            var coordinator = CreateCoordinator(cam);

            coordinator.OnCambridgeAudioOutputChanged(null,
                new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState { AudioOutput = "headphones" }));
            await Task.Delay(50);

            // Same output again
            coordinator.OnCambridgeAudioOutputChanged(null,
                new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState { AudioOutput = "headphones" }));
            await Task.Delay(50);

            Assert.Single(cam.SetBalanceCalls);
        }

        [Fact]
        public async Task HeadphonesDetection_CaseInsensitive()
        {
            var cam = new TestCambridgeAudioClient();
            var coordinator = CreateCoordinator(cam, headphonesOutput: "headphones");

            coordinator.OnCambridgeAudioOutputChanged(null,
                new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState { AudioOutput = "HEADPHONES" }));
            await Task.Delay(50);

            Assert.Single(cam.SetBalanceCalls);
            Assert.Equal(0, cam.SetBalanceCalls[0]);
        }

        [Fact]
        public async Task EmptyAudioOutput_InUpdate_IsIgnored()
        {
            var cam = new TestCambridgeAudioClient();
            var coordinator = CreateCoordinator(cam);

            // Update with no audio_output information
            coordinator.OnCambridgeAudioOutputChanged(null,
                new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState { AudioOutput = null }));
            await Task.Delay(50);

            Assert.Empty(cam.SetBalanceCalls);
        }

        [Fact]
        public async Task StartAsync_WithHeadphones_WireUpDetectionViaEvent()
        {
            var cam = new TestCambridgeAudioClient();
            var coordinator = CreateCoordinator(cam);

            // StartAsync subscribes to StateChanged
            await coordinator.StartAsync(CancellationToken.None);

            // Simulate device reporting headphones via the event
            cam.RaiseOutputChanged("headphones");
            await Task.Delay(50);

            Assert.Single(cam.SetBalanceCalls);
            Assert.Equal(0, cam.SetBalanceCalls[0]);
        }

        [Fact]
        public async Task StartAsync_ThenStopAsync_UnsubscribesDetection()
        {
            var cam = new TestCambridgeAudioClient();
            var coordinator = CreateCoordinator(cam);

            await coordinator.StartAsync(CancellationToken.None);
            await coordinator.StopAsync();

            // After stop, events should no longer trigger balance changes
            cam.RaiseOutputChanged("headphones");
            await Task.Delay(50);

            Assert.Empty(cam.SetBalanceCalls);
        }
    }
}
