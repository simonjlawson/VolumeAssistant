using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VolumeAssistant.Service;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;
using Xunit;

namespace VolumeAssistant.Tests
{
    public class SourceSwitchingTests
    {
        // ── Option defaults ──────────────────────────────────────────────────────

        [Fact]
        public void SourceSwitchingEnabled_DefaultsToFalse()
        {
            var options = new CambridgeAudioOptions();
            Assert.False(options.SourceSwitchingEnabled);
        }


        [Fact]
        public void SourceSwitchingNames_DefaultsToNull()
        {
            var options = new CambridgeAudioOptions();
            Assert.Null(options.SourceSwitchingNames);
        }

        [Fact]
        public void SourceSwitchingEnabled_CanBeSetToTrue()
        {
            var options = new CambridgeAudioOptions { SourceSwitchingEnabled = true };
            Assert.True(options.SourceSwitchingEnabled);
        }


        [Fact]
        public void SourceSwitchingNames_CanBeConfigured()
        {
            var options = new CambridgeAudioOptions { SourceSwitchingNames = "PC,TV,Spotify" };
            Assert.Equal("PC,TV,Spotify", options.SourceSwitchingNames);
        }

        // ── Recording client ─────────────────────────────────────────────────────

        private sealed class RecordingCambridgeAudioClient : ICambridgeAudioClient
        {
            public event EventHandler<CambridgeAudioStateChangedEventArgs>? StateChanged { add { } remove { } }
            public event EventHandler<CambridgeAudioConnectionChangedEventArgs>? ConnectionChanged { add { } remove { } }

            public bool IsConnected { get; set; } = true;
            public CambridgeAudioInfo? Info => null;
            public IReadOnlyList<CambridgeAudioSource> Sources { get; set; } = Array.Empty<CambridgeAudioSource>();
            public CambridgeAudioState? State { get; set; }

            public List<string> SetSourceIds { get; } = new List<string>();
            public int PlayPauseCalls { get; private set; }
            public int NextTrackCalls { get; private set; }
            public int PreviousTrackCalls { get; private set; }

            public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task DisconnectAsync() => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public Task<CambridgeAudioInfo> GetInfoAsync(CancellationToken ct = default) => Task.FromResult(new CambridgeAudioInfo());
            public Task<IReadOnlyList<CambridgeAudioSource>> GetSourcesAsync(CancellationToken ct = default)
                => Task.FromResult(Sources);
            public Task<CambridgeAudioState> GetStateAsync(CancellationToken ct = default) => Task.FromResult(new CambridgeAudioState());

            public Task SetVolumeAsync(int v, CancellationToken ct = default) => Task.CompletedTask;
            public Task SetMuteAsync(bool m, CancellationToken ct = default) => Task.CompletedTask;
            public Task SetSourceAsync(string s, CancellationToken ct = default) { SetSourceIds.Add(s); return Task.CompletedTask; }
            public Task SetAudioOutputAsync(string o, CancellationToken ct = default) => Task.CompletedTask;
            public Task PowerOnAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task PowerOffAsync(CancellationToken ct = default) => Task.CompletedTask;

            public Task PlayPauseAsync(CancellationToken ct = default) { PlayPauseCalls++; return Task.CompletedTask; }
            public Task NextTrackAsync(CancellationToken ct = default) { NextTrackCalls++; return Task.CompletedTask; }
            public Task PreviousTrackAsync(CancellationToken ct = default) { PreviousTrackCalls++; return Task.CompletedTask; }
            public Task SetBalanceAsync(int balance, CancellationToken ct = default) => Task.CompletedTask;
        }

        private sealed class NopAudioController : IAudioController
        {
            public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
            public float GetVolumePercent() => 50f;
            public bool GetMuted() => false;
            public void SetVolumePercent(float v) { }
            public void SetMuted(bool m) { }
            public void SetBalance(float balanceOffset) { }
            public float GetBalance() => 0f;
            public void Dispose() { }
        }

        private static (Worker worker, RecordingCambridgeAudioClient cam) CreateWorkerWithOptions(
            CambridgeAudioOptions cambridgeOptions)
        {
            var audio = new NopAudioController();
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);
            var cam = new RecordingCambridgeAudioClient();

            var worker = new Worker(
                audio, matterDevice, matterServer, mdns,
                NullLogger<Worker>.Instance, cam,
                Options.Create(cambridgeOptions));

            return (worker, cam);
        }

        private static IReadOnlyList<CambridgeAudioSource> MakeSources(params (string id, string name)[] entries)
        {
            var list = new List<CambridgeAudioSource>();
            foreach (var (id, name) in entries)
                list.Add(new CambridgeAudioSource { Id = id, Name = name });
            return list;
        }

        // ── CycleSourceAsync via NextTrack key ───────────────────────────────────

        [Fact]
        public void OnMediaKeyNextTrack_WhenSourceSwitchingEnabled_CallsSetSourceNotNextTrack()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = "PC,TV"
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"));
            cam.State = new CambridgeAudioState { Source = "pc_usb" };

            // Trigger source switching via the dedicated event (Shift+ScrollLock)
            var handler = typeof(Worker).GetMethod(
                "OnMediaKeySourceSwitchRequested",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            // Should have called SetSource, not NextTrack
            Assert.Equal(0, cam.NextTrackCalls);
            Assert.Single(cam.SetSourceIds);
            Assert.Equal("tv_arc", cam.SetSourceIds[0]);
        }

        [Fact]
        public void OnMediaKeyNextTrack_WhenSourceSwitchingEnabled_WrapsAroundToFirst()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = "PC,TV"
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"));
            // Current source is the last in the list
            cam.State = new CambridgeAudioState { Source = "tv_arc" };

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeySourceSwitchRequested",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            Assert.Single(cam.SetSourceIds);
            Assert.Equal("pc_usb", cam.SetSourceIds[0]);
        }

        [Fact]
        public void OnMediaKeyNextTrack_WhenSourceSwitchingEnabled_CurrentSourceNotInList_SwitchesToFirst()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = "PC,TV"
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"), ("spotify", "Spotify"));
            // Current source is Spotify, which is NOT in the configured list
            cam.State = new CambridgeAudioState { Source = "spotify" };

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeySourceSwitchRequested",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            Assert.Single(cam.SetSourceIds);
            Assert.Equal("pc_usb", cam.SetSourceIds[0]);
        }

        [Fact]
        public void OnMediaKeyNextTrack_WhenSourceSwitchingEnabledButNamesEmpty_DoesNothing()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = null
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"));
            cam.State = new CambridgeAudioState { Source = "pc_usb" };

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeySourceSwitchRequested",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            Assert.Empty(cam.SetSourceIds);
            Assert.Equal(0, cam.NextTrackCalls);
        }

        // ── CycleSourceAsync via PlayPause key ───────────────────────────────────

        [Fact]
        public void OnMediaKeyPlayPause_WhenSourceSwitchingEnabled_CyclesSources()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = "PC,TV"
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"));
            cam.State = new CambridgeAudioState { Source = "pc_usb" };

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeySourceSwitchRequested",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            Assert.Equal(0, cam.PlayPauseCalls);
            Assert.Single(cam.SetSourceIds);
            Assert.Equal("tv_arc", cam.SetSourceIds[0]);
        }

        // ── CycleSourceAsync via PreviousTrack key ───────────────────────────────

        [Fact]
        public void OnMediaKeyPreviousTrack_WhenSourceSwitchingEnabled_CyclesSources()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = "PC,TV"
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"));
            cam.State = new CambridgeAudioState { Source = "pc_usb" };

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeySourceSwitchRequested",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            Assert.Equal(0, cam.PreviousTrackCalls);
            Assert.Single(cam.SetSourceIds);
            Assert.Equal("tv_arc", cam.SetSourceIds[0]);
        }

        // ── Transport commands still work when not the switching key ─────────────

        [Fact]
        public void OnMediaKeyNextTrack_WhenSourceSwitchingEnabled_DifferentKey_StillSendsNextTrack()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = "PC,TV"
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"));
            cam.State = new CambridgeAudioState { Source = "pc_usb" };

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeyNextTrack",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            // NextTrack should have been sent as transport command
            Assert.Equal(1, cam.NextTrackCalls);
            Assert.Empty(cam.SetSourceIds);
        }

        [Fact]
        public void OnMediaKeyPlayPause_WhenSourceSwitchingDisabled_StillSendsPlayPause()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = false,
                SourceSwitchingNames = "PC,TV"
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"));
            cam.State = new CambridgeAudioState { Source = "pc_usb" };

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeyPlayPause",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            Assert.Equal(1, cam.PlayPauseCalls);
            Assert.Empty(cam.SetSourceIds);
        }

        // ── Case-insensitive source name matching ────────────────────────────────

        [Fact]
        public void CycleSourceAsync_MatchesSourceNameCaseInsensitively()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = "pc,tv"          // lower-case in config
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"));  // upper-case on device
            cam.State = new CambridgeAudioState { Source = "pc_usb" };

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeySourceSwitchRequested",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            Assert.Single(cam.SetSourceIds);
            Assert.Equal("tv_arc", cam.SetSourceIds[0]);
        }

        // ── Source switching key matching is case-insensitive ────────────────────

        [Fact]
        public void SourceSwitchingKey_MatchesCaseInsensitively()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = "PC,TV"
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"));
            cam.State = new CambridgeAudioState { Source = "pc_usb" };

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeySourceSwitchRequested",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            Assert.Single(cam.SetSourceIds);
            Assert.Equal("tv_arc", cam.SetSourceIds[0]);
        }

        // ── SourceSwitchingNames with whitespace is trimmed ──────────────────────

        [Fact]
        public void SourceSwitchingNames_TrimsWhitespaceAroundNames()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = " PC , TV "   // names with surrounding spaces
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"));
            cam.State = new CambridgeAudioState { Source = "pc_usb" };

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeySourceSwitchRequested",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            Assert.Single(cam.SetSourceIds);
            Assert.Equal("tv_arc", cam.SetSourceIds[0]);
        }

        // ── Not connected does nothing ───────────────────────────────────────────

        [Fact]
        public void OnMediaKeyNextTrack_WhenSourceSwitchingEnabled_NotConnected_DoesNothing()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = "PC,TV"
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.IsConnected = false;
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"));
            cam.State = new CambridgeAudioState { Source = "pc_usb" };
            var handler = typeof(Worker).GetMethod(
                "OnMediaKeySourceSwitchRequested",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
            Thread.Sleep(200);

            Assert.Empty(cam.SetSourceIds);
            Assert.Equal(0, cam.NextTrackCalls);
        }

        // ── Target source not on device logs warning, does not throw ─────────────

        [Fact]
        public void CycleSourceAsync_WhenTargetSourceNotOnDevice_DoesNotThrow()
        {
            var options = new CambridgeAudioOptions
            {
                SourceSwitchingEnabled = true,
                SourceSwitchingNames = "PC,HDMI"   // HDMI not available on device
            };
            var (worker, cam) = CreateWorkerWithOptions(options);
            cam.Sources = MakeSources(("pc_usb", "PC"), ("tv_arc", "TV"));
            cam.State = new CambridgeAudioState { Source = "pc_usb" };

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeySourceSwitchRequested",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var exception = Record.Exception(() =>
            {
                handler.Invoke(worker, new object?[] { null, EventArgs.Empty });
                Thread.Sleep(200);
            });

            Assert.Null(exception);
            Assert.Empty(cam.SetSourceIds);
        }
    }
}
