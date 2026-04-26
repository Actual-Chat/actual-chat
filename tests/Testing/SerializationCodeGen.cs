namespace ActualChat.Testing;

public static class SerializationCodeGen
{
    public static void ValidateType<T>()
    {
        ValidateMemoryPack<T>();
        ValidateMessagePack<T>();
    }

    // For modern union-shaped types (e.g. ChatEntry, Invite) that intentionally
    // ship MessagePack-only on the wire — MemoryPack is reserved for the legacy
    // wire-frozen counterparts.
    public static void ValidateMessagePackOnlyType<T>()
        => ValidateMessagePack<T>();

    // Private methods

    private static void ValidateMemoryPack<T>()
    {
        var hasInterface = typeof(T).GetInterfaces()
            .Any(i => i.IsGenericType
                && i.GetGenericTypeDefinition().FullName == "MemoryPack.IMemoryPackable`1");
#if USE_MEMORYPACK
        hasInterface.Should().BeTrue(
            $"{typeof(T).Name} should implement IMemoryPackable<T> (generator active)");
#else
        hasInterface.Should().BeFalse(
            $"{typeof(T).Name} should NOT implement IMemoryPackable<T> (generator disabled)");
#endif
    }

    private static void ValidateMessagePack<T>()
    {
        var attr = typeof(T).GetCustomAttributes(false)
            .FirstOrDefault(a => a.GetType().Namespace == "MessagePack");
        attr.Should().NotBeNull(
            $"{typeof(T).Name} should have a MessagePack attribute");
        var attrAssembly = attr!.GetType().Assembly.GetName().Name;
        attrAssembly.Should().Be("MessagePack.Annotations",
            $"real MessagePack attributes should be used for {typeof(T).Name}");
    }
}
