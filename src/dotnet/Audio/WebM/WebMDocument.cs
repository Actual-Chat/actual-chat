using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.WebM;

public record WebMDocument(EBML Ebml, Segment Segment, IReadOnlyList<Cluster> Clusters)
{
    public bool IsValid
        => Ebml != null! && Segment != null! && Clusters != null! && Clusters.Count != 0;
};
