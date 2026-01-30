using MemoryPack;

namespace ActualChat.Serialization;

#pragma warning disable CA1019 // Define accessors for attribute arguments

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class LegacyLanguageFormatterAttribute(bool isNullable)
    : MemoryPackCustomFormatterAttribute<IMemoryPackFormatter<Language?>, Language?>
{
    public override IMemoryPackFormatter<Language?> GetFormatter()
        => isNullable
            ? LegacyNullableLanguageFormatter.Default
            : LegacyLanguageFormatter.Default!;
}
