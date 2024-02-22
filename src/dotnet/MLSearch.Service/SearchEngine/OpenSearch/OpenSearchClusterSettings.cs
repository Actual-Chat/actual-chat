using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;
using OpenSearch.Client;

namespace ActualChat.MLSearch.SearchEngine.OpenSearch;

public sealed class OpenSearchClusterSettings
{
    public string OpenSearchClusterUri { get; set; } = "";
    // TODO: Replace ModelId with ModelGroup and search for the latest version instead.
    public string? ModelId { get; set; }
    // TODO: Get this value from the Model config in the OpenSearch cluster
    public int ModelDimension { get; set; }
    public string IngestPipelineName { get; set; } = "ml-ingest-pipeline";
    public string SearchIndexName { get; set; } = "ml-search-index";

    public Id IntoIngestPipelineId()
        => new (IngestPipelineName + IntoUniqueKey());

    public string IntoSearchIndexId()
        => SearchIndexName + IntoUniqueKey();
    public string IntoUniqueKey()
    {
        // This method must generate a unique key per configuration.
        // This key is later used to namespace models and pipelines
        // on the backend cluster.
        var serialized = JsonSerializer.Serialize(this);
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(serialized));
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }
}
