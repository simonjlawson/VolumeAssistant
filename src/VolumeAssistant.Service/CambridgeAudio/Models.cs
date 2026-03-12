using System.Text.Json.Serialization;

namespace VolumeAssistant.Service.CambridgeAudio;

/// <summary>
/// Device identity information returned by /system/info.
/// </summary>
public sealed class CambridgeAudioInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("unit_id")]
    public string UnitId { get; set; } = string.Empty;

    [JsonPropertyName("api")]
    public string ApiVersion { get; set; } = string.Empty;

    [JsonPropertyName("udn")]
    public string Udn { get; set; } = string.Empty;

    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = string.Empty;

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = string.Empty;
}

/// <summary>
/// Represents a selectable source (input) on the Cambridge Audio device.
/// </summary>
public sealed class CambridgeAudioSource
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("default_name")]
    public string DefaultName { get; set; } = string.Empty;

    [JsonPropertyName("nameable")]
    public bool Nameable { get; set; }

    [JsonPropertyName("ui_selectable")]
    public bool UiSelectable { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("preferred_order")]
    public int? PreferredOrder { get; set; }

    public override string ToString() => $"{Name} ({Id})";
}

/// <summary>
/// Zone state returned by /zone/state.
/// </summary>
public sealed class CambridgeAudioState
{
    /// <summary>ID of the currently selected source.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>Whether the device is powered on.</summary>
    [JsonPropertyName("power")]
    public bool Power { get; set; }

    /// <summary>Volume as a percentage (0–100). Null when pre-amp mode is disabled.</summary>
    [JsonPropertyName("volume_percent")]
    public int? VolumePercent { get; set; }

    /// <summary>Volume in dB. Null on some models.</summary>
    [JsonPropertyName("volume_db")]
    public int? VolumeDb { get; set; }

    /// <summary>Whether the zone is muted.</summary>
    [JsonPropertyName("mute")]
    public bool Mute { get; set; }

    /// <summary>Whether pre-amp mode is enabled (volume control active).</summary>
    [JsonPropertyName("pre_amp_mode")]
    public bool PreAmpMode { get; set; }

    /// <summary>Whether pre-amp state is active.</summary>
    [JsonPropertyName("pre_amp_state")]
    [JsonConverter(typeof(FlexibleBooleanConverter))]
    public bool PreAmpState { get; set; }
}

/// <summary>
/// Event arguments raised when the Cambridge Audio device state changes.
/// </summary>
public sealed class CambridgeAudioStateChangedEventArgs : EventArgs
{
    /// <summary>The new device state.</summary>
    public CambridgeAudioState State { get; }

    public CambridgeAudioStateChangedEventArgs(CambridgeAudioState state)
    {
        State = state;
    }
}

/// <summary>
/// Event arguments raised when the Cambridge Audio device connection state changes.
/// </summary>
public sealed class CambridgeAudioConnectionChangedEventArgs : EventArgs
{
    /// <summary>Whether the device is now connected.</summary>
    public bool IsConnected { get; }

    public CambridgeAudioConnectionChangedEventArgs(bool isConnected)
    {
        IsConnected = isConnected;
    }
}
