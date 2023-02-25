using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Kvas;

public static class HasOriginExt
{
    private static readonly ConcurrentDictionary<Type, Action<object, string>> OriginSetters = new();

    public static T WithOrigin<T>(this T source, string origin)
        where T: IHasOrigin
    {
        if (OrdinalEquals(source.Origin, origin))
            return source;

        return MemberwiseCloner.Invoke(source).SetOrigin(origin);
    }

    public static T SetOrigin<T>(this T target, string origin)
        where T: IHasOrigin
    {
        if (origin.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (OrdinalEquals(target.Origin, origin))
            return target;

        var setter = OriginSetters.GetOrAdd(
            typeof(T),
            static ([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] type) => {
                var property = type.GetProperty(nameof(IHasOrigin.Origin));
                if (property?.GetSetMethod() == null)
                    throw StandardError.Internal($"Type '{type.GetName()}' should writeable Origin property.");

                return property.GetSetter<string>();
            });
        setter.Invoke(target, origin);
        return target;
    }
}
