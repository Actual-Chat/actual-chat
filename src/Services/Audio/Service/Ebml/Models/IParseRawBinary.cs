using System;

namespace ActualChat.Audio.Ebml.Models
{
    public interface IParseRawBinary
    {
        void Parse(ReadOnlySpan<byte> span);
    }
}