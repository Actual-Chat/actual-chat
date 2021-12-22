namespace ActualChat.Media;

public static class MediaStreamPartExt
{
    public static byte[] Serialize(this IMediaStreamPart mediaStreamPart)
        => mediaStreamPart.Format?.Serialize() ?? mediaStreamPart.Frame!.Data;
}
