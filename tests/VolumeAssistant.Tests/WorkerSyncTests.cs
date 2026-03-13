using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VolumeAssistant.Service;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;
using VolumeAssistant.Service.Matter.Clusters;
using Xunit;

namespace VolumeAssistant.Tests
{
    public class WorkerSyncTests
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

            public void RaiseVolumeChanged(float volumePercent, bool muted)
            {
                LastSetVolume = volumePercent;
                LastSetMuted = muted;
                VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(volumePercent, muted));
            }

            public void Dispose() { }
        }

        private sealed class TestCambridgeAudioClient : ICambridgeAudioClient
        {
            public event EventHandler<CambridgeAudioStateChangedEventArgs>? StateChanged { add { _stateChanged += value; } remove { _stateChanged -= value; } }
            private EventHandler<CambridgeAudioStateChangedEventArgs>? _stateChanged;
            public event EventHandler<CambridgeAudioConnectionChangedEventArgs>? ConnectionChanged { add { } remove { } }

            public bool IsConnected { get; set; } = true;
            public CambridgeAudioInfo? Info => null;
            public IReadOnlyList<CambridgeAudioSource> Sources => Array.Empty<CambridgeAudioSource>();
            public CambridgeAudioState? State => null;

            public List<int> SetVolumeCalls { get; } = new();
            public List<bool> SetMuteCalls { get; } = new();

            public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task DisconnectAsync() => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public Task<CambridgeAudioInfo> GetInfoAsync(CancellationToken ct = default) => Task.FromResult(new CambridgeAudioInfo());
            public Task<IReadOnlyList<CambridgeAudioSource>> GetSourcesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CambridgeAudioSource>>(Array.Empty<CambridgeAudioSource>());
            public Task<CambridgeAudioState> GetStateAsync(CancellationToken ct = default) => Task.FromResult(new CambridgeAudioState());

            public Task SetVolumeAsync(int volumePercent, CancellationToken ct = default)
            {
                SetVolumeCalls.Add(volumePercent);
                return Task.CompletedTask;
            }

            public Task SetMuteAsync(bool muted, CancellationToken ct = default)
            {
                SetMuteCalls.Add(muted);
                return Task.CompletedTask;
            }

            public Task SetSourceAsync(string sourceId, CancellationToken ct = default) => Task.CompletedTask;
            public Task SetAudioOutputAsync(string output, CancellationToken ct = default) => Task.CompletedTask;
            public Task PowerOnAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task PowerOffAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task PlayPauseAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task NextTrackAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task PreviousTrackAsync(CancellationToken ct = default) => Task.CompletedTask;

            public void RaiseStateChanged(int volumePercent, bool mute)
            {
                _stateChanged?.Invoke(this, new CambridgeAudioStateChangedEventArgs(new CambridgeAudioState
                {
                    VolumePercent = volumePercent,
                    Mute = mute,
                }));
            }
        }

        [Fact]
        public void MatterCommand_DoesNotLoop_WhenApplyingToWindows()
        {
            var audio = new TestAudioController();
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);
            var cam = new TestCambridgeAudioClient();

            var worker = new Worker(audio, matterDevice, matterServer, mdns, NullLogger<Worker>.Instance, cam);

            // Attach worker handlers to test collaborators using reflection
            var workerType = typeof(Worker);
            var onMatter = workerType.GetMethod("OnMatterDeviceStateChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var onWindows = workerType.GetMethod("OnWindowsVolumeChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var onCam = workerType.GetMethod("OnCambridgeAudioStateChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            var matterDelegateType = typeof(EventHandler<>).MakeGenericType(typeof(ValueTuple<byte, bool>));
            var matterDel = Delegate.CreateDelegate(matterDelegateType, worker, onMatter);
            matterDevice.DeviceStateChanged += (EventHandler<(byte Level, bool IsOn)>)matterDel;

            var windowsDel = (EventHandler<VolumeChangedEventArgs>)Delegate.CreateDelegate(typeof(EventHandler<VolumeChangedEventArgs>), worker, onWindows);
            audio.VolumeChanged += windowsDel;

            var camDel = (EventHandler<CambridgeAudioStateChangedEventArgs>)Delegate.CreateDelegate(typeof(EventHandler<CambridgeAudioStateChangedEventArgs>), worker, onCam);
            cam.StateChanged += camDel;

            // Simulate Matter command by changing device level
            matterDevice.LevelControlCluster.CurrentLevel = 128;

            // Allow background tasks scheduled by handlers to run
            Thread.Sleep(100);

            // CambridgeAudio should have received one SetVolume call (from Matter -> worker -> cambridge)
            Assert.Single(cam.SetVolumeCalls);
        }

        private static (Worker worker, TestAudioController audio, TestCambridgeAudioClient cam) CreateWorkerWithOptions(CambridgeAudioOptions options)
        {
            var audio = new TestAudioController();
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);
            var cam = new TestCambridgeAudioClient();

            var worker = new Worker(
                audio, matterDevice, matterServer, mdns, NullLogger<Worker>.Instance, cam,
                Options.Create(options));

            // Initialise the syncer (normally created in ExecuteAsync) so that
            // OnWindowsVolumeChanged can enqueue volume changes.
            var syncer = new CambridgeAudioSyncer(cam, options, NullLogger<Worker>.Instance);
            var syncerField = typeof(Worker).GetField("_cambridgeSyncer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            syncerField.SetValue(worker, syncer);

            var workerType = typeof(Worker);
            var onWindows = workerType.GetMethod("OnWindowsVolumeChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var onCam = workerType.GetMethod("OnCambridgeAudioStateChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            var windowsDel = (EventHandler<VolumeChangedEventArgs>)Delegate.CreateDelegate(typeof(EventHandler<VolumeChangedEventArgs>), worker, onWindows);
            audio.VolumeChanged += windowsDel;

            var camDel = (EventHandler<CambridgeAudioStateChangedEventArgs>)Delegate.CreateDelegate(typeof(EventHandler<CambridgeAudioStateChangedEventArgs>), worker, onCam);
            cam.StateChanged += camDel;

            return (worker, audio, cam);
        }

        [Theory]
        [InlineData(100f, 80, 80)]  // Windows 100% → Cambridge 80 (MaxVolume)
        [InlineData(50f,  80, 40)]  // Windows 50%  → Cambridge 40 (50% of 80)
        [InlineData(0f,   80, 0)]   // Windows 0%   → Cambridge 0
        [InlineData(25f,  60, 15)]  // Windows 25%  → Cambridge 15 (25% of 60)
        public void WindowsVolumeChange_WithMaxVolume_ScalesCambridgeVolume(
            float windowsPercent, int maxVolume, int expectedCamVolume)
        {
            var options = new CambridgeAudioOptions { MaxVolume = maxVolume, RelativeVolume = false };
            var (_, audio, cam) = CreateWorkerWithOptions(options);

            audio.RaiseVolumeChanged(windowsPercent, false);
            Thread.Sleep(100);

            Assert.Single(cam.SetVolumeCalls);
            Assert.Equal(expectedCamVolume, cam.SetVolumeCalls[0]);
        }

        [Theory]
        [InlineData(80f,  80,  100f)]  // Cambridge 80 (MaxVolume) → Windows 100%
        [InlineData(40f,  80,  50f)]   // Cambridge 40 → Windows 50%
        [InlineData(0f,   80,  0f)]    // Cambridge 0 → Windows 0%
        [InlineData(60f,  80,  75f)]   // Cambridge 60 → Windows 75%
        public void CambridgeVolumeChange_WithMaxVolume_ScalesWindowsVolume(
            float cambridgePercent, int maxVolume, float expectedWindowsVolume)
        {
            var options = new CambridgeAudioOptions { MaxVolume = maxVolume, RelativeVolume = false };
            var (_, audio, cam) = CreateWorkerWithOptions(options);

            cam.RaiseStateChanged((int)cambridgePercent, false);
            Thread.Sleep(100);

            Assert.Equal(expectedWindowsVolume, audio.LastSetVolume, 1);
        }

        [Theory]
        [InlineData(100f, 100)]  // Without MaxVolume: Windows 100% → Cambridge 100
        [InlineData(50f,  50)]   // Without MaxVolume: Windows 50% → Cambridge 50
        public void WindowsVolumeChange_WithoutMaxVolume_PassesThroughUnchanged(
            float windowsPercent, int expectedCamVolume)
        {
            var options = new CambridgeAudioOptions { MaxVolume = null, RelativeVolume = false };
            var (_, audio, cam) = CreateWorkerWithOptions(options);

            audio.RaiseVolumeChanged(windowsPercent, false);
            Thread.Sleep(100);

            Assert.Single(cam.SetVolumeCalls);
            Assert.Equal(expectedCamVolume, cam.SetVolumeCalls[0]);
        }

    }
}
