using MemoryPack;

namespace ActualChat.Contacts;

public enum ContactSubsetKind { All, Chats, Place }

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ContactSubset
{
    [property: DataMember, MemoryPackOrder(0)] public ContactSubsetKind Kind { get; }
    [property: DataMember, MemoryPackOrder(1)] public PlaceId? PlaceId { get; }

    private ContactSubset(ContactSubsetKind kind, PlaceId? placeId = null)
    {
        Kind = kind;
        PlaceId = placeId;
    }

    public static ContactSubset All() => new (ContactSubsetKind.All);
    public static ContactSubset Chats() => new (ContactSubsetKind.Chats);
    public static ContactSubset Place(PlaceId placeId) => new (ContactSubsetKind.Place, placeId);

    public bool IsAll() => Kind == ContactSubsetKind.All;
    public bool IsChats() => Kind == ContactSubsetKind.Chats;

    public bool IsPlace([NotNullWhen(true)] out PlaceId? placeId)
    {
        placeId = PlaceId;
        return Kind == ContactSubsetKind.Place;
    }
}
