using OpenSearch.Client;
using OpenSearch.Net;

using HttpMethod = OpenSearch.Net.HttpMethod;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch.Extensions;

public static class OpenSearchClientExt
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

    public static async Task<BulkResponse> LogErrors(this Task<BulkResponse> responseTask, ILogger log)
    {
        var response = await responseTask.ConfigureAwait(false);
        foreach (var issue in response.ItemsWithErrors) {
            log.LogTrace(issue.ToString());
        }
        if (response.OriginalException is { } exc) {
            log.LogError(exc, "Failed to perform OpenSearch operation");
        }
        return response;
    }

    // Note: Shamelessly copied and modified from Search.Service/ElasticExt.cs
    public static async Task<BulkResponse> AssertSuccess(this Task<BulkResponse> responseTask)
    {
        var response = await responseTask.ConfigureAwait(false);
        if (!response.Errors)
            return response;

        if (response.OriginalException is { } exc) {
            // request sending failed
            throw StandardError.External($"OpenSearch request failed: {exc.Message}");
        }
        throw StandardError.External(
            $"OpenSearch request failed: {response.DebugInformation}."
                .TrimSuffix(":", ".")
                .EnsureSuffix(".")
        );
    }
}
