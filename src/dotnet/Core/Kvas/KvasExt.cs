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

    public static ValueTask<T?> GetValue<T>(this IKvas kvas, string key, CancellationToken cancellationToken = default)
        => GetValue<T>(kvas, key, default, cancellationToken);

    public static async ValueTask<T?> GetValue<T>(this IKvas kvas, string key, T? @default, CancellationToken cancellationToken = default)
    {
        var (hasValue, value) = await Get<T>(kvas, key, cancellationToken).ConfigureAwait(false);
        return hasValue ? value ?? @default : @default;
    }

    public static Task Set<T>(this IKvas kvas, string key, T value, CancellationToken cancellationToken = default)
    {
        var data = value == null ? null : Serializer.Write(value);
        return kvas.Set(key, data, cancellationToken);
    }

    public static Task Set<T>(this IKvas kvas, string key, Option<T> value, CancellationToken cancellationToken = default)
        => value.IsSome(out var v)
            ? kvas.Set(key, v, cancellationToken)
            : kvas.Remove(key, cancellationToken);

    public static Task Remove(this IKvas kvas, string key, CancellationToken cancellationToken = default)
        => kvas.Set(key, null, cancellationToken);

    // WithXxx

    public static IKvas WithPrefix<T>(this IKvas kvas)
        => kvas.WithPrefix(typeof(T));

    public static IKvas WithPrefix(this IKvas kvas, Type type)
        => kvas.WithPrefix(type.GetName());

    public static IKvas WithPrefix(this IKvas kvas, string prefix)
    {
        if (prefix.IsNullOrEmpty())
            return kvas;
        if (kvas is PrefixedKvas kvp)
            return new PrefixedKvas(kvp.Upstream, $"{prefix}.{kvp.Prefix}");
        return new PrefixedKvas(kvas, prefix);
    }
}
