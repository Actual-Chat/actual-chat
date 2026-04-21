using ActualChat.Aot;
using ActualChat.Module;
using ActualLab.Serialization.Internal;

namespace ActualChat.Core.Server.IntegrationTests;

/// <summary>
/// Sanity check for NativeAOT / trimming readiness: every type registered in
/// <see cref="AotTypes.All"/> with <see cref="AotTypeKind.Serializable"/> must have a
/// usable formatter available WITHOUT dynamic IL emit.
///
/// Covers both serializers gated by build flags:
///   USE_MESSAGEPACK: <see cref="DefaultMessagePackResolver"/> must return a non-null
///     formatter for the type — i.e. it's in a SG-generated resolver, a [MessagePackFormatter]
///     attribute, or one of the static non-dynamic resolvers.
///   USE_MEMORYPACK: the type must implement <c>MemoryPack.IMemoryPackable&lt;T&gt;</c>, which
///     is the marker the MemoryPack SG emits for every [MemoryPackable] type.
///
/// Configures the resolver chain in CLIENT mode (isServer: false) so StandardResolver's
/// dynamic IL emit is NOT in the chain — a type that only resolves via dynamic emit will
/// fail here, matching the runtime behavior under NativeAOT/Wasm/Maui.
/// </summary>
public class AotFormatterPresenceTest
{
    static AotFormatterPresenceTest()
        => CoreSerializerAndRpcSetup.Configure(isServer: true);

#if USE_MESSAGEPACK
    [Fact]
    public void AllSerializableTypes_HaveNonDynamicMessagePackFormatter()
    {
        var resolver = DefaultMessagePackResolver.Instance;
        var getFormatter = typeof(IFormatterResolver)
            .GetMethod(nameof(IFormatterResolver.GetFormatter))!;

        var missing = new List<string>();
        foreach (var type in SerializableTypes()) {
            object? formatter;
            try {
                formatter = getFormatter.MakeGenericMethod(type).Invoke(resolver, null);
            }
            catch (Exception e) {
                missing.Add($"{type.FullName}: threw {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}");
                continue;
            }
            if (formatter is null)
                missing.Add($"{type.FullName}: no formatter");
        }

        missing.Count.Should().Be(0, "\n  " + string.Join("\n  ", missing) +
            "\n— every Serializable AotTypes entry must resolve to a non-null MessagePack formatter " +
            "without dynamic IL emit (client-mode resolver chain).");
    }
#endif

#if USE_MEMORYPACK
    [Fact]
    public void AllSerializableTypes_HaveMemoryPackFormatter()
    {
        // A type is MemoryPack-serializable iff:
        //   (a) it implements MemoryPack.IMemoryPackable<T> (SG-generated formatter), OR
        //   (b) a formatter was registered for it via MemoryPackFormatterProvider.Register<T>().
        // Case (b) applies to string-identifier types (AliasId, UserId, ChatId, ...) which
        // declare [MemoryPackable(GenerateType.NoGenerate)] and register hand-written
        // StringIdentifierMemoryPackFormatter<T> from ApiModuleInitializer.
        var isRegistered = typeof(MemoryPack.MemoryPackFormatterProvider)
            .GetMethod(nameof(MemoryPack.MemoryPackFormatterProvider.IsRegistered))!;

        var missing = new List<string>();
        foreach (var type in SerializableTypes()) {
            var hasInterface = type.GetInterfaces().Any(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition().FullName == "MemoryPack.IMemoryPackable`1");
            var registered = (bool)isRegistered.MakeGenericMethod(type).Invoke(null, null)!;
            if (!hasInterface && !registered)
                missing.Add(type.FullName ?? type.Name);
        }

        missing.Count.Should().Be(0, "\n  " + string.Join("\n  ", missing) +
            "\n— every Serializable AotTypes entry must be MemoryPack-serializable.");
    }
#endif

    private static IEnumerable<Type> SerializableTypes()
        => AotTypes.All
            .Where(kv => kv.Value == AotTypeKind.Serializable)
            .Select(kv => kv.Key)
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });
}
