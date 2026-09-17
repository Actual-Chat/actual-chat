using ActualLab.Versioning;

namespace ActualChat.Media;

// A generated image offered for a piece of content, pending acceptance. At most one per key;
// generating a new one replaces it. The key is opaque here - only the caller parses it.

[DataContract, MessagePackObject]
public sealed partial record ImageSuggestion(
    [property: DataMember, Key(0)] string Key,
    [property: DataMember, Key(1)] MediaId MediaId,
    [property: DataMember, Key(2)] string ImageDescription,
    [property: DataMember, Key(3)] Moment CreatedAt,
    [property: DataMember, Key(4)] long Version = 0
) : IHasVersion<long>
{
    // Populated on read only, like Chat.Picture
    [DataMember, Key(5)]
    public Media? Media { get; init; }
}
