namespace ActualChat.App.Maui.IosShareExt.UI;

public class NSHasId<T, TId>(T value) : NSObject
    where T : class?, IHasId<TId>?
    where TId : StringIdentifier, IStringIdentifier<TId>
{
    public T Value { get; } = value;
    public TId? Id => Value?.Id;

    public static implicit operator T(NSHasId<T, TId> wrapper) => wrapper.Value;
    public static implicit operator NSHasId<T, TId>(T id) => new(id);

    public override bool IsEqual(NSObject? obj)
        => obj is NSHasId<T, TId> other && Equals(Id, other.Id);

    public override nuint GetNativeHash()
        => (nuint)(Id?.GetHashCode() ?? 0);

    public override string ToString()
        => Id?.ToString() ?? "";

    public override int GetHashCode()
        => Id?.GetHashCode() ?? 0;

    public override bool Equals(object? obj)
        => obj is NSHasId<T, TId> other && Equals(Id, other.Id);

    public override string DebugDescription => Id?.Value ?? "";
    public override string Description => Id?.Value ?? "";
}
