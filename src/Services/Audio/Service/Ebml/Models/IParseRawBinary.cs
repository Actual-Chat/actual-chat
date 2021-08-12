using System;

namespace ActualChat.Audio.Ebml.Models
{
    public interface IParseRawBinary
    {
        void Parse(Span<byte> span);
    }
}