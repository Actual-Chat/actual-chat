using ActualLab.Rpc;

namespace ActualChat.Media;

// Stores at most one pending generated image per key, plus a dismissal. The key is opaque:
// callers compose it, nothing here parses it. Permission checks belong to the caller - this
// service is unauthenticated like every other backend.

public interface IImageSuggestionsBackend : IComputeService, IBackendService
{
    // Returns the stored suggestion whether or not it is dismissed: filtering here would make this
    // depend on the current time, and a compute method that does never invalidates when the clock
    // passes the threshold. Callers compare GetDismissedUntil against now themselves.
    [ComputeMethod]
    Task<ImageSuggestion?> Get(string key, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Moment?> GetDismissedUntil(string key, CancellationToken cancellationToken);
    // When a generation is in flight, the moment it started. There is no way to predict its end,
    // and it is advisory: it lives on the node owning the key's shard and is lost if that node dies.
    [ComputeMethod]
    Task<Moment?> GetGenerationStartedAt(string key, CancellationToken cancellationToken);

    // Not a compute method: it is the sweep's scan, and tracking it would invalidate on every write
    Task<ApiArray<string>> ListStale(Moment maxCreatedAt, int limit, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ImageSuggestion?> OnGenerate(ImageSuggestionsBackend_Generate command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDismiss(ImageSuggestionsBackend_Dismiss command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemove(ImageSuggestionsBackend_Remove command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ImageSuggestionsBackend_Generate(
    [property: DataMember, Key(0)] string Key,
    [property: DataMember, Key(1)] string ImageDescription
) : ICommand<ImageSuggestion?>, IBackendCommand, IHasShardKey
{
    [DataMember, Key(2)] public int Width { get; init; } = 512;
    [DataMember, Key(3)] public int Height { get; init; } = 512;
    [DataMember, Key(4)] public MediaKind MediaKind { get; init; } = MediaKind.ChatPicture;

    // The style is kept out of ImageDescription so the stored description stays a subject - which
    // is what the generation modal shows and lets the user edit.
    [DataMember, Key(6)] public ImageStyle Style { get; init; } = ImageStyle.Default;
    // false = the banner's implicit trigger, satisfied by any existing suggestion;
    // true = someone pressed Regenerate and wants a different image than the one on screen
    [DataMember, Key(5)] public bool IsExplicit { get; init; }

    // Set on a regenerate so the same description yields a different image
    [DataMember, Key(7)] public long? Seed { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(Key);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ImageSuggestionsBackend_Dismiss(
    [property: DataMember, Key(0)] string Key,
    [property: DataMember, Key(1)] Moment DismissedUntil
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(Key);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ImageSuggestionsBackend_Remove(
    [property: DataMember, Key(0)] string Key,
    // False when the suggestion was accepted - the media is now the content's own picture
    [property: DataMember, Key(1)] bool MustDeleteMedia
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(Key);
}
