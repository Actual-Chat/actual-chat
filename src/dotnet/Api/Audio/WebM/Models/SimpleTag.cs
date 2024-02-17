using ActualChat.Spans;

namespace ActualChat.Audio.WebM.Models;

public sealed class SimpleTag
{
    [MatroskaElementDescriptor(MatroskaSpecification.TagName)]
    public string? TagName { get; set; }

    [MatroskaElementDescriptor(MatroskaSpecification.TagLanguage)]
    public string? TagLanguage { get; set; }

    [MatroskaElementDescriptor(MatroskaSpecification.TagDefault)]
    public ulong? TagDefault { get; set; }

    [MatroskaElementDescriptor(MatroskaSpecification.TagString)]
    public string? TagString { get; set; }

    [MatroskaElementDescriptor(MatroskaSpecification.TagBinary)]
    public byte[]? TagBinary { get; set; }

    public ulong GetSize()
    {
        var size = 0UL;
        size += EbmlHelper.GetElementSize(MatroskaSpecification.TagName, TagName, false);
        size += EbmlHelper.GetElementSize(MatroskaSpecification.TagLanguage, TagLanguage, true);
        size += EbmlHelper.GetElementSize(MatroskaSpecification.TagDefault, TagDefault);
        size += EbmlHelper.GetElementSize(MatroskaSpecification.TagString, TagString, false);
        size += EbmlHelper.GetElementSize(MatroskaSpecification.TagBinary, TagBinary);
        return size;
    }

    public bool Write(ref SpanWriter writer)
    {
        if (!EbmlHelper.WriteEbmlMasterElement(MatroskaSpecification.SimpleTag, GetSize(), ref writer))
            return false;

        EbmlHelper.WriteEbmlElement(MatroskaSpecification.TagName, TagName, false, ref writer);
        EbmlHelper.WriteEbmlElement(MatroskaSpecification.TagLanguage, TagLanguage, true, ref writer);
        EbmlHelper.WriteEbmlElement(MatroskaSpecification.TagDefault, TagDefault, ref writer);
        EbmlHelper.WriteEbmlElement(MatroskaSpecification.TagString, TagString, false, ref writer);
        EbmlHelper.WriteEbmlElement(MatroskaSpecification.TagBinary, TagBinary, ref writer);

        return true;
    }
}
