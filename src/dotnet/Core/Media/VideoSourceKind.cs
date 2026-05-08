namespace ActualChat.Media;

#pragma warning disable CA1028 // Enum Storage should be Int32 - byte is used for compact serialization

public enum VideoSourceKind : byte
{
    Camera,
    ScreenCast,
}
