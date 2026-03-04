namespace ActualChat.Media;

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
