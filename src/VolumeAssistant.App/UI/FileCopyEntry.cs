using System;

namespace VolumeAssistant.App.UI;

/// <summary>Status of a single file copy operation.</summary>
internal enum FileCopyStatus
{
    Pending,
    Success,
    Failed,
}

/// <summary>Represents the result of a single file copy operation tracked by <see cref="PreviousCopiesDialog"/>.</summary>
internal sealed class FileCopyEntry
{
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public FileCopyStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; }

    public FileCopyEntry(string sourcePath, string destinationPath, FileCopyStatus status = FileCopyStatus.Pending,
        string? errorMessage = null, DateTime timestamp = default)
    {
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        Status = status;
        ErrorMessage = errorMessage;
        Timestamp = timestamp == default ? DateTime.Now : timestamp;
    }
}
