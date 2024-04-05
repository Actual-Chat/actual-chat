using System.Security.Cryptography;
using System.Text;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch.Setup;

public sealed class ClusterSettings(string modelAllConfig, string modelId, int modelEmbeddingDimension)
{
    private const string NamePrefix = "ml";
    private const string IngestPipelineNameSuffix = "ingest-pipeline";
    private const string IndexNameSuffix = "index";

    public string ModelId => modelId;
    public int ModelEmbeddingDimension => modelEmbeddingDimension;

#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
    // This property generates and returns a unique key per configuration.
    // This key is later used as a namespace of models and pipelines
    // on the backend cluster.
    private string? _uniqueKey;
    private string UniqueKey
        => _uniqueKey ??= Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(modelAllConfig)))
            .ToLower(CultureInfo.InvariantCulture);
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

    public string IntoFullIngestPipelineName(string id)
        => string.Join('-',
            NamePrefix,
            id,
            IngestPipelineNameSuffix,
            UniqueKey);

    public IndexName IntoFullIndexName(string id)
        => string.Join('-',
            NamePrefix,
            id,
            IndexNameSuffix,
            UniqueKey);
}
