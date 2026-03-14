using System;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using VolumeAssistant.Core;
using Xunit;

namespace VolumeAssistant.Tests;

/// <summary>
/// Tests for the UiLoggerProvider used by the system tray app to display log output in the UI.
/// </summary>
public class UiLoggerProviderTests
{
    /// <summary>Creates a provider that dispatches inline (no WPF required).</summary>
    private static UiLoggerProvider CreateProvider(ObservableCollection<string> entries)
        => new(entries, action => action());

    [Fact]
    public void Log_InformationMessage_AppearsInEntries()
    {
        var entries = new ObservableCollection<string>();
        using var provider = CreateProvider(entries);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("Hello, {Name}!", "world");

        Assert.Single(entries);
        Assert.Contains("INF", entries[0]);
        Assert.Contains("TestCategory", entries[0]);
        Assert.Contains("Hello, world!", entries[0]);
    }

    [Fact]
    public void Log_WarningMessage_ContainsWrnPrefix()
    {
        var entries = new ObservableCollection<string>();
        using var provider = CreateProvider(entries);
        var logger = provider.CreateLogger("Cat");

        logger.LogWarning("Something went wrong");

        Assert.Single(entries);
        Assert.Contains("WRN", entries[0]);
    }

    [Fact]
    public void Log_ErrorWithException_IncludesExceptionText()
    {
        var entries = new ObservableCollection<string>();
        using var provider = CreateProvider(entries);
        var logger = provider.CreateLogger("Cat");
        var ex = new InvalidOperationException("boom");

        logger.LogError(ex, "Error occurred");

        Assert.Single(entries);
        Assert.Contains("ERR", entries[0]);
        Assert.Contains("boom", entries[0]);
    }

    [Fact]
    public void Log_DebugMessage_IsFilteredOut()
    {
        var entries = new ObservableCollection<string>();
        using var provider = CreateProvider(entries);
        var logger = provider.CreateLogger("Cat");

        logger.LogDebug("debug message");

        Assert.Empty(entries);
    }

    [Fact]
    public void Log_TraceMessage_IsFilteredOut()
    {
        var entries = new ObservableCollection<string>();
        using var provider = CreateProvider(entries);
        var logger = provider.CreateLogger("Cat");

        logger.LogTrace("trace message");

        Assert.Empty(entries);
    }

    [Fact]
    public void Log_ExceedsMaxEntries_OldestEntryRemoved()
    {
        var entries = new ObservableCollection<string>();
        // Use reflection to check actual MaxEntries (500), but for test speed use a small count
        // by filling to slightly above the limit. We'll log 502 messages.
        using var provider = CreateProvider(entries);
        var logger = provider.CreateLogger("Cat");

        // Log 502 info messages; provider caps at 500
        for (int i = 0; i < 502; i++)
            logger.LogInformation("message {Index}", i);

        Assert.Equal(500, entries.Count);
        // The oldest entries (0, 1) should be gone; last entry should contain 501
        Assert.Contains("501", entries[^1]);
    }

    [Fact]
    public void CreateLogger_MultipleCategories_ReturnsDistinctLoggers()
    {
        var entries = new ObservableCollection<string>();
        using var provider = CreateProvider(entries);

        var loggerA = provider.CreateLogger("CategoryA");
        var loggerB = provider.CreateLogger("CategoryB");

        loggerA.LogInformation("from A");
        loggerB.LogInformation("from B");

        Assert.Equal(2, entries.Count);
        Assert.Contains("CategoryA", entries[0]);
        Assert.Contains("CategoryB", entries[1]);
    }

    [Fact]
    public void Log_MessageIncludesTimestamp()
    {
        var entries = new ObservableCollection<string>();
        using var provider = CreateProvider(entries);
        var logger = provider.CreateLogger("Cat");

        logger.LogInformation("test");

        // Timestamp should be formatted as [HH:mm:ss]
        Assert.Matches(@"\[\d{2}:\d{2}:\d{2}\]", entries[0]);
    }

    [Fact]
    public void Log_DefaultDispatch_InvokesInline_WhenNoWpfApplication()
    {
        // Default dispatch should run inline when no WPF Application is available
        var entries = new ObservableCollection<string>();
        using var provider = new UiLoggerProvider(entries); // no explicit dispatch
        var logger = provider.CreateLogger("Cat");

        // Should not throw even without a WPF application running
        var exception = Record.Exception(() => logger.LogInformation("test inline"));
        Assert.Null(exception);
    }
}
