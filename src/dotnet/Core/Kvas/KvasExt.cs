using CommunityToolkit.HighPerformance.Buffers;

namespace ActualChat.Kvas;

public static class KvasExt
{
    public static KvasSerializer Serializer { get; set; } = KvasSerializer.Default;
    public static readonly string MigratedKey = "@Migrated";

    // AccessorFor

    public static KvasAccessor<T> AccessorFor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this IKvas kvas) where T : class, IHasKvasKey<T>, new()
        => new (kvas, T.KvasKey);

    // Get, Set, Remove w/ <T>

    public static async ValueTask<T?> Get<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvas kvas, string key, CancellationToken cancellationToken = default)
        where T : class?
  {
        var data = await kvas.Get(key, cancellationToken).ConfigureAwait(false);
        return data is null ? null : (T)Serializer.Read(data, typeof(T), out _)!;
  }

    public static ValueTask<T?> Get<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvas kvas, CancellationToken cancellationToken = default)
        where T : class, IHasKvasKey<T>
        => kvas.Get<T>(T.KvasKey, cancellationToken);

    [UnconditionalSuppressMessage("Tasks", "MA0100", Justification = "Don't need to wait for Set completion to dispose buffer writer.")]
    public static Task Set<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvas kvas, string key, T value, CancellationToken cancellationToken = default)
        where T : class?
    {
        if (value is null)
            return kvas.Set(key, null, cancellationToken);

        using var buffer = new ArrayPoolBufferWriter<byte>(ArrayPools.SharedBytePool, 1024);
        Serializer.Write(buffer, value, typeof(T));
        return kvas.Set(key, buffer.WrittenMemory.ToArray(), cancellationToken);
    }

    [UnconditionalSuppressMessage("Tasks", "MA0100", Justification = "Don't need to wait for Set completion to dispose buffer writer.")]
    public static Task Set<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvas kvas, T? value, CancellationToken cancellationToken = default)
        where T : class, IHasKvasKey<T>
        => kvas.Set(T.KvasKey, value, cancellationToken);

    public static async Task<T> Update<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvas kvas, string key, Func<T, T> update, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var value = await kvas.Get<T>(key, cancellationToken).ConfigureAwait(false);
        var newValue = update(value ?? new T());
        await kvas.Set(key, newValue, cancellationToken).ConfigureAwait(false);
        return newValue;
    }

    public static async Task<T> Update<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvas kvas, Func<T, T> update, CancellationToken cancellationToken = default)
        where T : class, IHasKvasKey<T>, new()
    {
        var value = await kvas.Get<T>(cancellationToken).ConfigureAwait(false);
        var newValue = update.Invoke(value ?? new T());
        await kvas.Set(newValue, cancellationToken).ConfigureAwait(false);
        return newValue;
    }

    public static async Task WhenMigrated(this IKvas kvas, CancellationToken cancellationToken = default)
    {
        // NOTE(AY): This method is unused for now, but I decided to keep it - just in case
        var cMigratedTask = Computed
            .Capture(() => kvas.Get(MigratedKey, cancellationToken), cancellationToken);
        var cSettingsTask = Computed
            .Capture(() => kvas.Get("UserLanguageSettings", cancellationToken), cancellationToken);
        var cMigrated = await cMigratedTask.ConfigureAwait(false);
        var cSettings = await cSettingsTask.ConfigureAwait(false);
        var task1 = cMigrated.When(x => x != null, cancellationToken);
        var task2 = cSettings.When(x => x != null, cancellationToken);
        await Task.WhenAny(task1, task2).ConfigureAwait(false);
    }

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

    // IKvasStore
    public static async ValueTask<(string, T)[]> ListAllEntries<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvasStore kvas, CancellationToken cancellationToken = default)
        where T : class?
    {
        var data = await kvas.ListAllEntries(cancellationToken).ConfigureAwait(false);
        return data.Select(x => (x.Key, (T)Serializer.Read(x.Value, typeof(T), out _)!)).ToArray();
    }
}
