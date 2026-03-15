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

        // Build the formatted message once into a single string allocation to
        // reduce transient string work and avoid concatenation churn.
        string content = formatter(state, exception);

        // If an exception is present, include only the exception message (not full ToString)
        // to keep in-memory logs compact. This preserves the important error text while
        // avoiding large stack-trace strings in the UI log.
        string? exPart = exception?.Message;

        // Compute total length for single-allocation formatting.
        // Pattern: "[HH:mm:ss] PRI Category: content" (+ " | exMessage" if present)
        string timePart = DateTime.Now.ToString("HH:mm:ss");
        string prefix = LogLevelToPrefix(logLevel);

        int totalLength = 1 + timePart.Length + 1 + 1 // [time] and following space
            + prefix.Length + 1 // prefix and space
            + _category.Length + 2 // category + ": "
            + content.Length;

        if (!string.IsNullOrEmpty(exPart))
            totalLength += 3 + exPart.Length; // " | " + ex message

        string final = string.Create(totalLength, (timePart, prefix, content, exPart, category: _category), (span, stateTuple) =>
        {
            var (t, p, c, e, cat) = stateTuple;
            int pos = 0;
            span[pos++] = '[';
            t.CopyTo(span.Slice(pos, t.Length));
            pos += t.Length;
            span[pos++] = ']';
            span[pos++] = ' ';
            p.CopyTo(span.Slice(pos, p.Length));
            pos += p.Length;
            span[pos++] = ' ';
            cat.CopyTo(span.Slice(pos, cat.Length));
            pos += cat.Length;
            span[pos++] = ':';
            span[pos++] = ' ';
            c.CopyTo(span.Slice(pos, c.Length));
            pos += c.Length;
            if (!string.IsNullOrEmpty(e))
            {
                span[pos++] = ' ';
                span[pos++] = '|';
                span[pos++] = ' ';
                e.CopyTo(span.Slice(pos, e.Length));
                pos += e.Length;
            }
        });

        _dispatch(() =>
        {
            _entries.Add(final);
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
