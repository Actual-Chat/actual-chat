namespace ActualChat.Audio.WebM;

#pragma warning disable CA1720 // Identifier 'Float' contains type name

public enum EbmlElementType
{
    SignedInteger,
    UnsignedInteger,
    Float,
    AsciiString,
    Utf8String,
    Date,
    Binary,
    MasterElement,
    None = -1
}
