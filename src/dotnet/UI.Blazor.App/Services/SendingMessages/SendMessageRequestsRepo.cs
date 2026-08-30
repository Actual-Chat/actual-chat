using ActualChat.Kvas;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;
using ActualLab.Locking;

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

    public async Task MarkMessageWasCreated(string requestUuid, long chatEntryLocalId, CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var entry = await _internal.Get<SendMessageRequestEntry>(requestUuid, cancellationToken).ConfigureAwait(false);
        if (entry == null)
            throw StandardError.Internal("Can not find given send message request entry.");
        entry = entry with {
            NewChatEntryLocalId = chatEntryLocalId,
        };
        await _internal.Set(entry.Uuid, entry, cancellationToken).ConfigureAwait(false);
        await _internal.Flush(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<KeyValuePair<string, SendMessageRequestEntry?>>> GetStored(CancellationToken cancellationToken)
    {
        using var releaser = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        return (await _internal.ListAllEntries<SendMessageRequestEntry>(cancellationToken).ConfigureAwait(false))
            .Select(c => new KeyValuePair<string, SendMessageRequestEntry?>(c.Item1, c.Item2))
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

[DataContract, MessagePackObject]
public sealed partial record SendMessageRequestEntry : IHasId<string>, ISanitized
{
    [DataMember, Key(0)] public required string Uuid { get; init; }
    [DataMember, Key(1)] public required Moment Now { get; init; }
    [DataMember, Key(2)] public required ChatId ChatId { get; init; }
    [DataMember, Key(3)] public long? LocalId { get; init; }
    [DataMember, Key(4)] public string Text {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";
    [DataMember, Key(5)] public Option<long?> RepliedEntryLid { get; init; }
    [DataMember, Key(6)] public AttachFileRequestEntry[] AttachFileRequests { get; init; } = [];
    [DataMember, Key(7)] public string ClientId { get; init; } = "";
    [DataMember, Key(8)] public string AfterSendMessageHandlerKey { get; init; } = "";
    [DataMember, Key(9)] public string AfterSendMessageHandlerArgs { get; init; } = "";
    [DataMember, Key(10)] public long? NewChatEntryLocalId { get; init; }
    [DataMember, Key(11)] public MediaRef[] ExistingMedia { get; init; } = [];

    string IHasId<string>.Id => Uuid;

    [SerializationConstructor]
    public SendMessageRequestEntry() { }
}

[DataContract, MessagePackObject]
public partial record AttachFileRequestEntry(
    [property: DataMember, Key(0)] string UploadSessionId,
    [property: DataMember, Key(1)] string FileName,
    [property: DataMember, Key(2)] string FileType,
    [property: DataMember, Key(3)] long FileLength,
    [property: DataMember, Key(4)] int Width,
    [property: DataMember, Key(5)] int Height
) : IHasId<string>
{
    string IHasId<string>.Id => UploadSessionId;
}
