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

    // Note: Shamelessly copied and modified from Search.Service/ElasticExt.cs
    public static T AssertSuccess<T>(this T response, bool allowNotFound = false)
    where T: ResponseBase
    {
        if (response.IsValid)
            return response;

        if (response.ApiCall.Success
            && response.ApiCall.HttpStatusCode == 404
            && allowNotFound
        )
        {
            return response;
        }

        if (response.OriginalException is { } exc) {
            // request sending failed
            throw StandardError.External($"OpenSearch request failed: {exc.Message}", exc);
        }
        if (response.ServerError is { } err) {
            // request sending failed
            throw StandardError.External($"OpenSearch request failed: {err}");
        }
        throw StandardError.External(
            $"OpenSearch request failed: {response.DebugInformation}."
                .TrimSuffix(":", ".")
                .EnsureSuffix(".")
        );
    }

    public static async Task<string> ToJsonAsync(
        this ISearchRequest searchRequest,
        IOpenSearchClient openSearch,
        CancellationToken cancellationToken)
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
}
