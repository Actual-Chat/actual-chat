using System.Linq.Expressions;
using OpenSearch.Client;

namespace ActualChat.Search;

public static class OpenSearchResponseExt
{
    public static async Task<T> Assert<T>(this Task<T> responseTask, ILogger? log = null) where T : IResponse
    {
        var response = await responseTask.ConfigureAwait(false);
        if (response.IsValid)
            return response;

        // response received
        var error = response.ServerError;
        if (error != null)
            throw StandardError.External($"Elastic request failed: {error.Error.Reason}.".TrimSuffix(":", ".")
                .EnsureSuffix("."));

        // request sending failed
        if (response.ApiCall.OriginalException is { } exc) {
            log ??= DefaultLogFor(typeof(OpenSearchResponseExt));
            log.LogError(exc, "Failed to perform elastic operation");
            throw StandardError.External($"Elastic request failed: {exc.Message}");
        }

        throw StandardError.External("Elastic request failed");
    }

    public static MultiMatchQueryDescriptor<TDocument> Fields<TDocument, TValue>(
        this MultiMatchQueryDescriptor<TDocument> multiMatchQueryDescriptor,
        params Expression<Func<TDocument, TValue>>[] fields)
        where TDocument : class
        => multiMatchQueryDescriptor.Fields(fields);
}
