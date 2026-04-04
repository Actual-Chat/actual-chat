using System.Text.Json.Serialization.Metadata;

namespace ActualChat.Aot;

public static class AotJsonContexts
{
    private static readonly List<IJsonTypeInfoResolver> Sources = new();

    public static IJsonTypeInfoResolver[] All
    {
        get {
            if (field is not null) return field;
            lock (Sources)
                return field ??= Sources.ToArray();
        }
    }

    public static void Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T resolver)
        where T : IJsonTypeInfoResolver
    {
        CodeKeeper.Keep<T>();
        lock (Sources)
            if (Sources.All(x => typeof(T) != x.GetType()))
                Sources.Add(resolver);
    }
}
