namespace ActualChat.Contacts;

/// <summary>
/// Defines the category of contacts to query.
/// </summary>
public enum ContactSubsetKind { All, Chats, Place }

/// <summary>
/// Specifies a subset of contacts to retrieve.
/// </summary>
[DataContract, MessagePackObject(true, AllowPrivate = true)]
public partial class ContactSubset : IEquatable<ContactSubset>
{
    [property: DataMember] public ContactSubsetKind Kind { get; }
    [property: DataMember] public PlaceId? PlaceId { get; }

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

    // Equality

    public bool Equals(ContactSubset? other)
        => other is not null && Kind == other.Kind && PlaceId == other.PlaceId;
    public override bool Equals(object? obj) => Equals(obj as ContactSubset);
    public override int GetHashCode() => HashCode.Combine(Kind, PlaceId);
}
