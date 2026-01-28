namespace ActualChat.Media;

public enum MediaStatus
{
    Reserved = 0,
    Preparing = 1,
    Ready = 2,
    Failed = 3,
}

public enum MediaPreparingStage
{
    None = 0x00,

    // Client-side processing
    ClientProcessing = 0x40,
    Transcoding = 0x41,
    Compressing = 0x42,

    // Uploading to server
    Uploading = 0x80,

    // Server-side processing
    ServerProcessing = 0xC0,
    Converting = 0xC1,
    GeneratingThumbnail = 0xC2,
    ProcessingMetadata = 0xC3,
    Validating = 0xC4,
    VirusScanning = 0xC5,
}
