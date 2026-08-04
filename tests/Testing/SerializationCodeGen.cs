namespace ActualChat.Testing;

public static class SerializationCodeGen
{
    public static void ValidateType<T>()
        => ValidateMessagePack<T>();

    // For the types still read back from MemoryPack bytes: legacy flow state
    // (Core.Server/Flows/FlowData.cs) and legacy server KVAS values (Core/Kvas/KvasSerializer.cs).
    public static void ValidateMemoryPackType<T>()
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
        attrAssembly.Should().Be("MessagePack.Annotations",
            $"real MessagePack attributes should be used for {typeof(T).Name}");
    }
}
