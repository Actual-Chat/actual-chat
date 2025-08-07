using ActualChat.Chat.Db;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Diagnostics;
using ActualChat.Mesh;
using ActualChat.Queues;
using ActualChat.Streaming;
using ActualChat.Transcription;
using ActualLab.Diagnostics;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Rpc;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public class TranslationsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), ITranslationsBackend
{
    private static readonly TimeSpan TranslateThrottleDelay = TimeSpan.FromMilliseconds(500);
    private readonly ConcurrentDictionary<StreamId, FuncWorker> _activePublishers = new ();

    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbTranslation> EntityResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbTranslation>>();
    [field: AllowNull, MaybeNull]
    private Translator Translator => field ??= Services.GetRequiredService<Translator>();
    [field: AllowNull, MaybeNull]
    private Translator RealtimeTranslator => field ??= Services.GetRequiredKeyedService<Translator>(Constants.Translation.RealtimeServiceKey);
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    [field: AllowNull, MaybeNull]
    private DiffEngine DiffEngine => field ??= Services.GetRequiredService<DiffEngine>();
    [field: AllowNull, MaybeNull]
    private IQueues Queues => field ??= Services.Queues();
    [field: AllowNull, MaybeNull]
    private MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    [field: AllowNull, MaybeNull]
    private IStreamingBackend StreamingBackend => field ??= Services.GetRequiredService<IStreamingBackend>();
    [field: AllowNull, MaybeNull]
    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    [field: AllowNull, MaybeNull]
    private IConversationsBackend ConversationsBackend => field ??= Services.GetRequiredService<IConversationsBackend>();
    private static bool DebugMode => Constants.DebugMode.TranslationBackend;
    private ILogger? DebugLog => DebugMode ? Log : null;

    // [ComputeMethod]
    public virtual async Task<Translation?> Get(TranslationId id, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return null;

        var (translationSource, translation) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (!translationSource.NeedsTranslation(translation))
            return translation;

        // we only try to enqueue and fast return to allow compute method to cache current result
        var cmd = new TranslationsBackend_Translate(id.SourceId, id.Language, false, false);
        await Queues.Enqueue(cmd, cancellationToken).ConfigureAwait(false);
        return translation;
    }

    // Not a [ComputeMethod]!
    public virtual async Task<ApiArray<Translation>> ListHanging(int limit, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        DateTime dMinModifiedAt = Clocks.SystemClock.Now - Settings.Translation.HangingTimeout;
        var dbTranslations = await dbContext.Translations.Where(x => !string.IsNullOrEmpty(x.StreamId) && x.ModifiedAt < dMinModifiedAt)
            .OrderBy(x => x.ModifiedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbTranslations.Select(x => x.ToModel()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<Translation?> OnChange(TranslationsBackend_Change command, CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        if (Invalidation.IsActive) {
            _ = GetInternal(id, default);
            return null!;
        }

        if (!Settings.IsTranslationEnabled)
            return null;

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;

        DbTranslation? dbTranslation;
        if (change.IsCreate(out var update)) {
            // Lock is required. We can't double-check the existence of the translation because we use RepeatableRead isolation level..
            await dbContext.Translations.Lock(id, cancellationToken).ConfigureAwait(false);

            dbTranslation = await dbContext.Translations.GetAsNoTracking(id.Value, cancellationToken).ConfigureAwait(false);
            if (dbTranslation is not null)
                return dbTranslation.ToModel();

            var translation = new Translation(id) {
                CreatedAt = now,
            };
            translation = ApplyDiff(translation, update);
            dbTranslation = new DbTranslation(translation) {
                Id = id.Value,
                CreatedAt = now,
                ModifiedAt = now,
            };

            dbContext.Add(dbTranslation);
        }
        else if (change.IsUpdate(out update)) {
            dbTranslation = await dbContext.Translations.GetAsNoTracking(id.Value, cancellationToken).ConfigureAwait(false);
            if (dbTranslation is null)
                return null;

            dbTranslation.RequireVersion(expectedVersion);
            var translation = ApplyDiff(dbTranslation.ToModel(), update);
            dbContext.Translations.Attach(dbTranslation);
            dbTranslation.UpdateFrom(translation);
        }
        else {
            await dbContext.Translations.Lock(id, cancellationToken).ConfigureAwait(false);
            dbTranslation = await dbContext.Translations.GetAsNoTracking(id.Value, cancellationToken).ConfigureAwait(false);
            if (dbTranslation is null)
                return null;

            dbTranslation.RequireVersion(expectedVersion);
            dbContext.Remove(dbTranslation);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbTranslation.ToModel();

        Translation ApplyDiff(Translation originalTranslation, TranslationDiff? diff) {
            // Update
            var newTranslation = DiffEngine.Patch(originalTranslation, diff) with {
                ModifiedAt = now,
                Version = diff?.Version ?? VersionGenerator.NextVersion(originalTranslation.Version),
            };
            // Validate
            if (!newTranslation.Content.IsNullOrEmpty() && newTranslation.SourceContentHash.IsNone)
                throw StandardError.Constraint("SourceContentHash must be set for non-empty Content.");
            return newTranslation;
        }
    }

    // [CommandHandler]
    public virtual async Task<Translation?> OnTranslate(TranslationsBackend_Translate command, CancellationToken cancellationToken)
    {
        var (sourceId, targetLanguage, ignoreVersion, skipRealtime) = command;
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var id = TranslationId.New(sourceId, targetLanguage);
        var (translationSource, translation) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        var isRetranslation = translation is not null && ignoreVersion && skipRealtime;
        if (!translationSource.NeedsTranslation(translation, isRetranslation))
            return translation;

        return skipRealtime || translationSource.Content.Length < Settings.Translation.StreamingMinContentLength
            ? await TranslateWithoutStreaming().ConfigureAwait(false)
            : await StreamTranslation().ConfigureAwait(false);

        async Task<Translation?> TranslateWithoutStreaming()
        {
            var context = await GetTranslationContext1().ConfigureAwait(false);
            var translatedText = await Translator.Translate(
                translationSource.Content,
                id.Language,
                context,
                cancellationToken).ConfigureAwait(false);

            var contentHash = translationSource.ContentHash.IsNone
                ? ChatEntryHashExt.GetContentHashString(translationSource.Content)
                : translationSource.ContentHash;
            var translationDiff = new TranslationDiff {
                StreamId = null,
                Content = translatedText,
                SourceContentHash = contentHash,
            };
            var change = translation is null
                ? Change.Create(translationDiff)
                : Change.Update(translationDiff);
            var version = ignoreVersion ? null : translation?.Version;
            var cmd = new TranslationsBackend_Change(id, version, change);
            return await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        }

        async Task<Translation?> StreamTranslation()
        {
            var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
            var context = await GetTranslationContext1().ConfigureAwait(false);
            var stream = Translator
                .Stream(translationSource.Content, id.Language, context, cancellationToken)
                .ToTranscriptDiffs()
                .Memoize(cancellationToken);
            var rpcStream = RpcStream.New(stream.Replay(cancellationToken));
            var publishStreamTask = StreamingBackend.PushTranscript(streamId, rpcStream, cancellationToken);
            // ensure a translation is created
            var translationDiff = new TranslationDiff {
                StreamId = streamId,
                Content = "",
                SourceContentHash = translationSource.ContentHash,
            };
            var change = translation is null
                ? Change.Create(translationDiff)
                : Change.Update(translationDiff);
            var version = ignoreVersion ? null : translation?.Version;
            var cmd = new TranslationsBackend_Change(id, version, change);

            // Use `isOutermost = true` to ensure that the translation will appear in streaming mode ASAP
            translation = await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);

            var translatedTranscript = Transcript.Empty;
            try {
                await foreach (var diff in stream.Replay(cancellationToken).ConfigureAwait(false))
                    translatedTranscript = diff.ApplyTo(translatedTranscript);
                await publishStreamTask.ConfigureAwait(false);
            }
            finally {
                var contentHash = translationSource.ContentHash.IsNone
                    ? ChatEntryHashExt.GetContentHashString(translationSource.Content)
                    : translationSource.ContentHash;
                var finalizeChange = Change.Update(new TranslationDiff {
                    StreamId = null,
                    Content = translatedTranscript.Text,
                    SourceContentHash = contentHash,
                });
                var finalizeCmd = new TranslationsBackend_Change(
                    id,
                    translation.Version,
                    finalizeChange);
                translation = await Commander.Call(finalizeCmd, cancellationToken).ConfigureAwait(false);
            }
            return translation;
        }

        async Task<TranslationResult[]> GetTranslationContext1()
        {
            if (id.Kind is not TranslationIdKind.TextEntry)
                return [];

            var chatEntryId = id.SourceId.GetChatEntryId();
            return await GetTranslationContext(chatEntryId, targetLanguage, cancellationToken).ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task<StreamId?> OnTranslateStream(
        TranslationsBackend_TranslateStream command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (streamId, targetLanguage) = command;
        DebugLog?.LogDebug("OnTranslateStream: {StreamId} -> {Language}", streamId, targetLanguage);

        return await StartTranscriptStreamTranslation(streamId, targetLanguage, cancellationToken).ConfigureAwait(false);
    }

    // protected methods

    [ComputeMethod]
    protected virtual async Task<Translation?> GetInternal(TranslationId id, CancellationToken cancellationToken)
    {
        var dbTranslation = await EntityResolver.Get(id.Value, cancellationToken).ConfigureAwait(false);
        var translation = dbTranslation?.ToModel();
        return translation;
    }

    [ComputeMethod]
    protected virtual async Task<TranslationResult[]> GetTranslationContext(ChatEntryId id, Language language, CancellationToken cancellationToken)
    {
        var count = Settings.Translation.ContextMessageCount;
        var translatedEntry = await ChatsBackend.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (translatedEntry is null)
            return [];

        if (translatedEntry.Content.Length > Settings.Translation.ContentMinLengthWithoutContext)
            return [];

        var entries = await ListEntries()
            .Where(x => x.SupportsTranslation(false))
            .Take(count * 2)
            .SelectAwait(async e => (e, await GetInternal(TranslationId.New(TextEntryId.New(e.ChatId, e.LocalId), language), cancellationToken).ConfigureAwait(false)))
            .Where(x => x.Item2 is not null)
            .OrderBy(x => x.e.LocalId)
            .Select(x => new TranslationResult(x.e.Content, x.Item2!.Content))
            .Take(count)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return entries.ToArray();

        async IAsyncEnumerable<ChatEntry> ListEntries()
        {
            for (var maxLidExclusive = id.LocalId; maxLidExclusive >= 0; maxLidExclusive -= Settings.Translation.ContextMessageCount) {
                var minLid = (maxLidExclusive - Settings.Translation.ContextMessageCount).Clamp(0, long.MaxValue);
                var idRange = new Range<long>(minLid, maxLidExclusive);
                var foundEntries = await ChatsBackend.GetEntries(id.ChatId, ChatEntryKind.Text, idRange, false, cancellationToken).ConfigureAwait(false);
                foreach (var entry in foundEntries.Where(e => e.LocalId < maxLidExclusive))
                    yield return entry;
            }
        }
    }

    protected virtual async Task<StreamId?> StartTranscriptStreamTranslation(StreamId streamId, Language targetLanguage, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return null;

        var transcript = await StreamingBackend.GetTranscript(streamId, cancellationToken).ConfigureAwait(false);
        if (transcript is null)
            return null;

        TranslationSourceId sourceId;
        var applicationStopping = Services.HostLifetime().ApplicationStopping;
        var cts = applicationStopping.CreateLinkedTokenSource();
        var transcriptStream = transcript
            .SuppressException<TranscriptDiff, RpcReconnectFailedException>(cts.Token)
            .Memoize(cts.Token);
        var translatedStreamId = StreamId.New(streamId, targetLanguage);
        {
            // ReSharper disable PossiblyMistakenUseOfCancellationToken
            var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            // This query will be executed once - no need to wrap in a compute method
            var chatEntrySid = await dbContext.ChatEntries
                .Where(e => e.Kind == ChatEntryKind.Text && e.StreamId == streamId.Value)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (chatEntrySid == null)
                return null; // Already transcribed

            sourceId = TranslationSourceId.New(TextEntryId.Parse(chatEntrySid));
        }

        var translationId = TranslationId.New(sourceId, targetLanguage);
        var newTranslationVersion = VersionGenerator.NextVersion();
        var cmd = new TranslationsBackend_Change(translationId,
            null,
            Change.Create(new TranslationDiff {
                StreamId = translatedStreamId,
                Content = "",
                Version = newTranslationVersion,
            }));
        var translation = await Commander
            .Call(cmd, cancellationToken)
            .ConfigureAwait(false);
        if (translation.Version != newTranslationVersion)
            return translatedStreamId; // Already being translated

        var worker = _activePublishers.GetOrAdd(translatedStreamId,
            static (_, args) => {
                return FuncWorker.New(
                    (args1, ct) => args1.Item1.TranslateTranscriptStream(
                        args1.transcriptStream,
                        args1.translatedStreamId,
                        args1.translationId,
                        args1.newTranslationVersion,
                        ct),
                    args,
                    args.cts);
            },
            (this, transcriptStream, translatedStreamId, translationId, newTranslationVersion, cts));

        DebugLog?.LogDebug("StartTranscriptStreamTranslation: {StreamId} -> {Language}", streamId, targetLanguage);
        worker.Start();
        return translatedStreamId;
    }

    private async Task TranslateTranscriptStream(
        AsyncMemoizer<TranscriptDiff> originalStream,
        StreamId translatedStreamId,
        TranslationId translationId,
        long newTranslationVersion,
        CancellationToken cancellationToken)
    {
        var language = translatedStreamId.Language!;
        DebugLog?.LogDebug("TranslateTranscriptStream: {StreamId}", translatedStreamId);

        var channel = Channel.CreateUnbounded<TranscriptDiff>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });
        using var activity = CoreServerInstruments.ActivitySource.StartActivity(ActivityKind.Client);
        try {
            var reader = channel.Reader;
            var writer = channel.Writer;
            _ = BackgroundTask.Run(async () => {
                    Exception? error = null;
                    var lastText = "";
                    var lastTranscript = Transcript.Empty;
                    var lastTranslatedTranscript = Transcript.Empty;
                    var stableTranscript = Transcript.Empty;
                    var stableTranslatedTranscript = Transcript.Empty;
                    try {
                        // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
                        await foreach (var transcriptDiffBatch in originalStream.Replay(cancellationToken)
                                           .Buffer(TranslateThrottleDelay,
                                               Clocks.CpuClock,
                                               cancellationToken: cancellationToken)
                                           .ConfigureAwait(false)) {
                            if (transcriptDiffBatch.Count == 0)
                                continue; // Skip empty batches

                            if (lastTranscript == Transcript.Empty)
                                DebugLog?.LogDebug("TranslateTranscriptStream: {StreamId} - First Transcript",
                                    translatedStreamId);
                            var transcriptBatch = transcriptDiffBatch.Scan((t, td) => t + td, lastTranscript).ToList();
                            var transcript = transcriptBatch[^1];
                            var newStableTranscript =
                                transcriptBatch.FirstOrDefault(t => t.IsStable) ?? stableTranscript;
                            if (newStableTranscript != stableTranscript) {
                                // Translate stable diff first, then the diff since stable state
                                var stableDiff = stableTranscript - newStableTranscript;
                                await Translate(stableDiff).ConfigureAwait(false);
                            }

                            // The diff represents the changes between the current transcript (transcript) and the stable transcript (stableTranscript).
                            // This diff is distinct from the transcriptDiff object because transcriptDiff is derived from the unstable transcript, which may still be undergoing changes.
                            // The purpose of this calculation is to isolate the differences that have occurred since the last stable state of the transcript.
                            var diffSinceStable = transcript - stableTranscript;
                            await Translate(diffSinceStable).ConfigureAwait(false);
                            lastTranscript = transcript;
                            continue;

                            async Task Translate(TranscriptDiff diff)
                            {
                                var text = diff.TextDiff.Suffix ?? "";
                                if (text.IsNullOrWhiteSpace())
                                    return;

                                if (OrdinalEquals(text, lastText))
                                    return; // No need to translate the same text (it's already been translated')

                                var context = new List<TranslationResult>();
                                if (!OrdinalEquals(stableTranscript.Text, stableTranslatedTranscript.Text))
                                    context.Add(new TranslationResult(stableTranscript.Text,
                                        stableTranslatedTranscript.Text));
                                var translatedText = await RealtimeTranslator.Translate(
                                        text,
                                        language,
                                        context.ToArray(),
                                        cancellationToken)
                                    .ConfigureAwait(false);
                                if (OrdinalIgnoreCaseEquals(translatedText,
                                        Constants.Translation.NoTranslationNeededText))
                                    translatedText = text; // No translation needed, use original content
                                if (!translatedText.StartsWith(" ", StringComparison.OrdinalIgnoreCase)
                                    && stableTranslatedTranscript.Text.Length > 0)
                                    translatedText = $" {translatedText}";
                                lastTranslatedTranscript = stableTranslatedTranscript.WithSuffix(translatedText,
                                    diff.TimeMapDiff.Suffix.Scale(text.Length, translatedText.Length));
                                var translatedStableDiff = lastTranslatedTranscript - stableTranslatedTranscript;
                                await writer.WriteAsync(translatedStableDiff, cancellationToken).ConfigureAwait(false);
                                if (diff.IsStable)
                                    stableTranslatedTranscript = lastTranslatedTranscript with { IsStable = true };
                                stableTranscript = newStableTranscript;
                                lastText = text;
                            }
                        }
                        var content = lastTranslatedTranscript.Text;
                        var sourceContent = lastTranscript.Text;
                        var finalizeRealtime = new TranslationsBackend_Change(translationId,
                            newTranslationVersion, // Will overwrite the first version only
                            Change.Update(new TranslationDiff {
                                StreamId = null,
                                Content = content,
                                SourceContentHash = ChatEntryHashExt.GetContentHashString(sourceContent),
                            }));
                        await Commander.Call(finalizeRealtime, true, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        error = ex;
                    }
                    finally {
                        try {
                            // // Delay to ensure ITranslationsBackend.Get() will return the finalized translation
                            // // Wait for Chat Entry Finalization - without delay translation will be skipped due to empty Content
                            // // TODO(AK): Think about more robust approach
                            // await Task.Delay(TranslateThrottleDelay * 2, cancellationToken).ConfigureAwait(false);

                            // Enqueue the translation command to retranslate full finalized transcript with larger context and model
                            // StreamId will be cleaned up by this command
                            var sourceId = translationId.SourceId;
                            var targetLanguage = translationId.Language;
                            var finalReTranslate =
                                new TranslationsBackend_Translate(sourceId, targetLanguage, true, true);
                            await Queues.Enqueue(finalReTranslate, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex2) {
                            if (error == null)
                                error = ex2; // Preserve the original error if it exists
                            else
                                Log.LogError(ex2, "Error while finalizing translation {StreamId}", translatedStreamId);
                        }
                        finally {
                            if (error != null)
                                Log.LogError(error,
                                    "Error while translating transcript {StreamId}",
                                    translatedStreamId);
                            writer.Complete(error);
                        }
                    }
                },
                cancellationToken);

            DebugLog?.LogDebug("TranslateTranscriptStream: {StreamId} - Publishing stream", translatedStreamId);

            var translatedStream = new RpcStream<TranscriptDiff>(reader.ReadAllAsync(cancellationToken));
            await StreamingBackend
                .PushTranscript(translatedStreamId, translatedStream, cancellationToken)
                .ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            DebugLog?.LogDebug("TranslateTranscriptStream: {StreamId} - Stream published", translatedStreamId);
        }
        catch (Exception ex3) {
            activity?.Finalize(ex3, cancellationToken);
            throw;
        }
        finally {
            if (_activePublishers.Remove(translatedStreamId, out var currentWorker))
                await currentWorker.DisposeSilentlyAsync().ConfigureAwait(false);
        }
    }

    protected virtual async Task<TranslationSource?> TryResolveTranslationSource(TranslationId translationId, CancellationToken cancellationToken)
    {
        var sourceId = translationId.SourceId;
        switch (sourceId.Kind) {
            case TranslationIdKind.TextEntry: {
                var chatEntryId = sourceId.GetChatEntryId();
                var entry = await ChatsBackend.GetEntry(chatEntryId, cancellationToken).ConfigureAwait(false);
                if (entry is null)
                    return null;

                return new TextEntryTranslationSource(entry, sourceId);
            }
            case TranslationIdKind.ConversationTitle or
                TranslationIdKind.ConversationDescription or
                TranslationIdKind.ConversationSummary: {
                var lid = sourceId.GetRefLId();
                var conversationId = ConversationId.New(sourceId.ChatId, lid);
                var conversation = await ConversationsBackend.Get(conversationId, cancellationToken).ConfigureAwait(false);
                if (conversation is null)
                    return null;

                return new ConversationTranslationSource(conversation, sourceId);
            }
            case TranslationIdKind.ThreadTitle or
                TranslationIdKind.ThreadDescription : {
                var lid = sourceId.GetRefLId();
                var threadChatId = sourceId.ChatId.CreateThreadId(lid);
                var threadChat = await ChatsBackend.Get(threadChatId, cancellationToken).ConfigureAwait(false);
                if (threadChat is null)
                    return null;

                return new ThreadTranslationSource(threadChat, sourceId);
            }
            default: throw new ArgumentOutOfRangeException(nameof(translationId.Kind));
        }
    }

    // Private methods

    private async Task<(TranslationSource? source, Translation? translation)> GetExisting(TranslationId id, CancellationToken cancellationToken)
    {
        var source = await TryResolveTranslationSource(id, cancellationToken).ConfigureAwait(false);
        if (source is null)
            return (null, null);

        var translation = await GetInternal(id, cancellationToken).ConfigureAwait(false);
        return (source, translation);
    }
}
