namespace ActualChat.Testing;

public static class SerializationCodeGen
{
    public static void ValidateType<T>()
    {
        ValidateMemoryPack<T>();
        ValidateMessagePack<T>();
    }

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
#if USE_MESSAGEPACK
        attrAssembly.Should().Be("MessagePack.Annotations",
            $"real MessagePack attributes should be used for {typeof(T).Name} when enabled");
#else
        attrAssembly.Should().Be("ActualChat.Core",
            $"shim attributes should be used for {typeof(T).Name} when MessagePack is disabled");
#endif
    }
}
