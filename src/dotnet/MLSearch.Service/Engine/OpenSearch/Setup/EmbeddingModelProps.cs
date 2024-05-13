using System.Security.Cryptography;
using System.Text;

namespace ActualChat.MLSearch.Engine.OpenSearch.Setup;

internal sealed class EmbeddingModelProps(string id, int embeddingDimension, string allConfig)
{
    public string Id => id;
    public int EmbeddingDimension => embeddingDimension;

#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
    // This property generates and returns a unique key per configuration.
    // This key is later used as a namespace of models and pipelines
    // on the backend cluster.
    private string? _uniqueKey;
    public string UniqueKey
        => _uniqueKey ??= Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(allConfig)))
            .ToLower(CultureInfo.InvariantCulture);
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms
}
