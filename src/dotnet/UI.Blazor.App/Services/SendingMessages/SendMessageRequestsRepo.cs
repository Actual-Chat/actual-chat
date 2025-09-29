using ActualChat.Kvas;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;
using ActualLab.Locking;
using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

public class SendMessageRequestsRepo
{
    private readonly AsyncLock _asyncLock = new(LockReentryMode.CheckedFail);
    private readonly PostRequestsStorageInternal _internal;

    public SendMessageRequestsRepo(AppUIHub hub)
    {
        var options = new PostRequestsStorageInternal.Options {
            BackendFactory = c => new WebKvasBackend($"{BlazorUIAppModule.ImportName}.sendMessageRequests", c),
        };
        _internal = new PostRequestsStorageInternal(options, hub.Services);
    }

    public async Task Add(SendMessageRequestEntry entry, CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        await _internal.Set(entry.Uuid, entry, cancellationToken).ConfigureAwait(false);
        await _internal.Flush(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAttachRequest(string entryUuid, AttachFileRequestEntry fileRequestEntry, CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var entry = await _internal.Get<SendMessageRequestEntry>(entryUuid, cancellationToken).ConfigureAwait(false);
        if (entry == null)
            return; // Nothing do. Everything is cleaned up.

        var attachRequests = entry.AttachFileRequests.ToList();
        var i1 = attachRequests.IndexOf(fileRequestEntry);
        if (i1 < 0)
            throw new InvalidOperationException("Can not find given attach request entry.");

        attachRequests.RemoveAt(i1);
        entry = entry with {
            AttachFileRequests = attachRequests.ToArray(),
        };
        await _internal.Set(entry.Uuid, entry, cancellationToken).ConfigureAwait(false);
        await _internal.Flush(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<KeyValuePair<string, SendMessageRequestEntry>>> GetStored(CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        return (await _internal.GetAll<SendMessageRequestEntry>(cancellationToken).ConfigureAwait(false))
            .Select(c => new KeyValuePair<string, SendMessageRequestEntry>(c.Item1, c.Item2))
            .ToArray();
    }

    public async Task Remove(string uuid, CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        await _internal.Set(uuid, null, cancellationToken).ConfigureAwait(false);
        await _internal.Flush(cancellationToken).ConfigureAwait(false);
    }
}

internal class PostRequestsStorageInternal : BatchingKvas
{
    public new record Options : BatchingKvas.Options
    {
        public required Func<IServiceProvider, IBatchingKvasBackend> BackendFactory { get; init; }
    }

    public new Options Settings { get; }

    public PostRequestsStorageInternal(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Backend = settings.BackendFactory.Invoke(services);
        _ = Reader.Start();
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record SendMessageRequestEntry(
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
