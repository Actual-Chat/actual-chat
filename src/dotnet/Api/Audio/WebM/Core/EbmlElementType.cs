namespace ActualChat.Audio.WebM;

/// <summary>
/// Specifies the data type of an EBML element.
/// </summary>
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
