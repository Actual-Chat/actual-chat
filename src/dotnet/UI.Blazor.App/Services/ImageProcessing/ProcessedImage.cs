namespace ActualChat.UI.Blazor.App.Services;

public record ProcessedImage
{
    public string MimeType { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public long Size { get; init; }
    public bool IsSource { get; init; }
    public bool Declined { get; init; }
}

public sealed record ProcessedWebImage : ProcessedImage
{
    public IJSObjectReference? FileProvider { get; init; }
}

public sealed record ProcessedStreamImage : ProcessedImage
{
    public IJSStreamReference? Stream { get; init; }
}

public sealed record ImageProcessingResult(IFileProvider? FileProvider, Size2D Size, bool Declined = false);
