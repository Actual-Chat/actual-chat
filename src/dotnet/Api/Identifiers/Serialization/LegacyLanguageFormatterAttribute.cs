namespace ActualChat.Serialization;

/// <summary>
/// Attribute to apply legacy Language serialization format to a property.
/// </summary>
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
