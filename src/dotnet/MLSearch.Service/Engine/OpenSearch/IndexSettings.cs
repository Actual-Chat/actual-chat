
namespace ActualChat.MLSearch.Engine.OpenSearch;

internal sealed record IndexSettings(string IndexName, string? ModelId, string? IngestPipelineId);
