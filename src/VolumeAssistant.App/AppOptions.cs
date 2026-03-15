namespace VolumeAssistant.App;

internal sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// Whether to show the transient source popup for source-switch events.
    /// Defaults to true.
    /// </summary>
    public bool UseSourcePopup { get; set; } = true;
}
