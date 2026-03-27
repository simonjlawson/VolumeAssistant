namespace VolumeAssistant.App;

internal sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Default balance offset applied when no configuration is provided.</summary>
    public const float DefaultBalanceOffset = -20f;

    /// <summary>
    /// Whether to show the transient source popup for source-switch events.
    /// Defaults to true.
    /// </summary>
    public bool UseSourcePopup { get; set; } = true;

    /// <summary>
    /// The stereo balance offset applied when the balance toggle (Shift+PrintScreen) is
    /// activated. A negative value shifts audio towards the left channel (e.g. -20 reduces
    /// the right channel by 20 %). A positive value shifts towards the right channel.
    /// Range is -100 to +100. Defaults to -20.
    /// </summary>
    public float BalanceOffset { get; set; } = DefaultBalanceOffset;

    /// <summary>
    /// When true, pressing Shift+PrintScreen will adjust the Windows audio stereo balance
    /// using the value in <see cref="BalanceOffset"/>.
    /// Defaults to false.
    /// </summary>
    public bool AdjustWindowsBalance { get; set; } = false;

    /// <summary>
    /// When true, the balance offset in <see cref="BalanceOffset"/> is applied immediately
    /// on startup (as if the balance toggle is already active), so the configured balance
    /// is the default face rather than requiring a manual key press.
    /// Defaults to false.
    /// </summary>
    public bool ApplyBalanceOnStartup { get; set; } = false;
}
