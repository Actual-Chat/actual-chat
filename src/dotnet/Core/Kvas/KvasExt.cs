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
        => new KvasForPrefix(prefix, kvas);
}
