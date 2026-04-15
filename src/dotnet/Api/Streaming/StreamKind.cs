namespace ActualChat.Streaming;

#pragma warning disable CA1028 // Enum Storage should be Int32 - byte is used for compact serialization

public enum StreamKind : byte
{
    Webcam = 0,
    Screencast = 1,
}
