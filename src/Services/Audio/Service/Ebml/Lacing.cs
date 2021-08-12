using System;

namespace ActualChat.Audio.Ebml
{
    [Flags]
    public enum Lacing : byte
    {
        No =        0b0000000,
        Xiph =      0b0000010,
        EBML =      0b0000110,
        FixedSize = 0b0000100
    }
}