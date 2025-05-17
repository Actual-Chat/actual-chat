using MemoryPack;

namespace ActualChat.Serialization;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class LanguageBackwardCompatibleFormatterAttribute(bool isNullable)
    : MemoryPackCustomFormatterAttribute<IMemoryPackFormatter<Language?>, Language?>
{
    public override IMemoryPackFormatter<Language?> GetFormatter()
        => isNullable ? NullableLanguageBackwardCompatibleFormatter.Default : LanguageBackwardCompatibleFormatter.Default!;
}
