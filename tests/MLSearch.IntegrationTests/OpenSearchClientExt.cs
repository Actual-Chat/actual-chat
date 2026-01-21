using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using OpenSearch.Client;
using OpenSearch.Net;
using OpenSearch.Net.Specification.HttpApi;
using HttpMethod = OpenSearch.Net.HttpMethod;

namespace ActualChat.MLSearch.IntegrationTests;

public readonly struct OpenSearchClientDebugExt
{
    private readonly IOpenSearchClient _client;

    public OpenSearchClientDebugExt(IOpenSearchClient client)
        => _client = client;

    public async Task<string> DumpStat()
    {
        var nodesStatsResponse = await _client.Nodes.StatsAsync();
        var sb = new StringBuilder();
        foreach (var node in nodesStatsResponse.Nodes) {
            sb.AppendLine($"Node ID stat: {node.Key}");
            var breakers = node.Value.Breakers;
            if (breakers == null) {
                sb.AppendLine("  No breakers info");
                continue;
            }
            foreach (var br in breakers) {
                var brKey = br.Key;
                var breakerStats = br.Value;
                sb.AppendLine($"  Breaker: {brKey}");
                sb.AppendLine($"    Estimated Size: {breakerStats.EstimatedSizeInBytes}");
                sb.AppendLine($"    Limit Size: {breakerStats.LimitSizeInBytes}");
                sb.AppendLine($"    Overhead: {breakerStats.Overhead}");
                sb.AppendLine($"    Tripped: {breakerStats.Tripped}");
            }
        }

        var clusterStatsResponse = await _client.Cluster.StatsAsync();
        var fs = clusterStatsResponse.Nodes.FileSystem;
        sb.AppendLine( "  Filesystem:");
        sb.AppendLine($"    Total: {fs.TotalInBytes} bytes");
        sb.AppendLine($"    Free: {fs.FreeInBytes} bytes");
        sb.AppendLine($"    Available: {fs.AvailableInBytes} bytes");
        sb.AppendLine("  Allocation:");
        await LogGetAsync(sb, "/_cat/allocation?v");
        sb.AppendLine("  Nodes disk usage:");
        await LogGetAsync(sb, "/_cat/nodes?v&h=name,disk.total,disk.used,disk.avail,disk.percent");
        sb.AppendLine("  Indexes:");
        await LogGetAsync(sb, "/_cat/indices?v&s=store.size:desc");
        return sb.ToString();
    }

    public async Task<string> DumpSettings()
    {
        var sb = new StringBuilder();
        sb.AppendLine("  Settings:");
        await LogGetAsync(sb, "/_cluster/settings?include_defaults=true");
        return sb.ToString();
    }

    private async Task LogGetAsync(StringBuilder sb, string url)
    {
        var parts = url.Split('?');
        var path = parts[0];
        var query = parts.Length > 1 ? parts[1] : "";
        var queryDict = QueryHelpers.ParseQuery(query);

        var requestParameters = new HttpGetRequestParameters();
        foreach (var kv in queryDict)
            requestParameters.SetQueryString(kv.Key, kv.Value);
        requestParameters.SetQueryString("format", "json");
        var response = await _client
            .LowLevel
            .DoRequestAsync<StringResponse>(
                HttpMethod.GET,
                path,
                CancellationToken.None,
                requestParameters: requestParameters);
        var responseBody = response.Body;
        sb.AppendLine(responseBody);
    }
}

public static class OpenSearchClientExt
{
    public static OpenSearchClientDebugExt Debug(this IOpenSearchClient client) => new(client);
}
