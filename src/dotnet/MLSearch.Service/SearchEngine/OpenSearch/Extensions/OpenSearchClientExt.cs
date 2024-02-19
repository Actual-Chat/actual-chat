using OpenSearch.Client;
using OpenSearch.Client.Specification.HttpApi;
using OpenSearch.Net;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch.Extensions;

public static class OpenSearchClientExt
{
    /// <summary>
    /// A helper method to directly consume OpenSearch documentation examples.
    /// </summary>
    /// <param name="http">OpenSearchClient.Http</param>
    /// <param name="script">A script from the OpenSearch documentation.</param>
    /// <returns></returns>
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
    public static DynamicResponse Run(this HttpNamespace http, String script)
    {
        using var reader = new StringReader(script);
        string headline = reader.ReadLine() ?? throw new InvalidOperationException("Script is expected to have a method and a path on the first line");
        string json = reader.ReadToEnd();
        return headline.Trim().Split(' ') switch {
            ["PUT", var path] => http.Put<DynamicResponse>(path, e => e.Body(json)),
            _ => throw new InvalidOperationException("Unknown script directive")
        };
    }
}
