using OpenSearch.Client.Specification.HttpApi;
using OpenSearch.Net;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch.Extensions;

public static class OpenSearchClientExt
{
    /// <summary>
    /// A helper method to execute OpenSearch requests
    /// in the form like in documentation examples.
    /// </summary>
    /// <param name="http">OpenSearchClient.Http</param>
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
    public static DynamicResponse Run(this HttpNamespace http, string script)
    {
        using var reader = new StringReader(script);

        var headline = reader.ReadLine()
            ?? throw new InvalidOperationException("Script is expected to have a method and a path on the first line");
        var json = reader.ReadToEnd();

        return headline.Trim().Split(' ') switch {
            ["PUT", var path] => http.Put<DynamicResponse>(path, e => e.Body(json)),
            _ => throw new InvalidOperationException("Unknown script directive")
        };
    }

    /// <summary>
    /// A helper method to asyncronously execute OpenSearch requests
    /// in the form like in documentation examples.
    /// </summary>
    /// <param name="http">OpenSearchClient.Http</param>
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
    public static async Task<DynamicResponse> RunAsync(this HttpNamespace http, string script, CancellationToken cancellationToken)
    {
        using var reader = new StringReader(script);

        var headline = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Script is expected to have a method and a path on the first line");
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        return await (headline.Trim().Split(' ') switch {
            ["PUT", var path] => http.PutAsync<DynamicResponse>(path, e => e.Body(json), cancellationToken),
            _ => throw new InvalidOperationException("Unknown script directive")
        }).ConfigureAwait(false);
    }
}
