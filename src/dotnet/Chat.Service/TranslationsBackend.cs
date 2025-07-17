using ActualChat.Chat.Db;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Mesh;
using ActualChat.Queues;
using ActualChat.Streaming;
using ActualChat.Transcription;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Rpc;
using ActualLab.Versioning;
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
        var cmd = new TranslationsBackend_Translate(id, false);
        await Queues.Enqueue(cmd, cancellationToken).ConfigureAwait(false);
        return translation;
    }

    // Not a compute method
    public Task<string> Translate(TranslationId id, string prefix, string content, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return Task.FromResult("");

        if (content.IsNullOrEmpty())
            return Task.FromResult("");

        if (id.Kind is not TranslationIdKind.TextEntry)
            return Task.FromResult("");

        return TranslateInternal(id, prefix, content, true, cancellationToken);
    }

    [ComputeMethod]
    protected virtual async Task<string> GetTranslationContext(ChatEntryId id, int count, CancellationToken cancellationToken)
    {
        var translatedEntry = await ChatsBackend.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (translatedEntry is null)
            return "";

        if (translatedEntry.Content.Length > Settings.Translation.ContentMinLengthWithoutContext)
            return "";

        var entries = await ListEntries()
            .Where(x => x.SupportsTranslation(false))
            .Take(count)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return string.Join(".\n", entries.Select(e => e.Content));

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

    // [CommandHandler]
    public virtual async Task<Translation?> OnChange(TranslationsBackend_Change command, CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        if (Invalidation.IsActive) {
            _ = Get(id, default);
            return null!;
        }

        if (!Settings.IsTranslationEnabled)
            return null;

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.Translations.LockShared(id, cancellationToken).ConfigureAwait(false);
        var dbTranslation = await dbContext.Translations.GetAsNoTracking(id.Value, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;

        if (change.IsCreate(out var translation)) {
            if (dbTranslation != null)
                return dbTranslation.ToModel();

            await dbContext.Translations.Lock(id, cancellationToken).ConfigureAwait(false);
            dbTranslation = new DbTranslation(translation) {
                Id = id.Value,
                CreatedAt = now,
                ModifiedAt = now,
            };
            if (dbTranslation.Version == 0)
                dbTranslation.Version = VersionGenerator.NextVersion();

            dbContext.Add(dbTranslation);
        }
        else if (change.IsUpdate(out translation)) {
            if (dbTranslation is null)
                return null;

            dbTranslation.RequireVersion(expectedVersion);
            dbContext.Translations.Attach(dbTranslation);
            translation = translation with {
                CreatedAt = dbTranslation.CreatedAt,
                ModifiedAt = now,
                Version = VersionGenerator.NextVersion(dbTranslation.Version),
            };
            dbTranslation.UpdateFrom(translation);
        }
        else
            throw StandardError.NotSupported("Translations cannot be removed.");

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbTranslation.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<Translation?> OnTranslate(TranslationsBackend_Translate command, CancellationToken cancellationToken)
    {
        var (id, ignoreVersion) = command;
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (translationSource, translation) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (!translationSource.NeedsTranslation(translation))
            return translation;

        translation ??= new Translation(id);
        return translationSource.Content.Length < Settings.Translation.StreamingMinContentLength
            ? await TranslateWithoutStreaming().ConfigureAwait(false)
            : await StreamTranslation().ConfigureAwait(false);

        async Task<Translation?> TranslateWithoutStreaming()
        {
            var translatedText = await TranslateInternal(id, "", translationSource.Content, false, cancellationToken).ConfigureAwait(false);
            return await Save(translation with {
                        Content = translatedText,
                        SourceContentHash = translationSource.ContentHash,
                    },
                    ignoreVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        async Task<Translation?> StreamTranslation()
        {
            var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
            var context = await GetTranslationContext(id.SourceId.GetChatEntryId(), Settings.Translation.ContextMessageCount, cancellationToken).ConfigureAwait(false);
            var stream = Translator.Stream(translationSource.Content, id.Language, context, cancellationToken).Memoize(cancellationToken);
            var publishStreamTask = StreamingBackend.PublishTranslation(streamId, stream.Replay(cancellationToken), cancellationToken);
            // ensure a translation is created
            translation = await Save(translation with {
                        StreamId = streamId.Value,
                        SourceContentHash = translationSource.ContentHash,
                        IsRealtime = false,
                    },
                    ignoreVersion,
                    cancellationToken)
                .Require()
                .ConfigureAwait(false);

            var translatedText = "";
            try {
                await publishStreamTask.ConfigureAwait(false);
                await foreach (var diff in stream.Replay(cancellationToken).ConfigureAwait(false))
                    translatedText = diff.ApplyTo(translatedText);
            }
            finally {
                translation = await Save(translation with {
                            Content = translatedText,
                            StreamId = "",
                            SourceContentHash = translationSource.ContentHash,
                        },
                        ignoreVersion,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            return translation;
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
        var translationCompleted = TaskCompletionSourceExt.New<TranslationResult>();
        var transcriptStream = transcript.Memoize(cts.Token);
        var translatedStreamId = StreamId.New(streamId, targetLanguage);
        var worker = _activePublishers.GetOrAdd(translatedStreamId,
            static (_, args) => {
                return FuncWorker.New(
                    (args1, ct) => args1.Item1.TranslateTranscriptStream(
                        args1.transcriptStream,
                        args1.translatedStreamId,
                        args1.translationCompleted,
                        ct),
                    args,
                    args.cts);
            },
            (this, transcriptStream, translatedStreamId, translationCompleted, cts));

        DebugLog?.LogDebug("StartTranscriptStreamTranslation: {StreamId} -> {Language}", streamId, targetLanguage);
        worker.Start();
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
            Change.Create(new Translation(translationId) {
                StreamId = translatedStreamId.Value,
                Content = "",
                Version = newTranslationVersion,
            }));
        var newTranslation = await Commander.Call(cmd, cancellationToken)
            .ConfigureAwait(false);

        if (newTranslation!.Version != newTranslationVersion)
            return translatedStreamId; // Already being translated

        var finalizeTask = translationCompleted.Task.ContinueWith(async r => {
            var (originalText, translatedText) = await r.ConfigureAwait(false);
            var finalizeRealtime = new TranslationsBackend_Change(translationId,
                newTranslationVersion, // Will overwrite the first version only
                Change.Update(new Translation(translationId) {
                    StreamId = Symbol.Empty,
                    Content = translatedText,
                    SourceContentHash = ChatEntryHashExt.GetContentHashString(originalText),
                }));
            return Commander.Call(finalizeRealtime, true, applicationStopping)
                .Catch(Log, "Failed to finalize streaming translation #{TranslationId}", translationId);
        }, applicationStopping, TaskContinuationOptions.None, TaskScheduler.Default);

        _ = Task.WhenAll(finalizeTask, transcriptStream.WriteTask).ContinueWith(async _ => {
            // Wait for Chat Entry Finalization - without delay translation will be skipped due to empty Content
            // TODO(AK): Think about more robust approach
            await Clocks.CpuClock.Delay(200, applicationStopping).ConfigureAwait(false);

            // Enqueue the translation command to retranslate full finalized transcript with larger context and model
            var finalReTranslate = new TranslationsBackend_Translate(translationId, true);
            await Queues.Enqueue(finalReTranslate, applicationStopping).ConfigureAwait(false);
        }, applicationStopping, TaskContinuationOptions.None, TaskScheduler.Default);

        // ReSharper enable PossiblyMistakenUseOfCancellationToken
        return translatedStreamId;
    }

    private async Task TranslateTranscriptStream(AsyncMemoizer<TranscriptDiff> originalStream, StreamId translatedStreamId, TaskCompletionSource<TranslationResult> translationCompleted, CancellationToken cancellationToken)
    {
        var language = translatedStreamId.Language!;
        DebugLog?.LogDebug("TranslateTranscriptStream: {StreamId}", translatedStreamId);

        var channel = Channel.CreateUnbounded<TranscriptDiff>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });
        try {
            var reader = channel.Reader;
            var writer = channel.Writer;
            _ = Task.Run(async () => {
                Exception? error = null;
                var lastTranscript = Transcript.Empty;
                var stableTranscript = Transcript.Empty;
                var stableTranslatedTranscript = Transcript.Empty;
                try {
                    // ReSharper disable once UseCancellationTokenForIAsyncEnumerable
                    await foreach (var transcriptDiffBatch in originalStream.Replay(cancellationToken).Buffer(TranslateThrottleDelay, Clocks.CpuClock, cancellationToken: cancellationToken).ConfigureAwait(false)) {
                        if (transcriptDiffBatch.Count == 0)
                            continue; // Skip empty batches

                        if (lastTranscript == Transcript.Empty)
                            DebugLog?.LogDebug("TranslateTranscriptStream: {StreamId} - First Transcript", translatedStreamId);
                        var transcriptBatch = transcriptDiffBatch.Scan((t, td) => t + td, lastTranscript).ToList();
                        var transcript = lastTranscript = transcriptBatch[^1];
                        var newStableTranscript = transcriptBatch.FirstOrDefault(t => t.IsStable) ?? Transcript.Empty;
                        var prefix = stableTranscript.Text;
                        // The diff represents the changes between the current transcript (transcript) and the stable transcript (stableTranscript).
                        // This diff is distinct from the transcriptDiff object because transcriptDiff is derived from the unstable transcript, which may still be undergoing changes.
                        // The purpose of this calculation is to isolate the differences that have occurred since the last stable state of the transcript.
                        var diffSinceStable = transcript - stableTranscript;
                        var content = diffSinceStable.TextDiff.Suffix ?? "";
                        if (!ReferenceEquals(newStableTranscript, Transcript.Empty))
                            stableTranscript = newStableTranscript;
                        if (content.IsNullOrWhiteSpace())
                            continue;

                        var translatedContent = await RealtimeTranslator.Translate(
                            content,
                            language,
                            prefix,
                            cancellationToken).ConfigureAwait(false);

                        // var translatedContent = await Translate(prefix, content, cancellationToken).ConfigureAwait(false);
                        if (OrdinalIgnoreCaseEquals(translatedContent, Constants.Translation.NoTranslationNeededText))
                            translatedContent = content; // No translation needed, use original content

                        if (!translatedContent.StartsWith(" ", StringComparison.OrdinalIgnoreCase) && stableTranslatedTranscript.Text.Length > 0)
                            translatedContent = $" {translatedContent}";

                        // DebugLog?.LogDebug("Translation in progress #{TranslationId}: {Content}->{TranslatedContent}", translationId, content, translatedContent);
                        var translatedTranscript = stableTranslatedTranscript.WithSuffix(translatedContent, diffSinceStable.TimeMapDiff.Suffix.Scale(content.Length, translatedContent.Length));
                        var translatedDiffSinceStable = translatedTranscript - stableTranslatedTranscript;
                        await writer.WriteAsync(translatedDiffSinceStable, cancellationToken).ConfigureAwait(false);
                        if (diffSinceStable.IsStable)
                            stableTranslatedTranscript = translatedTranscript with { IsStable = true };
                    }
                }
                catch (Exception ex) {
                    error = ex;
                }
                finally {
                    try {
                        if (error != null)
                            translationCompleted.SetException(error);
                        else
                            translationCompleted.SetResult(new TranslationResult(stableTranscript.Text, stableTranslatedTranscript.Text));

                        // delay to ensure ITranslationsBackend.Get() will return the finalized translation
                        await Task.Delay(TranslateThrottleDelay * 2, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex2) {
                        if (error == null)
                            error = ex2; // Preserve the original error if it exists
                        else
                            Log.LogError(ex2, "Error while finalizing translation {StreamId}", translatedStreamId);
                    }
                    finally {
                        if (error != null)
                            Log.LogError(error, "Error while translating transcript {StreamId}", translatedStreamId);
                        writer.Complete(error);
                    }
                }
            }, CancellationToken.None); // No need to cancel this task, it should finalize translation even if the caller cancels

            DebugLog?.LogDebug("TranslateTranscriptStream: {StreamId} - Publishing stream", translatedStreamId);

            var translatedStream = new RpcStream<TranscriptDiff>(reader.ReadAllAsync(cancellationToken));
            await StreamingBackend
                .PushTranscript(translatedStreamId, translatedStream, cancellationToken)
                .ConfigureAwait(false);

            DebugLog?.LogDebug("TranslateTranscriptStream: {StreamId} - Stream published", translatedStreamId);
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

        var dbTranslation = await EntityResolver.Get(id.Value, cancellationToken).ConfigureAwait(false);
        var translation = dbTranslation?.ToModel();
        return (source, translation);
    }

    private async Task<string> TranslateInternal(TranslationId id, string prefix, string content, bool isRealtime, CancellationToken cancellationToken)
    {
        var context = "";
        if (id.Kind is TranslationIdKind.TextEntry) {
            var chatEntryId = id.SourceId.GetChatEntryId();
            var count = isRealtime
                ? Settings.Translation.RealtimeContextMessageCount
                : Settings.Translation.ContextMessageCount;
            context = await GetTranslationContext(chatEntryId, count, cancellationToken).ConfigureAwait(false);
            if (!prefix.IsNullOrEmpty())
                context = $"{context}\n{prefix}";
        }
        var translator = isRealtime
            ? RealtimeTranslator
            : Translator;
        return await translator.Translate(
            content,
            id.Language,
            context,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Translation?> Save(Translation translation, bool ignoreVersion, CancellationToken cancellationToken)
    {
        try {
            var version = ignoreVersion
                ? (long?)null
                : translation.Version;
            var cmd = new TranslationsBackend_Change(translation.Id, version, Change.Upsert(translation));
            return await Commander.Call(cmd, true, cancellationToken).ConfigureAwait(false);
        }
        catch (VersionMismatchException) {
            // Ignore version mismatch if already translated
            return await Get(translation.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    // Nested types

    private record TranslationResult(string OriginalText, string TranslatedText);
}
