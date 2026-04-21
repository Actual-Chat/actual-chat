using System.ComponentModel;

namespace ActualChat.Internal;

// Used by JSON.NET to serialize dictionary keys of this type, and by generic
// string <-> T conversions in Blazor routing / configuration binding / etc.
public class StringLikeTypeConverter<T> : TypeConverter
    where T : IStringLike<T>
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string))
            return ((T?)value)?.Value;
        return base.ConvertTo(context, culture, value, destinationType)!;
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value)
    {
        if (value is string s)
            // ReSharper disable once HeapView.BoxingAllocation
            return s.IsNullOrEmpty() ? default(T) : T.Parse(s);
        return base.ConvertFrom(context!, culture!, value!)!;
    }
}
