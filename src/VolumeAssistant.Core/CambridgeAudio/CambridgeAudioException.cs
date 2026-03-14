namespace VolumeAssistant.Service.CambridgeAudio;

/// <summary>
/// Represents errors returned by the Cambridge Audio StreamMagic device or client.
/// </summary>
public sealed class CambridgeAudioException : Exception
{
    public CambridgeAudioException(string message) : base(message) { }
    public CambridgeAudioException(string message, Exception inner) : base(message, inner) { }
}
