using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace VolumeAssistant.Core;

/// <summary>
/// An ILogger provider that captures log messages for display in the UI.
/// Messages are marshalled onto the provided dispatch action (typically the WPF dispatcher).
/// </summary>
public sealed class UiLoggerProvider : ILoggerProvider
{
    private readonly ObservableCollection<string> _entries;
    private readonly Action<Action> _dispatch;
    private const int MaxEntries = 500;

    /// <summary>
    /// Initialises the provider. The <paramref name="dispatch"/> action is called to
    /// marshal each new log entry onto the correct thread (e.g. the WPF UI thread).
    /// </summary>
    public UiLoggerProvider(ObservableCollection<string> entries, Action<Action>? dispatch = null)
    {
        _entries = entries;
        // Default: run inline (callers should provide a UI-thread dispatcher when needed)
        _dispatch = dispatch ?? (a => a());
    }

    public ILogger CreateLogger(string categoryName) =>
        new UiLogger(categoryName, _entries, MaxEntries, _dispatch);

    public void Dispose() { }
}

internal sealed class UiLogger : ILogger
{
    private readonly string _category;
    private readonly ObservableCollection<string> _entries;
    private readonly int _maxEntries;
    private readonly Action<Action> _dispatch;

    internal UiLogger(
        string category,
        ObservableCollection<string> entries,
        int maxEntries,
        Action<Action> dispatch)
    {
        _category = category;
        _entries = entries;
        _maxEntries = maxEntries;
        _dispatch = dispatch;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = $"[{DateTime.Now:HH:mm:ss}] {LogLevelToPrefix(logLevel)} {_category}: {formatter(state, exception)}";
        if (exception != null)
            message += Environment.NewLine + exception.ToString();

        var captured = message;
        _dispatch(() =>
        {
            _entries.Add(captured);
            while (_entries.Count > _maxEntries)
                _entries.RemoveAt(0);
        });
    }

    private static string LogLevelToPrefix(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };
}
