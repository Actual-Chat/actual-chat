using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// The Chat-side backend of <see cref="IImageSuggestions"/>: it turns a chat or a place into the
/// image description <see cref="Media.IImageSuggestionsBackend"/> generates from.
/// </summary>
/// <remarks>
/// Describing needs an LLM and raw chat history, and API hosts register neither - they operate
/// every backend in client mode. The caller stays responsible for permissions: these methods read
/// what they are given.
/// <para>
/// Neither method is a compute method: a description is a fresh LLM answer, so caching one would
/// pin a single wording forever and nothing would ever invalidate it.
/// </para>
/// </remarks>
public interface IChatImageSuggestionsBackend : IRpcService, IBackendService
{
    Task<string> DescribeChat(ChatId chatId, CancellationToken cancellationToken);
    Task<string> DescribePlace(
        PlaceId placeId,
        ApiArray<ChatId> chatIds,
        bool isBackground,
        CancellationToken cancellationToken);
}
