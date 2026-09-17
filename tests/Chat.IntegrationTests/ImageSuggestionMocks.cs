using ActualChat.AI;
using ActualChat.Chat.ML;

namespace ActualChat.Chat.IntegrationTests;

// Stand-ins for the two paid services under IImageSuggestions, so these tests exercise the
// permission checks and the dedup rules without calling Cloudflare or OpenAI.

public sealed class ImageGeneratorMock : IImageGenerator
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private int _callCount;
    private string? _lastPrompt;

    public bool IsAvailable => true;
    public int CallCount => Volatile.Read(ref _callCount);
    public string? LastPrompt => Volatile.Read(ref _lastPrompt);

    public Task<GeneratedImage?> Generate(ImageGenerationRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        Volatile.Write(ref _lastPrompt, request.Prompt);
        return Task.FromResult<GeneratedImage?>(
            new GeneratedImage("image/png", request.Width, request.Height, PngBytes));
    }

    public void Reset()
    {
        Volatile.Write(ref _callCount, 0);
        Volatile.Write(ref _lastPrompt, null);
    }
}

public sealed class ChatImageDescriberMock : IChatImageDescriber
{
    private int _callCount;

    public string Result { get; set; } = "a stack of books";
    public int CallCount => Volatile.Read(ref _callCount);

    public Task<string> Describe(
        string title,
        string description,
        IReadOnlyCollection<ChatEntrySlim> chatEntries,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(Result);
    }

    public void Reset()
    {
        Volatile.Write(ref _callCount, 0);
        Result = "a stack of books";
    }
}
