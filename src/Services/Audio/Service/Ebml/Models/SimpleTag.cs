namespace ActualChat.Audio.Ebml.Models
{
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
    }
}