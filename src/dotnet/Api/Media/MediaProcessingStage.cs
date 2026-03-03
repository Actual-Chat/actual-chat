namespace ActualChat.Media;

public enum MediaProcessingStage
{
    Reserved = 0,
    ClientProcessing,
    Uploading,
    ServerProcessing,
    Ready = 0x100,
}
