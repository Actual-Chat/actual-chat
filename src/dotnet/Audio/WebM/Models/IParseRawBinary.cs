namespace ActualChat.Audio.WebM.Models;

public interface IParseRawBinary
{
    void Parse(ReadOnlySpan<byte> span);
}
