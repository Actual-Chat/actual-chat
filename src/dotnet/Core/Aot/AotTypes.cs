using System.Buffers;
using System.Text.Json.Serialization.Metadata;
using ActualChat.Internal;

namespace ActualChat.Aot;

/// <summary>
/// Provides lists of types for AOT testing, assists in AOT type registration.
/// </summary>
public static class AotTypes
{
    private static readonly List<IAotSource> Sources = new();
    private static readonly List<IJsonTypeInfoResolver> JsonTypeInfoResolverStore = new();

    public static IDictionary<Type, AotTypeKind> All {
        get {
            if (field is not null) return field;
            lock (Sources) {
                if (field is not null) return field;
                // Multiple sources legitimately register the same type (e.g. a [Serializable] used
                // by both the Api witness and a downstream module's witness). Last-wins on duplicate
                // keys: a downstream source's kind upgrade (Api -> Component) takes precedence over
                // an earlier base registration, which matches the AotHelper's source-ordering intent.
                var dict = new Dictionary<Type, AotTypeKind>();
                foreach (var src in Sources)
                    foreach (var (type, kind) in src.ListTypes())
                        dict[type] = kind;
                return field = dict;
            }
        }
    }

    public static void AddSource(IAotSource source)
    {
        if (CodeKeeper.AlwaysFalse)
            source.KeepTypes();
        lock (Sources) {
            // Deduplicate by source type
            var sourceType = source.GetType();
            if (Sources.Any(s => s.GetType() == sourceType))
                return;
            Sources.Add(source);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Retention-only — body is unreachable at runtime.")]
    [UnconditionalSuppressMessage("Trimming", "IL3050", Justification = "Retention-only — body is unreachable at runtime.")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void KeepSerializable<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TWitness>()
        where TWitness : IShapeable<T>
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        CodeKeeper.Keep<T>();
        CodeKeeper.Keep<NerdbankMessagePackByteSerializer<T>>();
        var serializer = new MessagePackSerializer();
        var buffer = new ArrayBufferWriter<byte>();
        serializer.Serialize<T, TWitness>(buffer, default);
        _ = serializer.Deserialize<T, TWitness>(ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>
    /// Forces ILC to preserve the closed <see cref="StringLikeNerdbankConverter{T}"/> for an
    /// <c>IStringLike&lt;T&gt;</c> identifier type. Required because
    /// <see cref="ActualChat.Serialization.Serializers.RegisterStringLikeTypes"/> instantiates
    /// the converter at runtime via <c>MakeGenericType</c> + <c>Activator.CreateInstance</c> —
    /// without an explicit Keep, NativeAOT throws "missing native code or metadata" the first
    /// time a payload of <c>T</c> hits the wire. Emitted per-type by App.AotHelper -g.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void KeepStringLikeConverter<T>()
        where T : IStringLike<T>
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        CodeKeeper.Keep<StringLikeNerdbankConverter<T>>();
        _ = new StringLikeNerdbankConverter<T>();
    }

    /// <summary>
    /// Forces ILC to preserve the closed <see cref="NerdbankMessagePackByteSerializer{T}"/>
    /// for a primitive / built-in type that flows through RPC signatures (<c>double</c>,
    /// <c>int</c>, <c>Guid</c>, …). Fusion's RPC plumbing builds the typed serializer at
    /// runtime via an <c>Expression</c>-compiled factory <c>Func&lt;MessagePackSerializer,
    /// ITypeShapeProvider, Type, NerdbankMessagePackByteSerializer&lt;T&gt;&gt;</c>, which
    /// requires the closed generic to exist in the AOT image. <see cref="KeepSerializable{T,TWitness}"/>
    /// already covers app types (they have a witness shape); this helper covers the BCL
    /// primitives PolyType handles intrinsically and that therefore aren't in any witness.
    /// Emitted per-type by App.AotHelper -g.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void KeepNerdbankSerializer<T>()
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        CodeKeeper.Keep<NerdbankMessagePackByteSerializer<T>>();
    }
}
