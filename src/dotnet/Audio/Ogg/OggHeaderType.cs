namespace ActualChat.Audio.Ogg;

[Flags]
public enum OggHeaderTypeFlag: byte
{
    Continued = 1,
    BeginOfStream = 1 << 1,
    EndOfStream = 1 << 2,
}
