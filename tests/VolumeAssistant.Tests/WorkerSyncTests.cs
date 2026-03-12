using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
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

        [Fact]
        public void CambridgeAudioUpdate_DoesNotLoop_BackToCambridge()
        {
            var audio = new TestAudioController();
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);
            var cam = new TestCambridgeAudioClient();

            var worker = new Worker(audio, matterDevice, matterServer, mdns, NullLogger<Worker>.Instance, cam);

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

            // Simulate Cambridge Audio reporting a change
            cam.RaiseStateChanged(30, false);

            Thread.Sleep(100);

            // Worker should have applied to Windows but should NOT have sent a SetVolume back to Cambridge
            Assert.Empty(cam.SetVolumeCalls);
        }
    }
}
