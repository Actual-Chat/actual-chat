namespace ActualChat.Media;

#pragma warning disable CA1700 // Possibly unused enum value ('Reserved')

public enum MediaProcessingStage
{
    Reserved = 0,
    ClientProcessing,
    Uploading,
    Uploaded,
    ServerProcessing,
    Saving,
    Ready,
}
