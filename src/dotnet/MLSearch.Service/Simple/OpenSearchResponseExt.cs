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
            throw StandardError.External($"OpenSearch request failed: {error.Error.Reason}.".TrimSuffix(":", ".")
                .EnsureSuffix("."));

        log ??= StaticLog.For(typeof(OpenSearchResponseExt));
        if (response is BulkResponse { IsValid: false } bulkResponse && bulkResponse.ItemsWithErrors.Count() is var failureCount and > 0) {
            var firstFailed = bulkResponse.ItemsWithErrors.FirstOrDefault();
            var firstFailedReason = firstFailed?.Error?.Reason ?? "N/A";
            log.LogError("OpenSearch Bulk request failed: {ItemsFailed}, {FailureReason}", failureCount, firstFailedReason);
            throw StandardError.External($"OpenSearch bulk request failed for {failureCount} items: {firstFailedReason}."
                .TrimSuffix(":", ".")
                .Trim()
                .EnsureSuffix("."));
        }

        // request sending failed
        if (response.ApiCall.OriginalException is { } exc) {
            log.LogError(exc, "Failed to perform OpenSearch operation");
            throw StandardError.External($"OpenSearch request failed: {exc.Message}");
        }

        throw StandardError.External("OpenSearch request failed");
    }

    public static MultiMatchQueryDescriptor<TDocument> Fields<TDocument, TValue>(
        this MultiMatchQueryDescriptor<TDocument> multiMatchQueryDescriptor,
        params Expression<Func<TDocument, TValue>>[] fields)
        where TDocument : class
        => multiMatchQueryDescriptor.Fields(fields);
}
