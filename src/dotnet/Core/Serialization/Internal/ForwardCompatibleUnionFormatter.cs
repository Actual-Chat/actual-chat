using System.Collections.Frozen;
using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

// Deliberately not a try/catch around the inner formatter: catching
// MessagePackSerializationException would swallow genuine corruption too and turn it into a
// silently missing value. Only a tag this build has no member for is tolerated.

/// <summary>
/// Reads a <c>[Union]</c> value whose tag this build may not know. A known tag is handed to the
/// standard union formatter untouched; an unknown one becomes
/// <see cref="IForwardCompatibleUnion{TSelf}.NewUnsupported"/>'s placeholder.
/// </summary>
public sealed class ForwardCompatibleUnionFormatter<TBase> : IMessagePackFormatter<TBase?>
    where TBase : class, IForwardCompatibleUnion<TBase>
{
    private static readonly FrozenSet<int> KnownTags = typeof(TBase)
        .GetCustomAttributes<UnionAttribute>()
        .Select(x => x.Key)
        .ToFrozenSet();
    private static readonly ConcurrentDictionary<IFormatterResolver, IMessagePackFormatter<TBase?>> InnerFormatters = new();

    public void Serialize(ref MessagePackWriter writer, TBase? value, MessagePackSerializerOptions options)
        => GetInnerFormatter(options).Serialize(ref writer, value, options);

    public TBase? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var innerFormatter = GetInnerFormatter(options);
        // A copy of the reader is a free bookmark: MessagePackReader is a struct over an
        // in-memory sequence, so peeking costs nothing and leaves the real reader untouched.
        var peek = reader;
        if (!TryReadUnknownTag(ref peek, out var tag))
            return innerFormatter.Deserialize(ref reader, options);

        var result = TBase.NewUnsupported(tag, ref peek, options);
        reader.Skip();
        return result;
    }

    // Private methods

    private static bool TryReadUnknownTag(ref MessagePackReader peek, out int tag)
    {
        tag = 0;
        if (peek.NextMessagePackType != MessagePackType.Array)
            return false; // Nil, or not an envelope at all - the inner formatter decides what that means
        if (peek.ReadArrayHeader() != 2)
            return false;
        if (peek.NextMessagePackType != MessagePackType.Integer)
            return false;

        tag = peek.ReadInt32();
        return !KnownTags.Contains(tag);
    }

    private static IMessagePackFormatter<TBase?> GetInnerFormatter(MessagePackSerializerOptions options)
        => InnerFormatters.GetOrAdd(options.Resolver, static resolver => {
            // Resolving through the app resolver would find this formatter again and recurse,
            // so the union formatter comes from the chain the app resolver is built on.
            var chain = ReferenceEquals(resolver, AppMessagePackKeylessResolver.Instance)
                ? AppMessagePackKeylessResolver.Settings.Resolvers
                : AppMessagePackResolverSettings.StandardResolvers;
            foreach (var r in chain)
                if (r.GetFormatter<TBase?>() is { } formatter)
                    return formatter;

            throw StandardError.Internal($"No union formatter for {typeof(TBase).GetName()}.");
        });
}
