namespace ActualChat.UI.Blazor.App.Services;

public sealed record ImageProcessRequest(ImageOutputSpec[] Outputs);

public sealed record ImageOutputSpec(
    string Kind,
    int? MaxSize,
    string Codec,
    bool StripMetadata,
    int? MaxPassthroughSize = null)
{
    public static ImageOutputSpec Main(int maxSize)
        => new("main", maxSize, "auto", true);

    public static ImageOutputSpec Estimate(int maxSize)
        => new("estimate", maxSize, "auto", true);

    public static ImageOutputSpec Original(bool stripMetadata)
        // A longer side than 8K is re-encoded down to 8K even for "Original"
        => new("main", Constants.Attachments.MaxImageSize, "passthrough", stripMetadata, Constants.Attachments.MaxImageSize);
}

public sealed record ImageSizeEstimate(long Uhd4KLength, long FullHdLength);
