using System.Collections.Generic;
using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.WebM
{
    public record WebMDocument(EBML Ebml, Segment Segment, IReadOnlyList<Cluster> Clusters)
    {
        // ReSharper disable ConditionIsAlwaysTrueOrFalse
        public bool IsValid => Ebml != null && Segment != null && Clusters?.Count > 0;
    };
}