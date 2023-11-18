namespace ActualChat.Audio.WebM;

#pragma warning disable CA1028 // If possible, make the underlying enum type System.Int32
#pragma warning disable CA1008 // Change 'No' to 'None'

[Flags]
public enum Lacing : byte
{
    No =        0b0000000,
    // ReSharper disable once IdentifierTypo
    Xiph =      0b0000010,
    // ReSharper disable once InconsistentNaming
    EBML =      0b0000110,
    FixedSize = 0b0000100,
}
