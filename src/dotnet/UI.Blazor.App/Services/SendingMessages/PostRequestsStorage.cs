using ActualChat.Kvas;
using ActualLab.Locking;
using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

public class PostRequestsStorage(AppUIHub hub)
{
    private const string EntryKey = "PostRequestEntryQueue";
    private readonly AsyncLock _asyncLock = new(LockReentryMode.CheckedFail);
    private LocalSettings LocalSettings => hub.LocalSettings;

    public async Task Add(PostRequestEntry entry, CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var entries = await LocalSettings.Get<PostRequestEntry[]>(EntryKey, cancellationToken).ConfigureAwait(false);
        entries ??= [];
        var list = entries.ToList();
        list.Add(entry);
        entries = list.ToArray();
        await LocalSettings.Set(EntryKey, entries, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PostRequestEntry[]> GetStored(CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var entries = await LocalSettings.Get<PostRequestEntry[]>(EntryKey, cancellationToken).ConfigureAwait(false);
        return entries ?? [];
    }

    public async Task<bool> Remove(string uuid, CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var entries = await LocalSettings.Get<PostRequestEntry[]>(EntryKey, cancellationToken).ConfigureAwait(false);
        entries ??= [];
        var list = entries.ToList();
        var entryToRemove = list.Find(c => OrdinalEquals(c.Uuid, uuid));
        if (entryToRemove is null)
            return false;
        list.Remove(entryToRemove);
        entries = list.ToArray();
        await LocalSettings.Set(EntryKey, entries, cancellationToken).ConfigureAwait(false);
        return true;
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record PostRequestEntry(
    [property: DataMember, MemoryPackOrder(0)] string Uuid,
    [property: DataMember, MemoryPackOrder(1)] Chats_UpsertTextEntry Command,
    [property: DataMember, MemoryPackOrder(2)] Moment Now) : IHasId<string>
{
    string IHasId<string>.Id => Uuid;
}
