using CommunityToolkit.HighPerformance.Buffers;

namespace ActualChat.Kvas;

#pragma warning disable IL2026 // We change everything to DynamicallyAccessedMemberTypes.All on serialized type

public static class KvasExt
{
    public static KvasSerializer Serializer { get; set; } = KvasSerializer.Default;
    public static readonly string MigratedKey = "@Migrated";

    // Get, Set, Remove w/ <T>

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "T is marked with DynamicallyAccessedMembers.")]
    public static async ValueTask<T?> Get<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvas kvas, string key, CancellationToken cancellationToken = default)
        where T : class?
  {
        var data = await kvas.Get(key, cancellationToken).ConfigureAwait(false);
        return data is null ? null : (T)Serializer.Read(data, typeof(T), out _)!;
  }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "T is marked with DynamicallyAccessedMembers.")]
    public static ValueTask<T?> Get<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvas kvas, CancellationToken cancellationToken = default)
        where T : class, IHasKvasKey<T>
        => kvas.Get<T>(T.KvasKey, cancellationToken);

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "T is marked with DynamicallyAccessedMembers.")]
    [UnconditionalSuppressMessage("Tasks", "MA0100", Justification = "Don't need to wait for Set completion to dispose buffer writer.")]
    public static Task Set<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvas kvas, string key, T value, CancellationToken cancellationToken = default)
        where T : class?
    {
        if (value is null)
            return kvas.Set(key, null, cancellationToken);

        using var buffer = new ArrayPoolBufferWriter<byte>();
        Serializer.Write(buffer, value, typeof(T));
        return kvas.Set(key, buffer.WrittenMemory.ToArray(), cancellationToken);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "T is marked with DynamicallyAccessedMembers.")]
    [UnconditionalSuppressMessage("Tasks", "MA0100", Justification = "Don't need to wait for Set completion to dispose buffer writer.")]
    public static Task Set<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvas kvas, T? value, CancellationToken cancellationToken = default)
        where T : class, IHasKvasKey<T>
        => kvas.Set(T.KvasKey, value, cancellationToken);

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "T is marked with DynamicallyAccessedMembers.")]
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

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "T is marked with DynamicallyAccessedMembers.")]
    public static async Task<T> Update<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
        (this IKvas kvas, Func<T, T> update, CancellationToken cancellationToken = default)
        where T : class, IHasKvasKey<T>, new()
    {
        var value = await kvas.Get<T>(cancellationToken);
        var newValue = update(value ?? new T());
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

    public static IKvas<TScope> WithScope<TScope>(this IKvas kvas)
        => new ScopedKvasProxy<TScope>(kvas);
}
