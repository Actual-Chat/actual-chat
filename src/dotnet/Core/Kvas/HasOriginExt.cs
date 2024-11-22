namespace ActualChat.Kvas;

public static class HasOriginExt
{
    private static readonly ConcurrentDictionary<Type, Action<object, string>> OriginSetters = new();

    [UnconditionalSuppressMessage("Trimming", "IL2111:LambdaParameter", Justification = "Target is used in the lambda.")]
    public static void SetOrigin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]T>(this T target, string origin)
    where T: class, IHasOrigin
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
