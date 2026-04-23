namespace ActualChat.Contacts;

/// <summary>
/// Defines the category of contacts to query.
/// </summary>
public enum ContactSubsetKind { All, Chats, Place }

/// <summary>
/// Specifies a subset of contacts to retrieve.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class ContactSubset
{
    [property: DataMember, MemoryPackOrder(0)] public ContactSubsetKind Kind { get; }
    [property: DataMember, MemoryPackOrder(1)] public PlaceId? PlaceId { get; }

    // [ConstructorShape] + internal visibility lets PolyType's reflection provider instantiate
    // this type through the primary ctor. The legacy MessagePack-CSharp AllowPrivate=true hint
    // doesn't exist in Nerdbank's shape provider, and an explicit annotation is cheaper than
    // surfacing the factory methods as ctors.
    [ConstructorShape]
    internal ContactSubset(ContactSubsetKind kind, PlaceId? placeId = null)
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
