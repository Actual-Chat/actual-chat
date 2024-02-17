namespace ActualChat.Audio.WebM.Models;

#pragma warning disable CA1028 // If possible, make the underlying enum type System.Int32

public enum EbmlEntryType : byte
{
    None = 0,
    Ebml,
    Segment,
    Cluster,
}
