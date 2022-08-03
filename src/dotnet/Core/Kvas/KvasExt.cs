namespace ActualChat.Kvas;

public static class KvasExt
{
    public static ITextSerializer Serializer { get; set; } = SystemJsonSerializer.Default;

    // Get, Set, Remove w/ <T>

    public static async ValueTask<Option<T>> Get<T>(this IKvas kvas, Symbol key, CancellationToken cancellationToken = default)
    {
        var data = await kvas.Get(key, cancellationToken).ConfigureAwait(false);
        return data is null ? Option<T>.None : Serializer.Read<T>(data);
    }

    public static void Set<T>(this IKvas kvas, Symbol key, T value)
    {
        var data = Serializer.Write(value);
        kvas.Set(key, data);
    }

    public static void Set<T>(this IKvas kvas, Symbol key, Option<T> value)
    {
        if (value.IsSome(out var v))
            kvas.Set(key, v);
        else
            kvas.Remove(key);
    }

    public static void Remove(this IKvas kvas, Symbol key)
        => kvas.Set(key, null);

    // WithXxx

    public static IKvas WithPrefix(this IKvas kvas, string prefix)
    {
        if (prefix.IsNullOrEmpty())
            return kvas;
        if (kvas is KvasForPrefix kvp)
            return new KvasForPrefix($"{prefix}.{kvp.Prefix}", kvp.Upstream);
        return new KvasForPrefix(prefix, kvas);
    }

    public static IKvas<TScope> WithScope<TScope>(this IKvas kvas)
        => new KvasForScope<TScope>(kvas);
}
