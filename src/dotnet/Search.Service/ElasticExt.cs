using Elastic.Clients.Elasticsearch;
using Elastic.Transport.Products.Elasticsearch;

namespace ActualChat.Search;

public static class ElasticExt
{
    public const string IndexTemplateName = "entries";
    public const string IndexPattern = "entries-*";

    public static IndexName ToIndexName(this ChatId chatId)
        => $"entries-{chatId.Value.ToLowerInvariant()}";

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
