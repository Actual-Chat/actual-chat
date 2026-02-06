using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.WebM;

/// <summary>
/// Represents a complete WebM document with header, segment, and clusters.
/// </summary>
public record WebMDocument(EBML Ebml, Segment Segment, IReadOnlyList<Cluster> Clusters)
{
    public bool IsValid
        => Ebml != null! && Segment != null! && Clusters != null! && Clusters.Count != 0;
};
