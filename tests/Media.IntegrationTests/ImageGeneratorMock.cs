using ActualChat.AI;

namespace ActualChat.Media.IntegrationTests;

// Stands in for Cloudflare so the suggestion tests neither cost money nor take ~10s per image.
// Delay exists to hold a generation open long enough to test what happens to concurrent callers.

public sealed class ImageGeneratorMock : IImageGenerator
{
    // A valid 1x1 PNG - the icon upload processor decodes it, so it has to be real
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private int _callCount;
    private string? _lastPrompt;

    public bool IsAvailable { get; set; } = true;
    public TimeSpan Delay { get; set; }
    public bool MustRejectPrompt { get; set; }

    public int CallCount => Volatile.Read(ref _callCount);
    public string? LastPrompt => Volatile.Read(ref _lastPrompt);

    public async Task<GeneratedImage?> Generate(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        Volatile.Write(ref _lastPrompt, request.Prompt);
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        if (MustRejectPrompt)
            return null;

        return new GeneratedImage("image/png", request.Width, request.Height, PngBytes);
    }

    public void Reset()
    {
        Volatile.Write(ref _callCount, 0);
        Volatile.Write(ref _lastPrompt, null);
        Delay = TimeSpan.Zero;
        MustRejectPrompt = false;
        IsAvailable = true;
    }
}
