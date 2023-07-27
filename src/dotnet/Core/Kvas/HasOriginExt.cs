using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Kvas;

public static class HasOriginExt
{
    private static readonly ConcurrentDictionary<Type, Action<object, string>> OriginSetters = new();

    public static void SetOrigin(this IHasOrigin target, string origin)
    {
        if (origin.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (OrdinalEquals(target.Origin, origin))
            return;

        var setter = OriginSetters.GetOrAdd(
            target.GetType(),
            static ([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] type) => {
                var property = type.GetProperty(nameof(IHasOrigin.Origin));
                if (property?.GetSetMethod() == null)
                    throw StandardError.Internal($"Type '{type.GetName()}' should writeable Origin property.");

                return property.GetSetter<string>();
            });
        setter.Invoke(target, origin);
    }
}
