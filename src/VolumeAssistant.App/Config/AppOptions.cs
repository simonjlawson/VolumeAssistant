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
    /// Set to 0 to disable balance toggling. Defaults to -20.
    /// </summary>
    public float BalanceOffset { get; set; } = DefaultBalanceOffset;
}
