using ActualChat.Internal;
using MemoryPack;

namespace ActualChat.Serialization;

#pragma warning disable CA2255

public static class ApiSerializerAndRpcSetup
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        // Roulette identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ChatRouletteId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Country>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Interest>());
        // Emoji identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Emoji>());
        // Everything else
    }
}
