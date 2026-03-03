namespace ActualChat.Media;

public enum MediaProcessingStage
{
    Reserved,
    ClientProcessing,
    Uploading,
    Uploaded,
    ServerProcessing,
    Saving,
    Ready
}
