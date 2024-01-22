using Elastic.Clients.Elasticsearch;
using Elastic.Transport.Products.Elasticsearch;

namespace ActualChat.Search;

public static class ElasticExt
{
    public const string EntriesIndexVersion = "v1";
    public const string IndexNamePrefix = $"entries-{EntriesIndexVersion}";
    public const string IndexTemplateName = IndexNamePrefix;
    public const string IndexPattern = $"{IndexNamePrefix}-*";

    public static IndexName ToIndexName(this Chat.Chat chat)
        => chat.Id.ToIndexName(chat.IsPublicPlaceChat());

    public static IndexName ToIndexName(this ChatId chatId, bool isPublicPlaceChat)
        => isPublicPlaceChat
            ? chatId.PlaceId.ToIndexName()
            : ToIndexName(chatId.Value);

    public static IndexName ToIndexName(this PlaceId placeId)
        => ToIndexName(placeId.Value);

    private static string ToIndexName(string sid)
        => $"{IndexNamePrefix}-{sid.ToLowerInvariant()}";

    public static IEnumerable<IndexName> GetPeerChatSearchIndexNamePatterns(UserId userId)
    {
        yield return $"{IndexNamePrefix}-p-{userId.Value.ToLowerInvariant()}-*";
        yield return $"{IndexNamePrefix}-p-*-{userId.Value.ToLowerInvariant()}";
    }

    public static async Task<T> Assert<T>(this Task<T> responseTask, ILogger? log = null) where T : ElasticsearchResponse
    {
        var response = await responseTask.ConfigureAwait(false);
        if (response.IsSuccess())
            return response;

        // response received
        if (response.TryGetElasticsearchServerError(out var error))
            throw StandardError.External($"Elastic request failed: {error.Error.Reason}.".TrimSuffix(":", ".").EnsureSuffix("."));

        // request sending failed
        if (response.ApiCallDetails.OriginalException is { } exc) {
            log ??= DefaultLog;
            log.LogError(exc, "Failed to perform elastic operation");
            throw StandardError.External($"Elastic request failed: {exc.Message}");
        }

        throw StandardError.External("Elastic request failed");
    }
}
