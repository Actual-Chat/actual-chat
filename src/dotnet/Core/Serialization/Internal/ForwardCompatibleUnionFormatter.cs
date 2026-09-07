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

    // False when [Union] attributes aren't readable at runtime, which is possible under Native
    // AOT: the source-generated union formatter bakes the tags into code and never reads the
    // attributes, so the trimmer is free to drop them and leave this the only reader. Losing them
    // has to degrade to today's behaviour rather than to the opposite of it - an empty set would
    // make *every* tag unknown, turning every message in every chat into a placeholder. Silently,
    // and on mobile only.
    public static bool IsTolerant => KnownTags.Count != 0;

    static ForwardCompatibleUnionFormatter()
    {
        if (!IsTolerant)
            StaticLog.For<ForwardCompatibleUnionFormatter<TBase>>().LogError(
                "No [Union] attributes on {Type} at runtime - tolerance is off for it. "
                + "An unknown member will fail to deserialize, as it did before this formatter.",
                typeof(TBase).GetName());
    }

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
        if (!IsTolerant)
            return false;
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
