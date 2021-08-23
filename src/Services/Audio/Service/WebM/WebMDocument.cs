using System.Collections.Generic;
using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.WebM
{
    public record WebMDocument(EBML Ebml, Segment Segment, IReadOnlyList<Cluster> Clusters);
}