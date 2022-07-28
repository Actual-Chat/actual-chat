namespace ActualChat.Kvass;

public static class KvassExt
{
    public static ITextSerializer Serializer { get; set; } = SystemJsonSerializer.Default;

    public static async ValueTask<Option<T>> Get<T>(this IKvass kvass, string key, CancellationToken cancellationToken = default)
    {
        var data = await kvass.Get(key, cancellationToken).ConfigureAwait(false);
        return data is null ? Option<T>.None : Serializer.Read<T>(data);
    }

    public static ValueTask Set<T>(this IKvass kvass, string key, T value, CancellationToken cancellationToken = default)
    {
        var data = Serializer.Write(value);
        return kvass.Set(key, data, cancellationToken);
    }

    public static ValueTask Set<T>(this IKvass kvass, string key, Option<T> value, CancellationToken cancellationToken = default)
        => value.IsSome(out var v)
            ? kvass.Set(key, v, cancellationToken)
            : kvass.Remove(key, cancellationToken);

    public static ValueTask Remove(this IKvass kvass, string key, CancellationToken cancellationToken = default)
        => kvass.Set(key, null, cancellationToken);
}
