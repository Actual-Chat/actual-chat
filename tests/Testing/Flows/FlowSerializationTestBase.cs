using ActualChat.Flows;

namespace ActualChat.Testing.Flows;

/// <summary>
/// Base class for verifying that every persisted property of a flow round-trips correctly
/// through MessagePack, plus MemoryPack for flows that still carry <c>[MemoryPackable]</c>.
/// Each derived test class provides a populated instance via <see cref="CreatePopulated"/>;
/// the base class snapshots all <c>[DataMember]</c> properties (including inherited ones)
/// before and after serialization and asserts that every value survives.
/// </summary>
public abstract class FlowSerializationTestBase<TFlow>(ITestOutputHelper @out) : TestBase(@out)
    where TFlow : Flow
{
    /// <summary>
    /// Construct a flow instance with every persisted property set to a non-default value.
    /// Inherited base-class properties must be set too — use object initializer or property
    /// assignment after construction.
    /// </summary>
    protected abstract TFlow CreatePopulated();

    [Fact]
    public void MemoryPack_RoundTrip()
    {
        // MemoryPack is a legacy read path, so only flows whose bytes are already in the DB
        // are [MemoryPackable]; the rest (test-only flows) have no formatter to exercise.
        // xUnit v2 has no dynamic skip, hence the early return rather than Assert.Skip.
        if (!SerializationCodeGen.IsMemoryPackable(typeof(TFlow))) {
            Out.WriteLine($"Skipped: {typeof(TFlow).Name} isn't [MemoryPackable].");
            return;
        }

        RoundTrip(Serializers.MemoryPack, "MemoryPack");
    }

    [Fact]
    public void MessagePack_RoundTrip()
        => RoundTrip(Serializers.MessagePack, "MessagePack");

    private void RoundTrip(IByteSerializer serializer, string name)
    {
        var original = CreatePopulated();
        AssertAllPersistedPropertiesNonDefault(original);
        var snapshot = TakeSnapshot(original);
        Out.WriteLine($"--- {name} round-trip of {typeof(TFlow).Name} ---");
        foreach (var (k, v) in snapshot)
            Out.WriteLine($"  {k} = {Format(v)}");

        using var buffer = serializer.Write(original);
        Out.WriteLine($"  bytes: {buffer.WrittenSpan.Length}");
        var deserialized = (TFlow)serializer.Read(buffer.WrittenMemory, typeof(TFlow), out _)!;
        var deserializedSnapshot = TakeSnapshot(deserialized);

        foreach (var (key, expectedValue) in snapshot) {
            var actualValue = deserializedSnapshot[key];
            actualValue.Should().BeEquivalentTo(expectedValue,
                $"property '{key}' on {typeof(TFlow).Name} should round-trip through {name}");
        }
    }

    private static Dictionary<string, object?> TakeSnapshot(TFlow flow)
    {
        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in GetSerializedProperties())
            snapshot[prop.Name] = prop.GetValue(flow);
        return snapshot;
    }

    private static void AssertAllPersistedPropertiesNonDefault(TFlow flow)
    {
        var unset = new List<string>();
        foreach (var prop in GetSerializedProperties()) {
            var value = prop.GetValue(flow);
            var defaultValue = prop.PropertyType.IsValueType
                ? Activator.CreateInstance(prop.PropertyType)
                : null;
            if (Equals(value, defaultValue))
                unset.Add($"{prop.Name} ({prop.PropertyType.Name})");
        }
        if (unset.Count > 0) {
            throw new InvalidOperationException(
                $"{nameof(CreatePopulated)}() must set every persisted property of {typeof(TFlow).Name} " +
                $"to a non-default value. Unset properties: {string.Join(", ", unset)}");
        }
    }

    private static PropertyInfo[] GetSerializedProperties()
        => typeof(TFlow)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(IsPersisted)
            .ToArray();

    private static bool IsPersisted(PropertyInfo p)
    {
        if (p.GetCustomAttribute<DataMemberAttribute>(inherit: true) == null)
            return false;
        if (p.GetCustomAttribute<IgnoreDataMemberAttribute>(inherit: true) != null)
            return false;
        return true;
    }

    private static string Format(object? value)
        => value switch {
            null => "<null>",
            string s => $"\"{s}\"",
            Array a => $"[{a.Length}]",
            _ => value.ToString() ?? "<null>",
        };
}
