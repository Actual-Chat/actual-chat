using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Diagnostics;
using ActualChat.Flows;
using ActualChat.Queues;
using ActualChat.Streaming;
using ActualChat.Transcription;
using ActualLab.Diagnostics;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Rpc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Chat;

/// <summary>
/// Backend service implementation for real-time chat entry translation using AI.
/// </summary>
public class TranslationsBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), ITranslationsBackend
{
    private static readonly TimeSpan TranslateThrottleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan EntryFinalizationTimeout = Constants.Transcription.RetranscriptionTimeout + TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<StreamId, FuncWorker> _activePublishers = new();

    private ChatSettings Settings => field ??= Services.GetRequiredService<ChatSettings>();
    private IDbEntityResolver<string, DbTranslation> EntityResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbTranslation>>();
    private IDbEntityResolver<string, DbChatEntryLanguage> LanguageEntityResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbChatEntryLanguage>>();
    private Translator Translator => field ??= Services.GetRequiredService<Translator>();
    private Translator RealtimeTranslator => field ??= Services.GetRequiredKeyedService<Translator>(Constants.Translation.RealtimeServiceKey);
    private Translator UITextTranslator => field ??= Services.GetRequiredKeyedService<Translator>(Constants.Translation.UITextServiceKey);
    private DiffEngine DiffEngine => field ??= Services.GetRequiredService<DiffEngine>();
    private IQueues Queues => field ??= Services.Queues();
    private MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IAudioStreamingBackend StreamingBackend => field ??= Services.GetRequiredService<IAudioStreamingBackend>();
    private IConversationsBackend ConversationsBackend => field ??= Services.GetRequiredService<IConversationsBackend>();
    private IHostApplicationLifetime HostLifetime => field ??= Services.HostLifetime();
    private FlowHub FlowHub => field ??= Services.FlowHub();

    private static bool DebugMode => Constants.DebugMode.TranslationBackend;
    private ILogger? DebugLog => DebugMode ? Log : null;

    // [ComputeMethod]
    public virtual async Task<Translation?> Get(TranslationId id, bool translateIfMissing, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return null;

        var (translationSource, translation) = await GetExisting(id, cancellationToken).ConfigureAwait(false);
        if (!translateIfMissing || !translationSource.NeedsTranslation(translation))
            return translation;

        // we only try to enqueue and fast return to allow compute method to cache current result
        var cmd = new TranslationsBackend_Translate(id.SourceId, id.Language, false, false);
        await Queues.Enqueue(cmd, cancellationToken).ConfigureAwait(false);
        return translation;
    }

    // [ComputeMethod]
    public virtual async Task<string?> GetTranslatedUIText(
        string text,
        Language language,
        UITextKind kind,
        CancellationToken cancellationToken)
    {
        // The translation runs inline; the Fusion compute cache (this method is sharded by text, so each
        // distinct string is owned by a single node) dedups it globally at runtime. No persistence: a
        // restart re-translates, which is cheap for this small set of short strings.
        if (text.IsNullOrWhiteSpace() || language.IsAnyEnglish)
            return null;

        if (!Settings.IsTranslationEnabled)
            return null;
        if (text.Length > Constants.Translation.MaxTextTranslationLength)
            return null;

        AppMeters.UITextCatalogMissCount.Add(1,
            new KeyValuePair<string, object?>("language", language.IsoCode),
            new KeyValuePair<string, object?>("kind", kind.ToString()));
        Log.LogWarning("No catalog entry for {Kind} in '{Language}': {Text}", kind, language.IsoCode, text);

        var contextHint = GetUITextTranslationHint(kind);
        var translated = await UITextTranslator
            .Translate(text, language, [], contextHint, cancellationToken)
            .ConfigureAwait(false);
        return translated.NullIfEmpty();
    }

    // Not a [ComputeMethod]!
    public virtual async Task<ApiArray<Translation>> ListHanging(ThisNodeRef nodeRef, int limit, CancellationToken cancellationToken)
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

        if (dbTranslation.StreamId is not null && !change.IsRemove())
            await FlowHub
                .NewResumeEvent<TranslationCleanupFlow>()
                .WithDelay(now + Settings.Translation.HangingTimeout, TimeSpan.FromMinutes(1))
                .Schedule(cancellationToken)
                .ConfigureAwait(false);
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

        // Check if source language matches target language - if so, return original content
        if (await IsSourceLanguageMatchesTarget(translationSource, targetLanguage, cancellationToken).ConfigureAwait(false))
            return await SaveTranslationAsOriginal().ConfigureAwait(false);

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
                GetTranslationContextHint(id.Kind),
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
            using var stream = Translator
                .Stream(
                    translationSource.Content,
                    id.Language,
                    context,
                    GetTranslationContextHint(id.Kind),
                    cancellationToken)
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
            if (id.Kind is not TranslationIdKind.ChatEntry)
                return [];

            var chatEntryId = id.SourceId.GetChatEntryId();
            return await GetTranslationContext(chatEntryId, targetLanguage, cancellationToken).ConfigureAwait(false);
        }

        async Task<Translation?> SaveTranslationAsOriginal()
        {
            var contentHash = translationSource.ContentHash.IsNone
                ? ChatEntryHashExt.GetContentHashString(translationSource.Content)
                : translationSource.ContentHash;
            var translationDiff = new TranslationDiff {
                StreamId = null,
                Content = translationSource.Content,
                SourceContentHash = contentHash,
            };
            var change = translation is null
                ? Change.Create(translationDiff)
                : Change.Update(translationDiff);
            var version = ignoreVersion ? null : translation?.Version;
            var cmd = new TranslationsBackend_Change(id, version, change);
            return await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
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
        DebugLog?.LogDebug("OnTranslateStream: #{StreamId} -> {Language}", streamId, targetLanguage);

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
            .Select((ChatEntry e, CancellationToken ct) => SelectTranslationAsync(e, language, ct))
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
                var foundEntries = await ChatsBackend.ListEntries(id.ChatId, idRange, false, cancellationToken).ConfigureAwait(false);
                foreach (var entry in foundEntries.Where(e => e.LocalId < maxLidExclusive))
                    yield return entry;
            }
        }
    }

    protected virtual async Task<bool> IsSourceLanguageMatchesTarget(
        TranslationSource? source,
        Language targetLanguage,
        CancellationToken cancellationToken)
    {
        if (source is not TextEntryTranslationSource textEntrySource)
            return false;

        var entryId = textEntrySource.ChatEntry.Id;
        var dbEntryLanguage = await LanguageEntityResolver.Get(entryId.Value, cancellationToken).ConfigureAwait(false);
        if (dbEntryLanguage is null)
            return false;

        // Only check if language has already been detected - don't trigger detection here
        var detectedLanguages = dbEntryLanguage.ToModel().Languages;
        if (detectedLanguages.Length == 0)
            return false;

        return detectedLanguages.Any(lang => IsLanguageMatch(lang, targetLanguage));

        static bool IsLanguageMatch(Language source, Language target)
        {
            // Direct match
            if (source == target)
                return true;
            // Match English variants (en-US, en-GB, en-IN) to any English target
            if (source.IsAnyEnglish && target.IsAnyEnglish)
                return true;
            // Match Spanish variants (es-ES, es-MX, es-US) to any Spanish target
            if (source.IsAnySpanish && target.IsAnySpanish)
                return true;
            return false;
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
        var translatedStreamId = StreamId.New(streamId, targetLanguage);
        {
            // ReSharper disable PossiblyMistakenUseOfCancellationToken
            var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            // This query will be executed once - no need to wrap in a compute method
            var chatEntrySid = await dbContext.ChatEntries
                .Where(e => e.Kind == 0 && e.ContentStreamId == streamId.Value)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (chatEntrySid == null)
                return null; // Already transcribed

            sourceId = TranslationSourceId.New(ChatEntryId.Parse(chatEntrySid));
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

 #pragma warning disable CA2016 // Pass cancellationToken
        var stopTokenSource = HostLifetime.CreateStopTokenSource();
 #pragma warning restore CA2016
        var stopToken = stopTokenSource.Token;
        // A lost source stream must stay an error: suppressing it here made a truncated transcript
        // look like a finished one, so TranslateTranscriptStream persisted a mid-sentence
        // translation as final and ended the client's stream normally, with nothing logged.
        var transcriptStream = transcript.Memoize(stopToken);

        var worker = _activePublishers.GetOrAdd(translatedStreamId,
            static (_, state) => {
                return FuncWorker.New(
                    static (arg, ct) => arg.self.TranslateTranscriptStream(
                        arg.transcriptStream,
                        arg.translatedStreamId,
                        arg.translationId,
                        arg.newTranslationVersion,
                        ct),
                    state,
                    state.stopTokenSource);
            },
            (self: this, transcriptStream, translatedStreamId, translationId, newTranslationVersion, stopTokenSource));

        DebugLog?.LogDebug("StartTranscriptStreamTranslation: #{StreamId} -> {Language}", streamId, targetLanguage);
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
        DebugLog?.LogDebug("TranslateTranscriptStream: #{StreamId}", translatedStreamId);

        var channel = Channel.CreateUnbounded<TranscriptDiff>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });
        using var activity = CoreServerInstruments.ActivitySource.StartActivity(GetType(), activityKind: ActivityKind.Client);
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
                                DebugLog?.LogDebug("TranslateTranscriptStream: #{StreamId} - First Transcript",
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

                                if (text == lastText)
                                    return; // No need to translate the same text (it's already been translated')

                                var context = new List<TranslationResult>();
                                if (stableTranscript.Text != stableTranslatedTranscript.Text)
                                    context.Add(new TranslationResult(stableTranscript.Text,
                                        stableTranslatedTranscript.Text));
                                var translatedText = await RealtimeTranslator.Translate(
                                        text,
                                        language,
                                        context.ToArray(),
                                        cancellationToken: cancellationToken)
                                    .ConfigureAwait(false);
                                if (string.Equals(translatedText, Constants.Translation.NoTranslationNeededText, StringComparison.OrdinalIgnoreCase))
                                    translatedText = text; // No translation needed, use original content
                                if (!translatedText.StartsWith(' ')
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
                            // The realtime transcript stream ends well before re-transcription
                            // finalizes the entry, so the entry is still content-streaming here.
                            // Re-translating now would be dropped by NeedsTranslation, leaving the
                            // realtime translation in place. Wait for the entry to finalize before
                            // enqueueing the re-translation of the full finalized transcript.
                            var sourceId = translationId.SourceId;
                            var targetLanguage = translationId.Language;
                            if (sourceId.Kind is TranslationIdKind.ChatEntry)
                                await WhenEntryFinalized(sourceId.GetChatEntryId(), cancellationToken).ConfigureAwait(false);
                            // StreamId will be cleaned up by this command
                            var cmd = new TranslationsBackend_Translate(
                                sourceId, targetLanguage,
                                OverwriteIfVersionMismatch: true,
                                SkipRealtimeTranslation: true);
                            await Queues.Enqueue(cmd, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex2) {
                            if (error == null)
                                error = ex2; // Preserve the original error if it exists
                            else
                                Log.LogError(ex2, "Error while finalizing translation #{StreamId}", translatedStreamId);
                        }
                        finally {
                            if (error != null)
                                Log.LogError(error,
                                    "Error while translating transcript #{StreamId}",
                                    translatedStreamId);
                            writer.Complete(error);
                        }
                    }
                },
                cancellationToken);

            DebugLog?.LogDebug("TranslateTranscriptStream: #{StreamId} - Publishing stream", translatedStreamId);

            var translatedStream = RpcStream.New(reader.ReadAllAsync(cancellationToken));
            await StreamingBackend
                .PushTranscript(translatedStreamId, translatedStream, cancellationToken)
                .ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            DebugLog?.LogDebug("TranslateTranscriptStream: #{StreamId} - Stream published", translatedStreamId);
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
            case TranslationIdKind.ChatEntry: {
                var chatEntryId = sourceId.GetChatEntryId();
                var entry = await ChatsBackend.GetEntry(chatEntryId, cancellationToken).ConfigureAwait(false);
                if (entry is null)
                    return null;

                return new TextEntryTranslationSource(entry, sourceId);
            }
            case TranslationIdKind.ConversationTitle or
                TranslationIdKind.ConversationDescription or
                TranslationIdKind.ConversationSummary: {
                var lid = sourceId.RefLid;
                var conversationId = ConversationId.New(sourceId.ChatId, lid);
                var conversation = await ConversationsBackend.Get(conversationId, cancellationToken).ConfigureAwait(false);
                if (conversation is null)
                    return null;

                return new ConversationTranslationSource(conversation, sourceId);
            }
            case TranslationIdKind.ThreadTitle or
                TranslationIdKind.ThreadDescription : {
                var lid = sourceId.RefLid;
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

    private static string? GetTranslationContextHint(TranslationIdKind kind)
        // Titles/descriptions/summaries are short standalone texts with no sibling-message context,
        // so the hint is the only signal distinguishing them from a chat message
        => kind switch {
            TranslationIdKind.ConversationTitle or TranslationIdKind.ThreadTitle
                => "The input is a title of a conversation in a chat app. "
                    + "Translate it as a concise title; do not add punctuation.",
            TranslationIdKind.ConversationDescription or TranslationIdKind.ThreadDescription
                => "The input is a short description of a conversation in a chat app.",
            TranslationIdKind.ConversationSummary
                => "The input is a brief summary of a conversation in a chat app.",
            _ => null,
        };

    // The translation rules live in the UI-text prompt file; the hint only names the kind
    private static string GetUITextTranslationHint(UITextKind kind)
        => "The input is a user-facing error or status message.";

    private async ValueTask<(ChatEntry e, Translation?)> SelectTranslationAsync(ChatEntry e, Language language, CancellationToken cancellationToken)
        => (e, await GetInternal(TranslationId.New(ChatEntryId.New(e.ChatId, e.LocalId), language), cancellationToken).ConfigureAwait(false));

    private async Task<(TranslationSource? source, Translation? translation)> GetExisting(TranslationId id, CancellationToken cancellationToken)
    {
        var source = await TryResolveTranslationSource(id, cancellationToken).ConfigureAwait(false);
        if (source is null)
            return (null, null);

        var translation = await GetInternal(id, cancellationToken).ConfigureAwait(false);
        return (source, translation);
    }

    private async Task WhenEntryFinalized(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        var idTile = Constants.Chat.ServerIdTileStack.FirstLayer.GetTile(entryId.LocalId);
        var cTile = await Computed
            .Capture(
                () => ChatsBackend.GetTile(entryId.ChatId, idTile.Range, includeRemoved: false, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        try {
            // A removed entry (empty realtime transcript) never appears in the non-removed tile,
            // so a missing entry counts as finalized.
            await cTile
                .When(t => {
                    var entry = t.Entries.SingleOrDefault(e => e.LocalId == entryId.LocalId);
                    return entry is null || !entry.IsContentStreaming;
                }, cancellationToken)
                .WaitAsync(EntryFinalizationTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            // Don't propagate: re-translation is best-effort and would be dropped anyway if not finalized.
            Log.LogWarning(
                "WhenEntryFinalized: entry #{EntryId} didn't finalize within {Timeout}",
                entryId, EntryFinalizationTimeout);
        }
    }
}
