namespace ActualChat.Audio.Ogg;

#pragma warning disable CA1028

[Flags]
public enum OggHeaderTypeFlag: byte
{
    Continued = 1,
    BeginOfStream = 1 << 1,
    EndOfStream = 1 << 2,
}
