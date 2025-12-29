namespace ActualChat.App.Maui.IosShareExt.UI;

public static class NSId
{
    public static NSId<TId> New<TId>(TId id) where TId : StringIdentifier, IStringIdentifier<TId>
        => new(id);
}

public class NSId<TId>(TId id) : NSObject
    where TId : StringIdentifier, IStringIdentifier<TId>
{
    public TId Id { get; } = id;

    public static implicit operator TId(NSId<TId> nsId) => nsId.Id;
    public static implicit operator NSId<TId>(TId id) => new(id);

    public override bool IsEqual(NSObject? obj)
        => obj is NSId<TId> other && Id.Equals(other.Id);

    public override nuint GetNativeHash()
        => (nuint)Id.GetHashCode();

    public override string ToString()
        => Id.ToString();

    public override int GetHashCode()
        => Id.GetHashCode();

    public override bool Equals(object? obj)
        => obj is NSId<TId> other && Id.Equals(other.Id);

    public override string DebugDescription => Id.Value;
    public override string Description => Id.Value;
}
