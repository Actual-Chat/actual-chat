using ActualChat.Kvas;
using ActualLab.Locking;
using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

public class PostRequestsStorage(AppUIHub hub)
{
    private const string EntryKey = "PostRequestEntryQueue";
    private readonly AsyncLock _asyncLock = new(LockReentryMode.CheckedFail);
    private LocalSettings LocalSettings => hub.LocalSettings;

    public async Task Add(PostMessageRequestEntry entry, CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var entries = await LocalSettings.Get<PostMessageRequestEntry[]>(EntryKey, cancellationToken).ConfigureAwait(false);
        entries ??= [];
        var list = entries.ToList();
        list.Add(entry);
        entries = list.ToArray();
        await LocalSettings.Set(EntryKey, entries, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAttachRequest(string entryUuid, AttachFileRequestEntry fileRequestEntry, CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var entries = await LocalSettings.Get<PostMessageRequestEntry[]>(EntryKey, cancellationToken).ConfigureAwait(false);
        entries ??= [];
        var i = Array.FindIndex(entries, c => OrdinalEquals(c.Uuid, entryUuid));
        if (i < 0)
            return; // Nothing do. Everything is cleaned up.

        var entry = entries[i];
        var attachRequests = entry.AttachFileRequests.ToList();
        var i1 = attachRequests.IndexOf(fileRequestEntry);
        if (i1 < 0)
            throw new InvalidOperationException("Can not find given attach request entry.");
        attachRequests.RemoveAt(i1);
        entries[i] = entry with {
            AttachFileRequests = attachRequests.ToArray(),
        };
        await LocalSettings.Set(EntryKey, entries, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PostMessageRequestEntry[]> GetStored(CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var entries = await LocalSettings.Get<PostMessageRequestEntry[]>(EntryKey, cancellationToken).ConfigureAwait(false);
        return entries ?? [];
    }

    public async Task<bool> Remove(string uuid, CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var entries = await LocalSettings.Get<PostMessageRequestEntry[]>(EntryKey, cancellationToken).ConfigureAwait(false);
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
public partial record PostMessageRequestEntry(
    [property: DataMember, MemoryPackOrder(0)] string Uuid,
    [property: DataMember, MemoryPackOrder(1)] Moment Now,
    [property: DataMember, MemoryPackOrder(2)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(3)] long? LocalId,
    [property: DataMember, MemoryPackOrder(4)] string Text,
    [property: DataMember, MemoryPackOrder(5)] Option<long?> RepliedEntryLid,
    [property: DataMember, MemoryPackOrder(6)] AttachFileRequestEntry[] AttachFileRequests
    ) : IHasId<string>
{
    string IHasId<string>.Id => Uuid;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AttachFileRequestEntry(
    [property: DataMember, MemoryPackOrder(0)] string UploadSessionId,
    [property: DataMember, MemoryPackOrder(1)] string FileName,
    [property: DataMember, MemoryPackOrder(2)] string FileType
) : IHasId<string>
{
    string IHasId<string>.Id => UploadSessionId;
}
