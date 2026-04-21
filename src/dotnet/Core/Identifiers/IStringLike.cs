namespace ActualChat;

#pragma warning disable CA1000, CA2252

/// <summary>
/// A non-generic shape implemented by every string-like value type — one that can
/// round-trip through a plain string via its <see cref="Value"/> property and a
/// static <c>Parse</c> method (see <see cref="IStringLike{TSelf}"/>).
/// Used by the shared StringLikeXxxFormatter/Converter classes to serialize such
/// types as plain strings across JSON / MessagePack / MemoryPack / TypeConverter.
/// </summary>
public interface IStringLike
{
    string Value { get; }
}

/// <summary>
/// Generic companion to <see cref="IStringLike"/> — exposes the static factory
/// that formatters use to reconstruct the value from its serialized string form.
/// </summary>
// ReSharper disable once TypeParameterCanBeVariant
public interface IStringLike<TSelf> : IStringLike
    where TSelf : IStringLike<TSelf>
{
    static abstract TSelf Parse(string? s);
}
