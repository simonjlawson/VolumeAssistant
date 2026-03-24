using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
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
    public class TransportControlTests
    {
        private static CambridgeAudioClient CreateClient(string zone = "ZONE1")
        {
            var options = Options.Create(new CambridgeAudioOptions { Zone = zone });
            return new CambridgeAudioClient(options, NullLogger<CambridgeAudioClient>.Instance);
        }

        // ── Option defaults ──────────────────────────────────────────────────────

        [Fact]
        public void MediaKeysEnabled_DefaultsToFalse()
        {
            var options = new CambridgeAudioOptions();
            Assert.False(options.MediaKeysEnabled);
        }

        [Fact]
        public void MediaKeysEnabled_CanBeSetToTrue()
        {
            var options = new CambridgeAudioOptions { MediaKeysEnabled = true };
            Assert.True(options.MediaKeysEnabled);
        }

        // ── NullCambridgeAudioClient transport no-ops ────────────────────────────

        [Fact]
        public async Task NullClient_PlayPauseAsync_Completes()
        {
            var client = new NullCambridgeAudioClient();
            await client.PlayPauseAsync();   // must not throw
        }

        [Fact]
        public async Task NullClient_NextTrackAsync_Completes()
        {
            var client = new NullCambridgeAudioClient();
            await client.NextTrackAsync();
        }

        [Fact]
        public async Task NullClient_PreviousTrackAsync_Completes()
        {
            var client = new NullCambridgeAudioClient();
            await client.PreviousTrackAsync();
        }

        // ── Transport methods throw when not connected ───────────────────────────

        [Fact]
        public async Task PlayPauseAsync_WhenNotConnected_ThrowsCambridgeAudioException()
        {
            var client = CreateClient();
            await Assert.ThrowsAsync<CambridgeAudioException>(() => client.PlayPauseAsync());
        }

        [Fact]
        public async Task NextTrackAsync_WhenNotConnected_ThrowsCambridgeAudioException()
        {
            var client = CreateClient();
            await Assert.ThrowsAsync<CambridgeAudioException>(() => client.NextTrackAsync());
        }

        [Fact]
        public async Task PreviousTrackAsync_WhenNotConnected_ThrowsCambridgeAudioException()
        {
            var client = CreateClient();
            await Assert.ThrowsAsync<CambridgeAudioException>(() => client.PreviousTrackAsync());
        }

        // ── RouteMessageAsync correctly routes /zone/play_control responses ───────

        [Fact]
        public async Task RouteMessageAsync_RoutesPlayControlResponseToPendingTcs()
        {
            var client = CreateClient();

            var pendingField = typeof(CambridgeAudioClient).GetField(
                "_pendingRequests",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var dict = new ConcurrentDictionary<string, ConcurrentQueue<TaskCompletionSource<JsonElement>>>();
            var queue = new ConcurrentQueue<TaskCompletionSource<JsonElement>>();
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(tcs);
            dict.TryAdd("/zone/play_control", queue);
            pendingField.SetValue(client, dict);

            string json = "{\"path\":\"/zone/play_control\",\"type\":\"response\",\"result\":200,\"params\":{\"data\":{}}}";
            using var doc = JsonDocument.Parse(json);
            var mi = typeof(CambridgeAudioClient).GetMethod(
                "RouteMessageAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var task = (Task)mi.Invoke(client, new object[] { doc.RootElement.Clone() })!;
            await task;

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
            Assert.Equal(tcs.Task, completed);
        }

        // ── Worker media key event handlers ──────────────────────────────────────

        private sealed class RecordingCambridgeAudioClient : ICambridgeAudioClient
        {
            public event EventHandler<CambridgeAudioStateChangedEventArgs>? StateChanged { add { } remove { } }
            public event EventHandler<CambridgeAudioConnectionChangedEventArgs>? ConnectionChanged { add { } remove { } }

            public bool IsConnected { get; set; } = true;
            public CambridgeAudioInfo? Info => null;
            public IReadOnlyList<CambridgeAudioSource> Sources => Array.Empty<CambridgeAudioSource>();
            public CambridgeAudioState? State => null;

            public int PlayPauseCalls { get; private set; }
            public int NextTrackCalls { get; private set; }
            public int PreviousTrackCalls { get; private set; }

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

        private static (Worker worker, RecordingCambridgeAudioClient cam) CreateWorkerWithCam()
        {
            var audio = new NopAudioController();
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);
            var cam = new RecordingCambridgeAudioClient();

            var worker = new Worker(
                audio, matterDevice, matterServer, mdns,
                NullLogger<Worker>.Instance, cam);

            return (worker, cam);
        }

        [Fact]
        public void OnMediaKeyPlayPause_WhenConnected_CallsPlayPauseAsync()
        {
            var (worker, cam) = CreateWorkerWithCam();

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeyPlayPause",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });

            Thread.Sleep(100);

            Assert.Equal(1, cam.PlayPauseCalls);
        }

        [Fact]
        public void OnMediaKeyNextTrack_WhenConnected_CallsNextTrackAsync()
        {
            var (worker, cam) = CreateWorkerWithCam();

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeyNextTrack",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });

            Thread.Sleep(100);

            Assert.Equal(1, cam.NextTrackCalls);
        }

        [Fact]
        public void OnMediaKeyPreviousTrack_WhenConnected_CallsPreviousTrackAsync()
        {
            var (worker, cam) = CreateWorkerWithCam();

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeyPreviousTrack",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });

            Thread.Sleep(100);

            Assert.Equal(1, cam.PreviousTrackCalls);
        }

        [Fact]
        public void OnMediaKeyPlayPause_WhenNotConnected_DoesNotCallPlayPauseAsync()
        {
            var (worker, cam) = CreateWorkerWithCam();
            cam.IsConnected = false;

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeyPlayPause",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });

            Thread.Sleep(100);

            Assert.Equal(0, cam.PlayPauseCalls);
        }

        [Fact]
        public void OnMediaKeyNextTrack_WhenNotConnected_DoesNotCallNextTrackAsync()
        {
            var (worker, cam) = CreateWorkerWithCam();
            cam.IsConnected = false;

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeyNextTrack",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });

            Thread.Sleep(100);

            Assert.Equal(0, cam.NextTrackCalls);
        }

        [Fact]
        public void OnMediaKeyPreviousTrack_WhenNotConnected_DoesNotCallPreviousTrackAsync()
        {
            var (worker, cam) = CreateWorkerWithCam();
            cam.IsConnected = false;

            var handler = typeof(Worker).GetMethod(
                "OnMediaKeyPreviousTrack",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            handler.Invoke(worker, new object?[] { null, EventArgs.Empty });

            Thread.Sleep(100);

            Assert.Equal(0, cam.PreviousTrackCalls);
        }
    }
}
