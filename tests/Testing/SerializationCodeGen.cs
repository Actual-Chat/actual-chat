namespace ActualChat.Testing;

/// <summary>
/// Post-MessagePack migration: the only remaining codegen guard is MemoryPack. The earlier
/// ValidateMessagePack check was removed along with MessagePack itself — serializable types
/// are now shape-driven via PolyType + Nerdbank and no longer carry [MessagePackObject].
/// </summary>
public static class SerializationCodeGen
{
    public static void ValidateType<T>()
    {
        var hasMemoryPackable = typeof(T).GetInterfaces()
            .Any(i => i.IsGenericType
                && i.GetGenericTypeDefinition().FullName == "MemoryPack.IMemoryPackable`1");
#if USE_MEMORYPACK
        hasMemoryPackable.Should().BeTrue(
            $"{typeof(T).Name} should implement IMemoryPackable<T> (generator active)");
#else
        hasMemoryPackable.Should().BeFalse(
            $"{typeof(T).Name} should NOT implement IMemoryPackable<T> (generator disabled)");
#endif
    }
}
