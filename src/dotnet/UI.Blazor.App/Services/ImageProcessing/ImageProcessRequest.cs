namespace ActualChat.UI.Blazor.App.Services;

public sealed record ImageProcessRequest(
    ImageOutputSpec[] Outputs,
    int MaxMobileEncodePixels = Constants.Attachments.MaxMobileEncodePixelCount);

public sealed record ImageOutputSpec(
    string Kind,
    int? MaxPixels,
    int? MaxLongSide,
    string Codec,
    bool StripMetadata,
    int? MaxPassthroughPixels = null)
{
    public static ImageOutputSpec Main(ImageQualityBudget budget)
        => new("main", budget.MaxPixels, budget.MaxLongSide, "auto", true);

    public static ImageOutputSpec Original(bool stripMetadata)
        => new("main", null, null, "passthrough", stripMetadata, null);

    public static ImageOutputSpec Placeholder()
        => new("placeholder", null, null, "placeholder", true);
}
