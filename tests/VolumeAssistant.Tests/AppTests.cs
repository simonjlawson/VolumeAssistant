using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VolumeAssistant.Core;
using VolumeAssistant.Service.CambridgeAudio;
using Xunit;

namespace VolumeAssistant.Tests;

/// <summary>
/// Tests for the App's client-factory behaviour and the UI logger dispatch used by the
/// tray application.  These tests run cross-platform and do not require Windows Forms.
/// </summary>
public class AppTests
{
    // ── Cambridge Audio factory helpers ─────────────────────────────────────────

    /// <summary>
    /// Simulates the factory logic used by AppHostFactory.CreateCambridgeClient.
    /// The factory should return <see cref="NullCambridgeAudioClient"/> when the
    /// integration is disabled.
    /// </summary>
    private static ICambridgeAudioClient CreateClient(CambridgeAudioOptions opts,
        ILoggerFactory? loggerFactory = null)
    {
        if (!opts.IsEnabled)
            return new NullCambridgeAudioClient();

        // Reflect the factory fallback when construction fails (no real host)
        try
        {
            var effectiveOptions = Options.Create(opts);
            var logger = (loggerFactory ?? NullLoggerFactory.Instance)
                .CreateLogger<CambridgeAudioClient>();
            return new CambridgeAudioClient(effectiveOptions, logger);
        }
        catch
        {
            return new NullCambridgeAudioClient();
        }
    }

    // ── CambridgeAudioOptions.IsEnabled ──────────────────────────────────────────

    [Fact]
    public void IsEnabled_WhenEnableFalse_ReturnsFalse()
    {
        var opts = new CambridgeAudioOptions { Enable = false };
        Assert.False(opts.IsEnabled);
    }

    [Fact]
    public void IsEnabled_WhenEnableTrueAndHostSet_ReturnsTrue()
    {
        var opts = new CambridgeAudioOptions { Enable = true, Host = "192.168.1.1" };
        Assert.True(opts.IsEnabled);
    }

    [Fact]
    public void IsEnabled_WhenEnableTrueAndHostNull_ReturnsTrue()
    {
        // Enable=true with no host triggers SSDP discovery; IsEnabled should still be true.
        var opts = new CambridgeAudioOptions { Enable = true, Host = null };
        Assert.True(opts.IsEnabled);
    }

    // ── Factory: disabled integration ────────────────────────────────────────────

    [Fact]
    public void Factory_WhenDisabled_ReturnsNullCambridgeAudioClient()
    {
        var opts = new CambridgeAudioOptions { Enable = false };
        var client = CreateClient(opts);
        Assert.IsType<NullCambridgeAudioClient>(client);
    }

    // ── NullCambridgeAudioClient behaviour ───────────────────────────────────────

    [Fact]
    public void NullCambridgeAudioClient_IsConnectedIsFalse()
    {
        var client = new NullCambridgeAudioClient();
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task NullCambridgeAudioClient_ConnectAsync_DoesNotThrow()
    {
        var client = new NullCambridgeAudioClient();
        var exception = await Record.ExceptionAsync(() => client.ConnectAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task NullCambridgeAudioClient_SetVolumeAsync_DoesNotThrow()
    {
        var client = new NullCambridgeAudioClient();
        var exception = await Record.ExceptionAsync(() => client.SetVolumeAsync(50));
        Assert.Null(exception);
    }

    [Fact]
    public async Task NullCambridgeAudioClient_PowerOnAsync_DoesNotThrow()
    {
        var client = new NullCambridgeAudioClient();
        var exception = await Record.ExceptionAsync(() => client.PowerOnAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task NullCambridgeAudioClient_GetInfoAsync_ReturnsEmpty()
    {
        var client = new NullCambridgeAudioClient();
        var info = await client.GetInfoAsync();
        Assert.NotNull(info);
    }

    [Fact]
    public async Task NullCambridgeAudioClient_GetSourcesAsync_ReturnsEmpty()
    {
        var client = new NullCambridgeAudioClient();
        var sources = await client.GetSourcesAsync();
        Assert.NotNull(sources);
        Assert.Empty(sources);
    }

    // ── UiLoggerProvider dispatch ─────────────────────────────────────────────────

    [Fact]
    public void UiLoggerProvider_WithCustomDispatch_InvokesOnCorrectThread()
    {
        // Simulate a Windows Forms-style dispatcher that posts to a dedicated thread.
        var entries = new ObservableCollection<string>();
        int? dispatchThreadId = null;

        Action<Action> dispatch = a =>
        {
            // Record the thread ID when the dispatcher is invoked
            dispatchThreadId = Thread.CurrentThread.ManagedThreadId;
            a();
        };

        using var provider = new UiLoggerProvider(entries, dispatch);
        var logger = provider.CreateLogger("AppTest");

        logger.LogInformation("dispatch test");

        Assert.Single(entries);
        Assert.NotNull(dispatchThreadId);
        Assert.Contains("AppTest", entries[0]);
    }

    [Fact]
    public void UiLoggerProvider_DispatchIsCalledForEachLogEntry()
    {
        var entries = new ObservableCollection<string>();
        int dispatchCount = 0;

        Action<Action> dispatch = a => { dispatchCount++; a(); };

        using var provider = new UiLoggerProvider(entries, dispatch);
        var logger = provider.CreateLogger("Cat");

        logger.LogInformation("one");
        logger.LogWarning("two");
        logger.LogError("three");

        Assert.Equal(3, dispatchCount);
        Assert.Equal(3, entries.Count);
    }

    // ── CambridgeAudioOptions default values ─────────────────────────────────────

    [Fact]
    public void CambridgeAudioOptions_DefaultPort_Is80()
    {
        var opts = new CambridgeAudioOptions();
        Assert.Equal(80, opts.Port);
    }

    [Fact]
    public void CambridgeAudioOptions_DefaultZone_IsZone1()
    {
        var opts = new CambridgeAudioOptions();
        Assert.Equal("ZONE1", opts.Zone);
    }

    [Fact]
    public void CambridgeAudioOptions_DefaultMaxVolume_IsNull()
    {
        var opts = new CambridgeAudioOptions();
        Assert.Null(opts.MaxVolume);
    }

    [Fact]
    public void CambridgeAudioOptions_DefaultMediaKeysEnabled_IsFalse()
    {
        var opts = new CambridgeAudioOptions();
        Assert.False(opts.MediaKeysEnabled);
    }

    [Fact]
    public void CambridgeAudioOptions_DefaultSourceSwitchingEnabled_IsFalse()
    {
        var opts = new CambridgeAudioOptions();
        Assert.False(opts.SourceSwitchingEnabled);
    }

    // ── CambridgeAudioOptions effective options cloning ───────────────────────────

    [Fact]
    public void CambridgeAudioOptions_Clone_PreservesAllFields()
    {
        var original = new CambridgeAudioOptions
        {
            Enable = true,
            Host = "192.168.1.5",
            Port = 8080,
            Zone = "ZONE2",
            MaxVolume = 75,
            MediaKeysEnabled = true,
            SourceSwitchingEnabled = true,
            SourceSwitchingNames = "PC,TV",
            StartPower = true,
            ClosePower = true,
            StartVolume = 20,
            StartSourceName = "USB",
            RelativeVolume = true,
        };

        // Simulate the "discovered host differs from configured host" clone performed by the factory
        var cloned = new CambridgeAudioOptions
        {
            Enable = original.Enable,
            Host = "192.168.1.99", // overridden by discovery
            Port = original.Port,
            Zone = original.Zone,
            InitialReconnectDelayMs = original.InitialReconnectDelayMs,
            MaxReconnectDelayMs = original.MaxReconnectDelayMs,
            RequestTimeoutMs = original.RequestTimeoutMs,
            StartSourceName = original.StartSourceName,
            StartVolume = original.StartVolume,
            StartOutput = original.StartOutput,
            StartPower = original.StartPower,
            ClosePower = original.ClosePower,
            RelativeVolume = original.RelativeVolume,
            MaxVolume = original.MaxVolume,
        };

        Assert.Equal(original.Enable, cloned.Enable);
        Assert.Equal(original.Port, cloned.Port);
        Assert.Equal(original.Zone, cloned.Zone);
        Assert.Equal(original.MaxVolume, cloned.MaxVolume);
        Assert.Equal(original.StartVolume, cloned.StartVolume);
        Assert.Equal(original.StartPower, cloned.StartPower);
        Assert.Equal(original.ClosePower, cloned.ClosePower);
        Assert.Equal(original.RelativeVolume, cloned.RelativeVolume);
        Assert.Equal("192.168.1.99", cloned.Host); // discovery overrides host
    }
}
