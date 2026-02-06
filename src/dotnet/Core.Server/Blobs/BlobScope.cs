namespace ActualChat.Blobs;

/// <summary>
/// Defines blob storage scope identifiers for different content types.
/// </summary>
public static class BlobScope
{
    public static readonly Symbol AudioRecord = "audio-record";
    public static readonly Symbol ContentRecord = "content-record";
    public static readonly Symbol UploadTempRecord = "upload-temp-record";
}
