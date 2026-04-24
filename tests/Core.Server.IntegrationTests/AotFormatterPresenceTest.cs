using ActualChat.Aot;
using ActualLab.Serialization.Internal;

namespace ActualChat.Core.Server.IntegrationTests;

/// <summary>
/// Sanity check for NativeAOT / trimming readiness: every type registered in
/// <see cref="AotTypes.All"/> with <see cref="AotTypeKind.Serializable"/> must have a
/// usable formatter available WITHOUT dynamic IL emit.
///
/// Covers both serializers:
///   MessagePack: <see cref="DefaultMessagePackResolver"/> must return a non-null
///     formatter for the type — i.e. it's in a SG-generated resolver, a [MessagePackFormatter]
///     attribute, or one of the static non-dynamic resolvers.
///   USE_MEMORYPACK: the type must implement <c>MemoryPack.IMemoryPackable&lt;T&gt;</c>, which
///     is the marker the MemoryPack SG emits for every [MemoryPackable] type.
///
/// Configures the resolver chain in CLIENT mode (isServer: false) so StandardResolver's
/// dynamic IL emit is NOT in the chain — a type that only resolves via dynamic emit will
/// fail here, matching the runtime behavior under NativeAOT/Wasm/Maui.
/// </summary>
public class AotFormatterPresenceTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void AllSerializableTypes_HaveNonDynamicMessagePackFormatter()
    {
        var resolver = DefaultMessagePackResolver.Instance;
        var getFormatter = typeof(IFormatterResolver)
            .GetMethod(nameof(IFormatterResolver.GetFormatter))!;

        var supported = 0;
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
            else
                supported++;
        }

        Out.WriteLine($"MessagePack-serializable types: {supported} (missing: {missing.Count})");

        missing.Count.Should().Be(0, "\n  " + string.Join("\n  ", missing) +
            "\n— every Serializable AotTypes entry must resolve to a non-null MessagePack formatter " +
            "without dynamic IL emit (client-mode resolver chain).");
    }

#if USE_MEMORYPACK
    [Fact]
    public void AllMemoryPackableTypes_HaveMemoryPackFormatter()
    {
        // A type is MemoryPack-serializable iff:
        //   (a) it implements MemoryPack.IMemoryPackable<T> (SG-generated formatter), OR
        //   (b) a formatter was registered for it via MemoryPackFormatterProvider.Register<T>().
        // Case (b) applies to string-identifier types (AliasId, UserId, ChatId, ...) which
        // declare [MemoryPackable(GenerateType.NoGenerate)] and register hand-written
        // StringIdentifierMemoryPackFormatter<T> from ApiModuleInitializer.
        // Only types that opt in to MemoryPack via [MemoryPackable] are checked here —
        // the Serializable set also contains types that are MessagePack-only (e.g. marked
        // with [MessagePackFormatter] or [Union] without [MemoryPackable]).
        var isRegistered = typeof(MemoryPackFormatterProvider)
            .GetMethod(nameof(MemoryPackFormatterProvider.IsRegistered))!;

        var supported = 0;
        var missing = new List<string>();
        foreach (var type in SerializableTypes()) {
            var hasMemoryPackableAttr = type.CustomAttributes.Any(a =>
                a.AttributeType.FullName == "MemoryPack.MemoryPackableAttribute");
            if (!hasMemoryPackableAttr)
                continue;
            var hasInterface = type.GetInterfaces().Any(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition().FullName == "MemoryPack.IMemoryPackable`1");
            var registered = (bool)isRegistered.MakeGenericMethod(type).Invoke(null, null)!;
            if (hasInterface || registered)
                supported++;
            else
                missing.Add(type.FullName ?? type.Name);
        }

        Out.WriteLine($"MemoryPack-serializable types: {supported} (missing: {missing.Count})");

        missing.Count.Should().Be(0, "\n  " + string.Join("\n  ", missing) +
            "\n— every [MemoryPackable] AotTypes entry must be MemoryPack-serializable.");
    }
#endif

    private static IEnumerable<Type> SerializableTypes()
        => AotTypes.All
            .Where(kv => kv.Value == AotTypeKind.Serializable)
            .Select(kv => kv.Key)
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });
}
