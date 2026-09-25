using ActualChat.Chat.Db;
using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Queues;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Coach;

/// <summary>
/// Analyses a user's own voice entries (metrics + LLM spans) and, once a run of entries has gone
/// quiet, their turn-taking in it; every row write emits the matching event to the user shard.
/// </summary>
public class CoachAnalysisBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), ICoachAnalysisBackend
{
    // A run is scanned in windows this many lids wide; a run longer than MaxRunEntries is cut there.
    private const long ScanWindow = 50;
    private const int MaxRunEntries = 400;

    private ChatSettings Settings { get; } = services.GetRequiredService<ChatSettings>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IChatEntryLanguagesBackend LanguagesBackend
        => field ??= Services.GetRequiredService<IChatEntryLanguagesBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private ISpeechTagger Tagger => field ??= Services.GetRequiredService<ISpeechTagger>();
    private IQueues Queues => field ??= Services.Queues();
    private IDbEntityResolver<string, DbCoachEntry> EntryResolver
        => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbCoachEntry>>();

    // [ComputeMethod]
    public virtual async Task<CoachEntryAnalysis?> Get(ChatEntryId id, CancellationToken cancellationToken)
    {
        var dbEntry = await EntryResolver.Get(id.Value, cancellationToken).ConfigureAwait(false);
        return dbEntry?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<CoachConversationAnalysis?> GetConversation(
        ConversationId id, AuthorId authorId, CancellationToken cancellationToken)
    {
        var rowId = DbCoachConversation.ComposeId(id, authorId);
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbRow = await dbContext.CoachConversations
            .FirstOrDefaultAsync(x => x.Id == rowId, cancellationToken)
            .ConfigureAwait(false);
        return dbRow?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachEntryMarks>> ListMarks(
        ChatId chatId, AuthorId authorId, Range<long> lidTileRange, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var rows = await dbContext.CoachEntries
            .Where(x => x.ChatId == chatId.Value && x.AuthorId == authorId.Value
                && x.LocalId >= lidTileRange.Start && x.LocalId < lidTileRange.End && x.Spans != "[]")
            .OrderBy(x => x.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => r.ToModel()).Select(m => new CoachEntryMarks(m.Id.LocalId, m.Spans)).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task OnAnalyzeEntry(
        CoachAnalysisBackend_AnalyzeEntry command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var written = context.Operation.Items.KeylessGet<CoachEntryAnalysis?>();
            if (written is not null)
                InvalidateEntry(written);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        var dbEntry = await dbContext.CoachEntries.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);

        if (command.IsRemoved) {
            if (dbEntry is null)
                return;

            var removed = dbEntry.ToModel();
            dbContext.Remove(dbEntry);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            context.Operation.Items.KeylessSet(removed);
            context.Operation.AddEvent(new CoachEntryAnalyzedEvent(removed, true));
            return;
        }

        var entry = await ChatsBackend.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (entry is null || !IsAnalyzable(entry))
            return;

        var existing = dbEntry?.ToModel();
        var isUnchanged = existing is not null && existing.ContentHash == entry.ContentHash;
        if (isUnchanged && existing!.TagState != CoachTagState.Pending)
            return;

        var author = await AuthorsBackend
            .Get(id.ChatId, entry.AuthorId, RequestedAuthorKind.Default, cancellationToken)
            .ConfigureAwait(false);
        if (author is null || author.UserId.IsGuestOrNull() || author.IsAnonymous == true)
            return;

        var language = await GetLanguage(id, author.UserId, cancellationToken).ConfigureAwait(false);
        var analysis = Analyze(entry, author.UserId, language, existing);
        var settings = await ServerKvasBackend.ForUser(author.UserId)
            .UserCoachSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        if (settings.IsCoachingEnabled)
            analysis = await ApplyTags(analysis, entry.Content, cancellationToken).ConfigureAwait(false);
        if (isUnchanged && analysis.TagState == existing!.TagState)
            return;

        analysis = analysis with { Version = VersionGenerator.NextVersion(dbEntry?.Version ?? 0) };
        if (dbEntry is null)
            dbContext.Add(new DbCoachEntry(analysis));
        else
            dbEntry.UpdateFrom(analysis);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(analysis);
        context.Operation.AddEvent(new CoachEntryAnalyzedEvent(analysis, false));
    }

    // [CommandHandler]
    public virtual async Task OnAnalyzeConversation(
        CoachAnalysisBackend_AnalyzeConversation command, CancellationToken cancellationToken)
    {
        var (chatId, entryLid) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var touched = context.Operation.Items.KeylessGet<TouchedRun?>();
            if (touched is null)
                return;

            foreach (var authorId in touched.AuthorIds)
                _ = GetConversation(touched.Id, authorId, default);
            foreach (var tagged in touched.TaggedEntries)
                InvalidateEntry(tagged);
            return;
        }

        var now = Clocks.SystemClock.Now;
        var run = await FindRun(chatId, entryLid, cancellationToken).ConfigureAwait(false);
        if (run.Count == 0)
            return;

        var maturity = Settings.Coach.ConversationMaturity;
        var quietSince = run.Max(e => e.EndsAt ?? e.BeginsAt);
        var isStillActive = now - quietSince < maturity;
        var waitedLongEnough = now - command.DelayUntil >= Settings.Coach.MaxConversationWait;
        if (isStillActive && !waitedLongEnough)
            throw StandardError.Postpone(maturity - (now - quietSince));

        var conversationId = ConversationId.New(chatId, run[0].LocalId);
        var runVersion = run[^1].LocalId;
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        var touchedRun = new TouchedRun(conversationId);

        foreach (var authorId in run.Where(IsAnalyzable).Select(e => e.AuthorId).Distinct()) {
            var author = await AuthorsBackend
                .Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (author is null || author.UserId.IsGuestOrNull() || author.IsAnonymous == true)
                continue;

            await TagPendingEntries(dbContext, run, authorId, touchedRun, context, cancellationToken)
                .ConfigureAwait(false);

            var rowId = DbCoachConversation.ComposeId(conversationId, authorId);
            var dbRow = await dbContext.CoachConversations.ForUpdate()
                .FirstOrDefaultAsync(x => x.Id == rowId, cancellationToken)
                .ConfigureAwait(false);
            if (dbRow is not null && dbRow.ConversationVersion == runVersion)
                continue;

            var stats = ConversationStats.Compute(run, authorId, Settings.Coach.MaxResponseGapSeconds);
            if (stats is null)
                continue;

            var version = VersionGenerator.NextVersion(dbRow?.Version ?? 0);
            var model = new CoachConversationAnalysis(conversationId, authorId, version) {
                UserId = author.UserId,
                ConversationVersion = runVersion,
                EndsAt = quietSince,
                OwnSpeechSeconds = stats.OwnSpeechSeconds,
                TotalSpeechSeconds = stats.TotalSpeechSeconds,
                OwnTurns = stats.OwnTurns,
                TotalTurns = stats.TotalTurns,
                Participants = stats.Participants,
                LongestMonologueSeconds = stats.LongestMonologueSeconds,
                Responses = stats.Responses,
                ResponseGapSeconds = stats.ResponseGapSeconds,
                Interruptions = stats.Interruptions,
            };
            if (dbRow is null)
                dbContext.Add(new DbCoachConversation(model));
            else
                dbRow.UpdateFrom(model);
            touchedRun.AuthorIds.Add(authorId);
            context.Operation.AddEvent(new CoachConversationAnalyzedEvent(model));
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(touchedRun);
    }

    // [EventHandler]
    public virtual async Task OnChatEntryChangedEvent(
        ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive || !Settings.Coach.IsEnabled)
            return;

        var (entry, author, changeKind, oldEntry) = eventCommand;
        if (entry is not TextEntry || entry.IsSystemEntry || author.UserId.IsGuestOrNull())
            return;

        if (changeKind == ChangeKind.Remove || entry.IsRemoved) {
            await Queues.Enqueue(new CoachAnalysisBackend_AnalyzeEntry(entry.Id, true), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var usageEvent = UsageEventSource.FromEntryChange(entry, oldEntry, changeKind);
        var isFinalized = usageEvent is { Kind: UsageEventKind.Speech };
        var isEdited = changeKind == ChangeKind.Update
            && oldEntry is not null
            && oldEntry.ContentHash != entry.ContentHash
            && entry is { HasAudio: true, IsContentStreaming: false, EndsAt: not null };
        if (!isFinalized && !isEdited)
            return;

        await Queues.Enqueue(new CoachAnalysisBackend_AnalyzeEntry(entry.Id, false), cancellationToken)
            .ConfigureAwait(false);
        if (!isFinalized)
            return;

        var delayUntil = (entry.EndsAt ?? Clocks.SystemClock.Now) + Settings.Coach.ConversationMaturity;
        var analyzeRun = new CoachAnalysisBackend_AnalyzeConversation(entry.ChatId, entry.LocalId) {
            DelayUntil = delayUntil,
        };
        await Queues.Enqueue(analyzeRun, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static bool IsAnalyzable(ChatEntry entry)
        => entry is {
                HasAudio: true,
                IsRemoved: false,
                IsSystemEntry: false,
                IsContentStreaming: false,
                EndsAt: not null,
            }
            && !entry.Content.IsNullOrWhiteSpace();

    private void InvalidateEntry(CoachEntryAnalysis analysis)
    {
        _ = Get(analysis.Id, default);
        var tile = Constants.Chat.EntryIdTiles.GetTile(analysis.Id.LocalId);
        _ = ListMarks(analysis.Id.ChatId, analysis.AuthorId, tile.Range, default);
    }

    private CoachEntryAnalysis Analyze(ChatEntry entry, UserId userId, Language? language, CoachEntryAnalysis? existing)
    {
        var duration = (entry.EndsAt!.Value - entry.BeginsAt).TotalSeconds;
        var markup = new PlayableTextMarkup(entry.Content, entry.Audio?.TimeMap ?? LinearMap.Zero);
        var isSplittable = SpeechTextStats.IsWordSplittable(language);
        var text = isSplittable ? SpeechTextStats.Compute(markup) : null;
        var timing = isSplittable ? SpeechTimingStats.Compute(markup, duration, Settings.Coach.MinPauseSeconds) : null;
        var isUnchanged = existing is not null && existing.ContentHash == entry.ContentHash;
        return new CoachEntryAnalysis(entry.Id, existing?.Version ?? 0) {
            AuthorId = entry.AuthorId,
            UserId = userId,
            BeginsAt = entry.BeginsAt,
            Language = language,
            DurationSeconds = duration,
            SpeechSeconds = timing?.SpeechSeconds,
            Words = text?.Words,
            Sentences = text?.Sentences,
            Questions = text?.Questions,
            Repetitions = text?.Repetitions,
            DistinctWords = text?.DistinctWords,
            Pauses = timing?.Pauses,
            PauseSeconds = timing?.PauseSeconds,
            Spans = isUnchanged ? existing!.Spans : text?.RepetitionSpans ?? ApiArray<SpeechSpan>.Empty,
            FilledPauses = isUnchanged ? existing!.FilledPauses : 0,
            Fillers = isUnchanged ? existing!.Fillers : 0,
            WeakWords = isUnchanged ? existing!.WeakWords : 0,
            Profanities = isUnchanged ? existing!.Profanities : 0,
            TagState = isUnchanged ? existing!.TagState : CoachTagState.Pending,
            PromptVersion = isUnchanged ? existing!.PromptVersion : 0,
            TaggedAt = isUnchanged ? existing!.TaggedAt : null,
            ContentHash = entry.ContentHash,
        };
    }

    private async Task<CoachEntryAnalysis> ApplyTags(
        CoachEntryAnalysis analysis, string text, CancellationToken cancellationToken)
    {
        if (analysis.TagState == CoachTagState.Tagged && analysis.PromptVersion >= Settings.Coach.PromptVersion)
            return analysis;

        var result = await Tagger.Tag(new SpeechTagRequest(text, analysis.Language), cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
            return analysis;

        var repetitions = analysis.Spans.Where(s => s.Kind == SpeechSpanKind.Repetition);
        var spans = result.Spans.Concat(repetitions).OrderBy(s => s.Start).ToApiArray();
        return analysis with {
            Spans = spans,
            FilledPauses = spans.Count(s => s.Kind == SpeechSpanKind.FilledPause),
            Fillers = spans.Count(s => s.Kind == SpeechSpanKind.Filler),
            WeakWords = spans.Count(s => s.Kind == SpeechSpanKind.Weak),
            Profanities = spans.Count(s => s.Kind == SpeechSpanKind.Profanity),
            TagState = CoachTagState.Tagged,
            PromptVersion = result.PromptVersion,
            TaggedAt = Clocks.SystemClock.Now,
        };
    }

    private async Task TagPendingEntries(
        ChatDbContext dbContext,
        List<ChatEntry> run,
        AuthorId authorId,
        TouchedRun touched,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        var own = run.Where(e => e.AuthorId == authorId && IsAnalyzable(e)).ToDictionary(e => e.Id.Value);
        var ownIds = own.Keys.ToList();
        var promptVersion = Settings.Coach.PromptVersion;
        var pending = await dbContext.CoachEntries.ForUpdate()
            .Where(x => ownIds.Contains(x.Id))
            .Where(x => x.TagState == CoachTagState.Pending || x.PromptVersion < promptVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var dbEntry in pending) {
            var entry = own[dbEntry.Id];
            var analysis = await ApplyTags(dbEntry.ToModel(), entry.Content, cancellationToken).ConfigureAwait(false);
            if (analysis.TagState != CoachTagState.Tagged)
                continue;

            analysis = analysis with { Version = VersionGenerator.NextVersion(dbEntry.Version) };
            dbEntry.UpdateFrom(analysis);
            touched.TaggedEntries.Add(analysis);
            context.Operation.AddEvent(new CoachEntryAnalyzedEvent(analysis, false));
        }
    }

    // The coaching "conversation" is a run of entries with no quiet gap of ConversationMaturity
    // between neighbours; the summarizer's conversations exist only for long threads, so they
    // cannot be the unit here.
    private async Task<List<ChatEntry>> FindRun(ChatId chatId, long entryLid, CancellationToken cancellationToken)
    {
        var maturity = Settings.Coach.ConversationMaturity;
        var lidRange = await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
        if (entryLid < lidRange.Start || entryLid >= lidRange.End)
            return [];

        var run = new LinkedList<ChatEntry>();
        var anchor = await ReadWindow(chatId, entryLid, entryLid + 1, cancellationToken).ConfigureAwait(false);
        if (anchor.Count == 0)
            return [];

        run.AddFirst(anchor[0]);
        var windowStart = entryLid;
        while (run.Count < MaxRunEntries && windowStart > lidRange.Start) {
            var from = Math.Max(lidRange.Start, windowStart - ScanWindow);
            var window = await ReadWindow(chatId, from, windowStart, cancellationToken).ConfigureAwait(false);
            var isClosed = false;
            for (var i = window.Count - 1; i >= 0; i--) {
                var candidate = window[i];
                if (run.First!.Value.BeginsAt - (candidate.EndsAt ?? candidate.BeginsAt) >= maturity) {
                    isClosed = true;
                    break;
                }
                run.AddFirst(candidate);
            }
            if (isClosed)
                break;

            windowStart = from;
        }

        var windowEnd = entryLid + 1;
        while (run.Count < MaxRunEntries && windowEnd < lidRange.End) {
            var to = Math.Min(lidRange.End, windowEnd + ScanWindow);
            var window = await ReadWindow(chatId, windowEnd, to, cancellationToken).ConfigureAwait(false);
            var isClosed = false;
            foreach (var candidate in window) {
                var last = run.Last!.Value;
                if (candidate.BeginsAt - (last.EndsAt ?? last.BeginsAt) >= maturity) {
                    isClosed = true;
                    break;
                }
                run.AddLast(candidate);
            }
            if (isClosed)
                break;

            windowEnd = to;
        }
        return run.ToList();
    }

    private async Task<List<ChatEntry>> ReadWindow(
        ChatId chatId, long from, long to, CancellationToken cancellationToken)
    {
        var entries = await ChatsBackend
            .ListEntries(chatId, new Range<long>(from, to), false, cancellationToken)
            .ConfigureAwait(false);
        return entries
            .Where(e => e.LocalId >= from && e.LocalId < to && e is TextEntry && !e.IsSystemEntry && !e.IsRemoved)
            .OrderBy(e => e.LocalId)
            .ToList();
    }

    // The entry's detected language, else the language the user records in: an entry has no
    // language row while translation is off or before detection lands.
    private async Task<Language?> GetLanguage(ChatEntryId id, UserId userId, CancellationToken cancellationToken)
    {
        var tile = await LanguagesBackend
            .GetTile(id.ChatId, Constants.Chat.EntryIdTiles.GetTile(id.LocalId).Range, cancellationToken)
            .ConfigureAwait(false);
        var language = tile.Entries.FirstOrDefault(e => e.Id == id)?.Languages.FirstOrDefault();
        if (language is not null)
            return language;

        var languageSettings = await ServerKvasBackend.ForUser(userId)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return languageSettings.Primary;
    }

    // Nested types

    private sealed class TouchedRun(ConversationId id)
    {
        public ConversationId Id { get; } = id;
        public List<AuthorId> AuthorIds { get; } = [];
        public List<CoachEntryAnalysis> TaggedEntries { get; } = [];
    }
}
