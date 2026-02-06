namespace ActualChat.Audio.WebM.Models;

/// <summary>
/// Base class for top-level WebM elements (EBML, Segment, Cluster).
/// </summary>
public abstract class RootEntry : BaseModel
{
    public bool IsCompleted { get; private set; }

    public void Complete()
        => IsCompleted = true;
}
