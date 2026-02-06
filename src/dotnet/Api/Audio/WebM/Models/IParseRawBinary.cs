namespace ActualChat.Audio.WebM.Models;

/// <summary>
/// Interface for models that can parse raw binary data.
/// </summary>
public interface IParseRawBinary
{
    void Parse(ReadOnlySpan<byte> span);
}
