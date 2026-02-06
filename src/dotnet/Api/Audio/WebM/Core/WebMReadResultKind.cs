namespace ActualChat.Audio.WebM;

/// <summary>
/// Specifies the type of element returned by <see cref="WebMReader"/>.
/// </summary>
public enum WebMReadResultKind
{
    None = 0,
    Ebml,
    Segment,
    BeginCluster,
    CompleteCluster,
    Block,
    BlockGroup
}
