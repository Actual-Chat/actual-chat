using System.ComponentModel;

namespace ActualChat.Internal;

// Used by JSON.NET to serialize dictionary keys of this type
public class StringIdentifierTypeConverter<TId> : TypeConverter
    where TId : StringIdentifier, IStringIdentifier<TId>
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string))
            return ((TId?)value)?.Value;
        return base.ConvertTo(context, culture, value, destinationType)!;
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value)
    {
        if (value is string s)
            return s.IsNullOrEmpty() ? null : TId.Parse(s);
        return base.ConvertFrom(context!, culture!, value!)!;
    }
}
