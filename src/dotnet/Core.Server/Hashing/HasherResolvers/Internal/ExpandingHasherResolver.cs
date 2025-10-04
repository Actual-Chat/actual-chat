namespace ActualChat.Hashing.Internal;

#pragma warning disable IL2111, IL2060, IL2067

public sealed class ExpandingHasherResolver(
    HasherResolver baseResolver,
    Func<ExpandingHasherResolver, Type, Delegate?>? expander = null
    ) : HasherResolver
{
    public HasherResolver BaseResolver { get; } = baseResolver;
    public Func<ExpandingHasherResolver, Type, Delegate?> Expander { get; } = expander ?? DefaultExpander;

    public override Delegate? Get(Type type)
        => Expander.Invoke(this, type);

    public static Delegate? DefaultExpander(ExpandingHasherResolver self, Type type)
    {
        var hasher = self.BaseResolver.Get(type);
        if (hasher is not null)
            return hasher;

        if (type is { IsValueType: true, IsGenericType: true }) {
            var gDef = type.GetGenericTypeDefinition();
            var gArgs = type.GetGenericArguments();
            var h0 = gArgs.Length >= 1 ? self.Get(gArgs[0]) : null;
            var h1 = gArgs.Length >= 2 ? self.Get(gArgs[1]) : null;
            var h2 = gArgs.Length >= 3 ? self.Get(gArgs[2]) : null;
            if (gDef == typeof(Nullable<>) && h0 is not null)
                return (Delegate?)Hashers.ForNullableMethod.MakeGenericMethod(gArgs[0]).Invoke(null, [h0])!;
            if (gDef == typeof(ValueTuple<>) && h0 is not null)
                return (Delegate?)Hashers.ForValueTuple1Method.MakeGenericMethod(gArgs[0]).Invoke(null, [h0])!;
            if (gDef == typeof(ValueTuple<,>) && h0 is not null && h1 is not null)
                return (Delegate?)Hashers.ForValueTuple2Method.MakeGenericMethod(gArgs[0], gArgs[1]).Invoke(null, [h0, h1])!;
            if (gDef == typeof(ValueTuple<,,>) && h0 is not null && h1 is not null && h2 is not null)
                return (Delegate?)Hashers.ForValueTuple3Method.MakeGenericMethod(gArgs[0], gArgs[1], gArgs[2]).Invoke(null, [h0, h1, h2])!;
        }

        foreach (var baseType in type.GetAllBaseTypes(false, true)) {
            hasher = self.Get(baseType);
            if (hasher is not null)
                return hasher;
        }

        return null;
    }
}
