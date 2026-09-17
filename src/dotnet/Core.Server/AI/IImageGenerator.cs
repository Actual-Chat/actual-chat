namespace ActualChat.AI;

public interface IImageGenerator
{
    bool IsAvailable { get; }

    // Returns null when the provider's content filter rejects the prompt - an ordinary outcome
    // for user-influenced text, not a failure. Transport, auth and capacity problems throw.
    Task<GeneratedImage?> Generate(ImageGenerationRequest request, CancellationToken cancellationToken);
}

public sealed record ImageGenerationRequest(string Prompt)
{
    public int Width { get; init; } = 512;
    public int Height { get; init; } = 512;
    // Omitted by default so repeated generations for the same prompt differ
    public long? Seed { get; init; }
}

public sealed record GeneratedImage(string ContentType, int Width, int Height, byte[] Data);
