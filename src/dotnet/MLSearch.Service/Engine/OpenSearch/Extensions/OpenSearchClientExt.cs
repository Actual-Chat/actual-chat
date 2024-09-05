using System.Linq.Expressions;
using OpenSearch.Client;
using OpenSearch.Net;

using HttpMethod = OpenSearch.Net.HttpMethod;

namespace ActualChat.MLSearch.Engine.OpenSearch.Extensions;

internal static class OpenSearchClientExt
{
    /// <summary>
    /// A helper method to execute OpenSearch requests
    /// in the form like in documentation examples.
    /// </summary>
    /// <param name="openSearch">OpenSearchClient instance.</param>
    /// <param name="script">A script that is based on or similar to an example in the OpenSearch documentation.</param>
    /// <returns>A <see cref="DynamicResponse"/> to be processed.</returns>
    /// <exception cref="InvalidOperationException"></exception>
    /// <example>
    /// OpenSearchClient.Http.Run(
    ///     $$"""
    ///         PUT /_ingest/pipeline/{{name}}
    ///         {
    ///             // Json config.
    ///         }
    ///     """
    /// );
    /// </example>
    public static DynamicResponse Run(this IOpenSearchClient openSearch, string script)
    {
        using var reader = new StringReader(script);

        var headline = reader.ReadLine()
            ?? throw new InvalidOperationException("Script is expected to have a method and a path on the first line");
        var json = reader.ReadToEnd();

        return headline.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) switch {
            ["PUT", var path] => openSearch.LowLevel.DoRequest<DynamicResponse>(HttpMethod.PUT, path, PostData.String(json)),
            _ => throw new InvalidOperationException("Unknown script directive")
        };
    }

    /// <summary>
    /// A helper method to asyncronously execute OpenSearch requests
    /// in the form like in documentation examples.
    /// </summary>
    /// <param name="openSearch">OpenSearchClient instance.</param>
    /// <param name="script">A script that is based on or similar to an example in the OpenSearch documentation.</param>
    /// <returns>Asyncronously returns a <see cref="DynamicResponse"/> to be processed.</returns>
    /// <exception cref="InvalidOperationException"></exception>
    /// <example>
    /// await OpenSearchClient.Http.RunAsync(
    ///     $$"""
    ///         PUT /_ingest/pipeline/{{name}}
    ///         {
    ///             // Json config.
    ///         }
    ///     """
    /// ).ConfigureAwait(false);
    /// </example>
    public static async Task<DynamicResponse> RunAsync(this IOpenSearchClient openSearch, string script, CancellationToken cancellationToken)
    {
        using var reader = new StringReader(script);

        var headline = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Script is expected to have a method and a path on the first line");
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        return await (headline.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) switch {
            ["PUT", var path] => openSearch.LowLevel.DoRequestAsync<DynamicResponse>(HttpMethod.PUT, path, cancellationToken, PostData.String(json)),
            ["POST", var path] => openSearch.LowLevel.DoRequestAsync<DynamicResponse>(HttpMethod.POST, path, cancellationToken, PostData.String(json)),
            _ => throw new InvalidOperationException("Unknown script directive")
        }).ConfigureAwait(false);
    }

    public static T LogErrors<T>(this T log, BulkResponse response)
    where T: ILogger
    {
        foreach (var issue in response.ItemsWithErrors) {
            log.LogTrace(issue.ToString());
        }
        if (response.OriginalException is { } exc) {
            log.LogError(exc, "Failed to perform OpenSearch operation");
        }
        return log;
    }

    public static DynamicResponse AssertSuccess(this DynamicResponse response, bool allowNotFound = false)
        => response.Success && (!response.TryGetServerError(out var error) || error is null || error.Status < 0)
            ? response
            : response.InnerAssertSuccess(allowNotFound);

    public static T AssertSuccess<T>(this T response, bool allowNotFound = false)
        where T : ResponseBase
        => response.IsValid ? response : response.InnerAssertSuccess(allowNotFound);

    // Note: Shamelessly copied and modified from Search.Service/ElasticExt.cs
    private static T InnerAssertSuccess<T>(this T response, bool allowNotFound = false)
        where T: IOpenSearchResponse
    {
        var apiCall = response.ApiCall;

        if (allowNotFound
            && apiCall?.Success == true
            && apiCall.HttpStatusCode == 404) {
            return response;
        }

        if (apiCall?.OriginalException is { } originalException) {
            throw StandardError.External($"OpenSearch request failed: {originalException.Message}", originalException);
        }
        if (response.TryGetServerErrorReason(out var err)) {
            throw StandardError.External($"OpenSearch request failed: {err}");
        }
        var debugInformation = response is ResponseBase { } responseBase
            ? responseBase.DebugInformation
            : apiCall?.DebugInformation ?? "Unknown reason";

        throw StandardError.External(
            $"OpenSearch request failed: {debugInformation}."
                .TrimSuffix(":", ".")
                .EnsureSuffix(".")
        );
    }

    public static async Task<string> ToJsonAsync<TRequest>(
        this TRequest searchRequest,
        IOpenSearchClient openSearch,
        CancellationToken cancellationToken)
        where TRequest : IRequest
    {
        var serializableRequest = PostData.Serializable(searchRequest);
        serializableRequest.DisableDirectStreaming = false;

        var ms = new MemoryStream();
        await serializableRequest.WriteAsync(ms, openSearch.ConnectionSettings, cancellationToken)
            .ConfigureAwait(false);
        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ms);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    // TODO: merge with AssertSuccess
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

        log ??= StaticLog.For(typeof(OpenSearchClientExt));
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

    public static SearchDescriptor<T> Log<T>(this SearchDescriptor<T> descriptor, IOpenSearchClient client, ILogger? log, string description = "") where T : class
        => LogRequest(descriptor, client, log, description);

    public static ICreateIndexRequest Log(this ICreateIndexRequest request, IOpenSearchClient client, ILogger? log, string description = "")
        => LogRequest(request, client, log, description);

    private static T LogRequest<T>(T request, IOpenSearchClient client, ILogger? log, string description)
    {
        if (log == null)
            return request;

        try {
            var s = client.RequestResponseSerializer.SerializeToString(request);
            log.LogDebug("{Description}: {Request}", description, s);
        }
        catch (Exception e) {
            log.LogWarning(e, "Could not log {Description}", description);
        }
        return request;
    }

    public static MultiMatchQueryDescriptor<TDocument> Fields<TDocument, TValue>(
        this MultiMatchQueryDescriptor<TDocument> multiMatchQueryDescriptor,
        params Expression<Func<TDocument, TValue>>[] fields)
        where TDocument : class
        => multiMatchQueryDescriptor.Fields(fields);
}
