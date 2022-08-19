using Stl.Reflection;

namespace ActualChat.Kvas;

public static class KvasExt
{
    public static ITextSerializer Serializer { get; set; } = SystemJsonSerializer.Default;

    // Get, Set, Remove w/ <T>

    public static async ValueTask<Option<T>> Get<T>(this IKvas kvas, string key, CancellationToken cancellationToken = default)
    {
        var data = await kvas.Get(key, cancellationToken).ConfigureAwait(false);
        return data is null ? Option<T>.None : Serializer.Read<T>(data);
    }

    public static Task Set<T>(this IKvas kvas, string key, T value)
    {
        var data = Serializer.Write(value);
        return kvas.Set(key, data);
    }

    public static Task Set<T>(this IKvas kvas, string key, Option<T> value)
        => value.IsSome(out var v)
            ? kvas.Set(key, v)
            : kvas.Remove(key);

    public static Task Remove(this IKvas kvas, string key)
        => kvas.Set(key, null);

    // WithXxx

    public static IKvas WithPrefix<T>(this IKvas kvas)
        => kvas.WithPrefix(typeof(T));

    public static IKvas WithPrefix(this IKvas kvas, Type type)
        => kvas.WithPrefix(type.GetName());

    public static IKvas WithPrefix(this IKvas kvas, string prefix)
    {
        if (prefix.IsNullOrEmpty())
            return kvas;
        if (kvas is PrefixedKvasWrapper kvp)
            return new PrefixedKvasWrapper(kvp.Upstream, $"{prefix}.{kvp.Prefix}");
        return new PrefixedKvasWrapper(kvas, prefix);
    }
}
