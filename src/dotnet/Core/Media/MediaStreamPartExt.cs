using ActualChat.Blobs;

namespace ActualChat.Media;

public static class MediaStreamPartExt
{
    public static BlobPart ToBlobPart(this IMediaStreamPart mediaStreamPart)
        => mediaStreamPart.Format?.ToBlobPart() ?? mediaStreamPart.Frame!.ToBlobPart();
}
