using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
// note: avoid direct dependency on Microsoft.Win32.SystemEvents types in tests
using VolumeAssistant.Service;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;
using Xunit;

namespace VolumeAssistant.Tests
{
    public class PowerModeTests
    {
        private sealed class RecordingCambridgeAudioClient : ICambridgeAudioClient
        {
            public event EventHandler<CambridgeAudioStateChangedEventArgs>? StateChanged { add { } remove { } }
            public event EventHandler<CambridgeAudioConnectionChangedEventArgs>? ConnectionChanged;

            public bool IsConnected { get; set; } = true;

            public void FireConnectionChanged(bool isConnected)
                => ConnectionChanged?.Invoke(this, new CambridgeAudioConnectionChangedEventArgs(isConnected));
            public CambridgeAudioInfo? Info => null;
            public System.Collections.Generic.IReadOnlyList<CambridgeAudioSource> Sources => Array.Empty<CambridgeAudioSource>();
            public CambridgeAudioState? State => null;

            public int PowerOnCalls { get; private set; }
            public int PowerOffCalls { get; private set; }

            public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task DisconnectAsync() => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public Task<CambridgeAudioInfo> GetInfoAsync(CancellationToken ct = default) => Task.FromResult(new CambridgeAudioInfo());
            public Task<System.Collections.Generic.IReadOnlyList<CambridgeAudioSource>> GetSourcesAsync(CancellationToken ct = default)
                => Task.FromResult<System.Collections.Generic.IReadOnlyList<CambridgeAudioSource>>(Array.Empty<CambridgeAudioSource>());
            public Task<CambridgeAudioState> GetStateAsync(CancellationToken ct = default) => Task.FromResult(new CambridgeAudioState());

            public Task SetVolumeAsync(int volumePercent, CancellationToken ct = default) => Task.CompletedTask;
            public Task SetMuteAsync(bool muted, CancellationToken ct = default) => Task.CompletedTask;
            public Task SetSourceAsync(string sourceId, CancellationToken ct = default) => Task.CompletedTask;
            public Task SetAudioOutputAsync(string output, CancellationToken ct = default) => Task.CompletedTask;
            public Task PowerOnAsync(CancellationToken ct = default)
            {
                PowerOnCalls++;
                return Task.CompletedTask;
            }
            public Task PowerOffAsync(CancellationToken ct = default)
            {
                PowerOffCalls++;
                return Task.CompletedTask;
            }
            public Task PlayPauseAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task NextTrackAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task PreviousTrackAsync(CancellationToken ct = default) => Task.CompletedTask;
            public Task SetBalanceAsync(int balance, CancellationToken ct = default) => Task.CompletedTask;
        }

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

            public void RaiseVolumeChanged(float volumePercent, bool muted)
            {
                LastSetVolume = volumePercent;
                LastSetMuted = muted;
                VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(volumePercent, muted));
            }

            public void Dispose() { }
        }

        private sealed class FakePowerEventArgs : EventArgs
        {
            public string Mode { get; }
            public FakePowerEventArgs(string mode) => Mode = mode;
        }

        [Fact]
        public void Resume_TriggersPowerOn_WhenStartPowerTrue()
        {
            var audio = new TestAudioController();
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);

            var cam = new RecordingCambridgeAudioClient();
            var options = new CambridgeAudioOptions { StartPower = true };

            var worker = new Worker(audio, matterDevice, matterServer, mdns, NullLogger<Worker>.Instance, cam, Options.Create(options));

            var onPower = typeof(Worker).GetMethod("OnPowerModeChangedInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            // Simulate resume with a fake EventArgs that contains a Mode property
            var fakeArgs = new FakePowerEventArgs("Resume");
            onPower.Invoke(worker, new object?[] { null, fakeArgs });

            // Allow background task to run
            Thread.Sleep(50);

            Assert.Equal(1, cam.PowerOnCalls);
        }

        [Fact]
        public void Suspend_TriggersPowerOff_WhenClosePowerTrue()
        {
            var audio = new TestAudioController();
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);

            var cam = new RecordingCambridgeAudioClient();
            var options = new CambridgeAudioOptions { ClosePower = true };

            var worker = new Worker(audio, matterDevice, matterServer, mdns, NullLogger<Worker>.Instance, cam, Options.Create(options));

            var onPower = typeof(Worker).GetMethod("OnPowerModeChangedInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            // Simulate suspend with a fake EventArgs that contains a Mode property
            var fakeArgs = new FakePowerEventArgs("Suspend");
            onPower.Invoke(worker, new object?[] { null, fakeArgs });
            // Allow background task to run
            Thread.Sleep(50);

            Assert.Equal(1, cam.PowerOffCalls);
        }

        [Fact]
        public async Task Resume_WaitsForConnectionChangedEvent_BeforePowerOn()
        {
            var audio = new TestAudioController();
            var matterDevice = new MatterDevice();
            var matterServer = new MatterServer(matterDevice, NullLogger<MatterServer>.Instance);
            var mdns = new MdnsAdvertiser(matterDevice, NullLogger<MdnsAdvertiser>.Instance);

            var cam = new RecordingCambridgeAudioClient { IsConnected = false };
            var options = new CambridgeAudioOptions { StartPower = true };

            var worker = new Worker(audio, matterDevice, matterServer, mdns, NullLogger<Worker>.Instance, cam, Options.Create(options));

            var onPower = typeof(Worker).GetMethod("OnPowerModeChangedInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            var fakeArgs = new FakePowerEventArgs("Resume");
            onPower.Invoke(worker, new object?[] { null, fakeArgs });

            // Give the background task time to subscribe to ConnectionChanged
            await Task.Delay(50);

            // Simulate device reconnecting
            cam.IsConnected = true;
            cam.FireConnectionChanged(true);

            // Allow background task to complete power-on
            await Task.Delay(100);

            Assert.Equal(1, cam.PowerOnCalls);
        }
    }
}
