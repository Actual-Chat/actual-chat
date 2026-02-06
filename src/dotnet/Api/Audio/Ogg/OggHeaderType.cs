namespace ActualChat.Audio.Ogg;

/// <summary>
/// Flags for Ogg page header type.
/// </summary>
#pragma warning disable CA1028

[Flags]
public enum OggHeaderTypeFlag: byte
{
    Continued = 1,
    BeginOfStream = 1 << 1,
    EndOfStream = 1 << 2,
}
