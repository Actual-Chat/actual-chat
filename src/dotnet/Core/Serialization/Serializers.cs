using ActualChat.Internal;
using ActualChat.Serialization.Internal;

namespace ActualChat.Serialization;

/// <summary>
/// Central serializer facade for ActualChat. Owns the lifecycle of the
/// Nerdbank.MessagePack-backed byte serializers (both the [Key]-honoring variant and the
/// keyless variant where [Key] attributes are ignored, producing name-keyed maps for any
/// type that lacks an explicit converter).
/// <para>
/// Module initializers register their stringlike identifier types and custom converters via
/// <see cref="Register(IReadOnlyList{Type}?,IReadOnlyList{MessagePackConverter}?)"/>; both
/// serializer variants pick them up. Application code should reference
/// <c>Serializers.MessagePack</c> / <c>Serializers.KeylessMessagePack</c> rather than the
/// framework-level statics.
/// </para>
/// </summary>
public static class Serializers
{
    private static readonly Lock Lock = new();
    private static readonly MessagePackSerializer BaseSerializer = NerdbankMessagePackByteSerializer.DefaultSerializer;
    private static readonly List<ITypeShapeProvider> ShapeProviders = new();
    private static readonly HashSet<Type> StringLikeTypes = new();
    private static readonly Dictionary<Type, MessagePackConverter> CustomConvertersByTarget = new();

    private static volatile SerializerState? _state;
    private static volatile SerializerState? _clientState;

    private static SerializerState State {
        get {
            var state = _state;
            if (state is not null)
                return state;

            lock (Lock)
                // ReSharper disable once NonAtomicCompoundOperator
                return _state ??= Build(isClientSide: false);
        }
    }

    public static SerializerState ClientSide {
        get {
            var clientState = _clientState;
            if (clientState is not null)
                return clientState;

            lock (Lock)
                // ReSharper disable once NonAtomicCompoundOperator
                return _clientState ??= Build(isClientSide: true);
        }
    }

    // Public properties

    public static ITextSerializer SystemJson { get; } = SystemJsonSerializer.Default;
    public static ITextSerializer SystemJsonTypeDecorating { get; } = SystemJsonSerializer.DefaultTypeDecorating;

    public static ITextSerializer NewtonsoftJson { get; } = NewtonsoftJsonSerializer.Default;
    public static ITextSerializer NewtonsoftJsonTypeDecorating { get; } = NewtonsoftJsonSerializer.DefaultTypeDecorating;

    public static IByteSerializer MemoryPack => MemoryPackByteSerializer.Default;
    public static IByteSerializer MemoryPackTypeDecorating => MemoryPackByteSerializer.DefaultTypeDecorating;

    public static IByteSerializer MessagePack => State.MessagePack;
    public static IByteSerializer MessagePackTypeDecorating => State.MessagePackTypeDecorating;

    public static IByteSerializer KeylessMessagePack => State.KeylessMessagePack;
    public static IByteSerializer KeylessMessagePackTypeDecorating => State.KeylessMessagePackTypeDecorating;

    // Registration API

    public static void RegisterShapeProvider(ITypeShapeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (Lock) {
            if (ShapeProviders.Contains(provider))
                return;
            ShapeProviders.Add(provider);
            _state = null;
            _clientState = null;
        }
    }

    public static void RegisterStringLikeTypes(IReadOnlyList<Type> stringLikeTypes)
    {
        ArgumentNullException.ThrowIfNull(stringLikeTypes);
        if (stringLikeTypes.Count == 0)
            return;
        lock (Lock) {
            var changed = false;
            foreach (var t in stringLikeTypes)
                if (StringLikeTypes.Add(t))
                    changed = true;
            if (changed) {
                _state = null;
                _clientState = null;
            }
        }
    }

    public static void RegisterConverters(IReadOnlyList<MessagePackConverter> converters)
    {
        ArgumentNullException.ThrowIfNull(converters);
        if (converters.Count == 0)
            return;
        lock (Lock) {
            var changed = false;
            foreach (var c in converters) {
                var target = GetConverterTarget(c);
                if (CustomConvertersByTarget.TryAdd(target, c))
                    changed = true;
            }
            if (changed) {
                _state = null;
                _clientState = null;
            }
        }
    }

    // Private methods

    private static SerializerState Build(bool isClientSide)
    {
        // Materialize per-type converters from the registries.
        var extras = new List<MessagePackConverter>(StringLikeTypes.Count + CustomConvertersByTarget.Count);
        foreach (var t in StringLikeTypes)
            extras.Add(CreateStringLikeConverter(t));
        extras.AddRange(CustomConvertersByTarget.Values);

        // Deduplicate by converter-target. Earlier registrations (Fusion's baseline) win, so
        // the framework's RpcHandshakeNerdbankConverter etc. are never displaced by a duplicate.
        var targetTypes = new HashSet<Type>();
        var keptConverters = new List<MessagePackConverter>();
        foreach (var c in BaseSerializer.Converters)
            if (targetTypes.Add(GetConverterTarget(c)))
                keptConverters.Add(c);
        foreach (var c in extras)
            if (targetTypes.Add(GetConverterTarget(c)))
                keptConverters.Add(c);

        ConverterTypeCollection.ConverterType[] extraOpenGenerics = [
            typeof(RangeNerdbankConverter<>),
            typeof(SetDiffNerdbankConverter<>),
            typeof(SetDiffNerdbankConverter<,>),
            typeof(ChangeNerdbankConverter<>),
            typeof(ChangeNerdbankConverter<,>),
            typeof(ExpiringNerdbankConverter<>),
            typeof(TrimmedNerdbankConverter<>),
            typeof(NullableNerdbankConverter<>),
            typeof(BoxNerdbankConverter<>),
            typeof(MaybeNerdbankConverter<>),
            typeof(ImmutableDictionaryNerdbankConverter<,>)];
        var openGenericSet = new HashSet<Type>();
        var keptOpenGenerics = new List<ConverterTypeCollection.ConverterType>();
        foreach (var ct in extraOpenGenerics)
            if (openGenericSet.Add(ct))
                keptOpenGenerics.Add(ct);
        foreach (var ct in BaseSerializer.ConverterTypes) {
            var def = ((Type)ct).GetGenericTypeDefinition();
            if (openGenericSet.Add(def))
                keptOpenGenerics.Add(ct);
        }

        var withKeys = BaseSerializer with {
            Converters = ConverterCollection.Create(keptConverters.ToArray()),
            ConverterTypes = ConverterTypeCollection.Create(keptOpenGenerics.ToArray()),
            // Match MessagePack-CSharp's lenient deserialization: types with `Rules = null!`-style
            // "populated later" init members should round-trip through Nerdbank just like they
            // did through MessagePack, rather than throwing on the non-nullable ctor parameter.
            DeserializeDefaultValues =
                DeserializeDefaultValuesPolicy.AllowNullValuesForNonNullableProperties
                | DeserializeDefaultValuesPolicy.AllowMissingValuesForRequiredProperties,
        };
        // Assume UTC when a DateTime value carries no Kind — matches the legacy MessagePack behavior.
        // Nerdbank otherwise throws on the first Moment/DateTime that hits wire with Kind = Unspecified.
        withKeys = withKeys.WithAssumedDateTimeKind(DateTimeKind.Utc);

        // Keyless variant: same converters (the explicit ones win for system RPC types so their
        // [Key]-array wire is preserved), but PolyType ignores [Key] for everything else, so app
        // types fall back to name-keyed maps — matching what JS objects naturally encode to.
        var withoutKeys = withKeys with { IgnoreKeyAttributes = true };

        var useReflection = RuntimeInfo.IsServer && !isClientSide;
        var typeShapeProvider = BuildTypeShapeProvider(useReflection);
        var messagePack = new NerdbankMessagePackByteSerializer(withKeys, typeShapeProvider);
        var messagePackTypeDecorating = new TypeDecoratingByteSerializer(messagePack);
        var keylessMessagePack = new NerdbankMessagePackByteSerializer(withoutKeys, typeShapeProvider);
        var keylessMessagePackTypeDecorating = new TypeDecoratingByteSerializer(keylessMessagePack);

        // Sync framework statics for any consumer that hasn't migrated to Serializers.* yet.
        // Only the default (reflection-aware on the server) state writes to the framework
        // statics — the codegen-only ClientState exists purely for opt-in test access.
        if (!isClientSide) {
            NerdbankMessagePackByteSerializer.DefaultSerializer = withKeys;
            NerdbankMessagePackByteSerializer.DefaultTypeShapeProvider = typeShapeProvider;
            NerdbankMessagePackByteSerializer.Default = messagePack;
            NerdbankMessagePackByteSerializer.DefaultTypeDecorating = messagePackTypeDecorating;
            ByteSerializer.Default = messagePack;
        }

        return new SerializerState(
            messagePack, messagePackTypeDecorating,
            keylessMessagePack, keylessMessagePackTypeDecorating);
    }

    private static ITypeShapeProvider BuildTypeShapeProvider(bool includeReflection)
    {
        // Per-assembly source-generated witnesses (CoreWitness, FrameworkWitness,
        // ApiContractsWitness, ApiWitness, BlazorUIAppWitness) are aggregated. Every
        // serializable type accessed on AOT/trimmed clients must be in some witness; every
        // open-generic converter must have a matching [TypeShapeExtension(target, …)]
        // declaration (see SerializerAssociatedTypes.cs).
        //
        // When <paramref name="includeReflection"/> is true, PolyType's
        // ReflectionTypeShapeProvider is appended LAST — codegen wins for known types,
        // reflection covers anything else. Server uses this; AOT clients turn it off via
        // Serializers.EnableReflection = false.
        if (ShapeProviders.Count == 0)
            throw new InvalidOperationException(
                "Serializers has no registered shape providers. Each module initializer must call "
                + "Serializers.RegisterShapeProvider(<itsWitness>.GeneratedTypeShapeProvider) before any "
                + "MessagePack/KeylessMessagePack serializer is used.");
        var providers = new List<ITypeShapeProvider>(ShapeProviders);
        if (includeReflection)
            providers.Add(PolyType.ReflectionProvider.ReflectionTypeShapeProvider.Default);
        return providers.Count == 1
            ? providers[0]
            : new PolyType.Utilities.AggregatingTypeShapeProvider(providers.ToArray());
    }

    private static Type GetConverterTarget(MessagePackConverter converter)
    {
        var t = converter.GetType();
        while (t is not null && t != typeof(object)) {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(MessagePackConverter<>))
                return t.GetGenericArguments()[0];
            t = t.BaseType;
        }
        return typeof(object);
    }

    [UnconditionalSuppressMessage("Trimming", "IL3050", Justification = "StringLikeNerdbankConverter<> is known and preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "StringLikeNerdbankConverter<> is known and preserved.")]
    private static MessagePackConverter CreateStringLikeConverter(Type type)
    {
        var converterType = typeof(StringLikeNerdbankConverter<>).MakeGenericType(type);
        return (MessagePackConverter)Activator.CreateInstance(converterType)!;
    }

    // Nested types

    public sealed record SerializerState(
        // ReSharper disable once MemberHidesStaticFromOuterClass
        IByteSerializer MessagePack,
        // ReSharper disable once MemberHidesStaticFromOuterClass
        IByteSerializer MessagePackTypeDecorating,
        // ReSharper disable once MemberHidesStaticFromOuterClass
        IByteSerializer KeylessMessagePack,
        // ReSharper disable once MemberHidesStaticFromOuterClass
        IByteSerializer KeylessMessagePackTypeDecorating);
}
