using ActualChat.Chat.Db;
using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Hashing;
using ActualChat.Queues;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Coach;

/// <summary>
/// Analyses a user's own voice entries (metrics + LLM spans) and, once a run of entries has gone
/// quiet, their turn-taking in it; every row write emits the matching event to the user shard.
/// The LLM is always called outside the operation transaction, which only re-checks and stores.
/// </summary>
public class CoachAnalysisBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), ICoachAnalysisBackend
{
    // A run is scanned in windows this many lids wide, at most MaxScanLids back from the anchor
    private const long ScanWindow = 50;
    private const long MaxScanLids = 5000;

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
        var tile = Constants.Chat.EntryIdTiles.GetTile(lidTileRange);
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var rows = await dbContext.CoachEntries
            .Where(x => x.ChatId == chatId.Value && x.AuthorId == authorId.Value
                && x.LocalId >= tile.Range.Start && x.LocalId < tile.Range.End && x.Spans != "[]")
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
                InvalidateEntry(written.Id, written.AuthorId);
            return;
        }
        if (!Settings.Coach.IsEnabled)
            return;

        var existing = await Get(id, cancellationToken).ConfigureAwait(false);
        var entry = command.IsRemoved
            ? null
            : await ChatsBackend.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (entry is null || !IsAnalyzable(entry)) {
            if (existing is not null)
                await RemoveEntry(id, context, cancellationToken).ConfigureAwait(false);
            return;
        }

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

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        var dbEntry = await dbContext.CoachEntries.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        var current = await ChatsBackend.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (current is null || !IsStillWorthWriting(dbEntry, analysis, current.ContentHash))
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
            var touch = context.Operation.Items.KeylessGet<CoachRunTouch?>();
            if (touch is null)
                return;

            foreach (var authorId in touch.AuthorIds)
                _ = GetConversation(touch.Id, authorId, default);
            foreach (var taggedEntry in touch.TaggedEntries)
                InvalidateEntry(taggedEntry.Id, taggedEntry.AuthorId);
            return;
        }
        if (!Settings.Coach.IsEnabled)
            return;

        var now = Clocks.SystemClock.Now;
        var (firstLid, run) = await FindRun(chatId, entryLid, cancellationToken).ConfigureAwait(false);
        if (run.Count == 0)
            return;

        var maturity = Settings.Coach.ConversationMaturity;
        var quietSince = run.Max(e => e.EndsAt ?? e.BeginsAt);
        var isStillActive = now - quietSince < maturity;
        var waitedLongEnough = now - command.DelayUntil >= Settings.Coach.MaxConversationWait;
        if (isStillActive && !waitedLongEnough)
            throw StandardError.Postpone(maturity - (now - quietSince));

        var conversationId = ConversationId.New(chatId, firstLid);
        var runVersion = run[^1].LocalId;
        var authors = new List<(AuthorId Id, UserId UserId)>();
        foreach (var authorId in run.Where(IsAnalyzable).Select(e => e.AuthorId).Distinct()) {
            var author = await AuthorsBackend
                .Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (author is not null && !author.UserId.IsGuestOrNull() && author.IsAnonymous != true)
                authors.Add((authorId, author.UserId));
        }
        var tagged = await TagPendingEntries(run, authors, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        var touchedAuthors = new List<AuthorId>();
        var touchedEntries = new List<CoachTaggedEntry>();

        var runEntries = run.ToDictionary(e => e.Id);
        foreach (var analysis in tagged) {
            var dbEntry = await dbContext.CoachEntries.ForUpdate()
                .FirstOrDefaultAsync(x => x.Id == analysis.Id.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbEntry is null || !IsStillWorthWriting(dbEntry, analysis, runEntries[analysis.Id].ContentHash))
                continue;

            var stored = analysis with { Version = VersionGenerator.NextVersion(dbEntry.Version) };
            dbEntry.UpdateFrom(stored);
            touchedEntries.Add(new CoachTaggedEntry(stored.Id, stored.AuthorId));
            context.Operation.AddEvent(new CoachEntryAnalyzedEvent(stored, false));
        }

        foreach (var (authorId, userId) in authors) {
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
                UserId = userId,
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
            touchedAuthors.Add(authorId);
            context.Operation.AddEvent(new CoachConversationAnalyzedEvent(model));
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(
            new CoachRunTouch(conversationId, touchedAuthors.ToApiArray(), touchedEntries.ToApiArray()));
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
            if (entry.HasAudio)
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
        // An edit after the run went quiet must also re-tag a non-opted-in user's row; its delay counts
        // from now so the run command finds the row already reset by the entry command
        var anchor = isFinalized ? entry.EndsAt ?? Clocks.SystemClock.Now : Clocks.SystemClock.Now;
        var delayUntil = anchor + Settings.Coach.ConversationMaturity;
        var analyzeRun = new CoachAnalysisBackend_AnalyzeConversation(entry.ChatId, entry.LocalId) {
            DelayUntil = delayUntil,
            Salt = isFinalized ? "" : entry.ContentHash.Value,
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

    // The entry may have been edited while the LLM was running (then the analysis is stale), or a
    // concurrent command may already have stored the same tags
    private static bool IsStillWorthWriting(
        DbCoachEntry? dbEntry, CoachEntryAnalysis analysis, HashString currentContentHash)
    {
        if (analysis.ContentHash != currentContentHash)
            return false;
        if (dbEntry is null || dbEntry.ContentHash != analysis.ContentHash.Value)
            return true;

        // Same text: only newer tags are worth writing; a pending re-analysis of identical text
        // carries nothing and must not downgrade a row a concurrent command just tagged
        return analysis.TagState == CoachTagState.Tagged
            && (dbEntry.TagState != CoachTagState.Tagged || dbEntry.PromptVersion < analysis.PromptVersion);
    }

    private void InvalidateEntry(ChatEntryId id, AuthorId authorId)
    {
        _ = Get(id, default);
        var tile = Constants.Chat.EntryIdTiles.GetTile(id.LocalId);
        _ = ListMarks(id.ChatId, authorId, tile.Range, default);
    }

    private async Task RemoveEntry(ChatEntryId id, CommandContext context, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbEntry = await dbContext.CoachEntries.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbEntry is null)
            return;

        var removed = dbEntry.ToModel();
        dbContext.Remove(dbEntry);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(removed);
        context.Operation.AddEvent(new CoachEntryAnalyzedEvent(removed, true));
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

    // Reads the rows without locks and tags them outside any transaction; the caller re-checks each
    // row under a lock before storing. A row whose text hash is stale (the entry was edited and the
    // entry command has not run yet) is re-analysed here rather than waited for.
    private async Task<List<CoachEntryAnalysis>> TagPendingEntries(
        List<ChatEntry> run, List<(AuthorId Id, UserId UserId)> authors, CancellationToken cancellationToken)
    {
        var userIds = authors.ToDictionary(a => a.Id, a => a.UserId);
        var own = run
            .Where(e => IsAnalyzable(e) && userIds.ContainsKey(e.AuthorId))
            .ToDictionary(e => e.Id.Value);
        var ownIds = own.Keys.ToList();
        List<DbCoachEntry> rows;
        {
            var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var _ = dbContext.ConfigureAwait(false);
            rows = await dbContext.CoachEntries
                .Where(x => ownIds.Contains(x.Id))
                .OrderBy(x => x.LocalId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var promptVersion = Settings.Coach.PromptVersion;
        var tagged = new List<CoachEntryAnalysis>();
        foreach (var dbEntry in rows) {
            if (tagged.Count >= Settings.Coach.MaxTaggerCallsPerRun)
                break;

            var entry = own[dbEntry.Id];
            var analysis = dbEntry.ToModel();
            if (analysis.ContentHash != entry.ContentHash) {
                var language = await GetLanguage(entry.Id, analysis.UserId, cancellationToken).ConfigureAwait(false);
                analysis = Analyze(entry, analysis.UserId, language, analysis);
            }
            if (analysis.TagState == CoachTagState.Tagged && analysis.PromptVersion >= promptVersion)
                continue;

            analysis = await ApplyTags(analysis, entry.Content, cancellationToken).ConfigureAwait(false);
            if (analysis.TagState == CoachTagState.Tagged)
                tagged.Add(analysis);
        }
        return tagged;
    }

    // The coaching "conversation" is a run of entries with no quiet gap of ConversationMaturity
    // between neighbours; the summarizer's conversations exist only for long threads, so they
    // cannot be the unit here. The run is identified by its true first lid; when it is longer than
    // MaxRunEntries only its tail is analysed, so the identity never depends on which entry
    // triggered the command.
    private async Task<(long FirstLid, List<ChatEntry> Entries)> FindRun(
        ChatId chatId, long entryLid, CancellationToken cancellationToken)
    {
        var maturity = Settings.Coach.ConversationMaturity;
        var lidRange = await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
        if (entryLid < lidRange.Start || entryLid >= lidRange.End)
            return (entryLid, []);

        var anchor = await ReadWindow(chatId, entryLid, entryLid + 1, cancellationToken).ConfigureAwait(false);
        if (anchor.Count == 0)
            return (entryLid, []);

        var run = new LinkedList<ChatEntry>();
        run.AddFirst(anchor[0]);
        var scanFloor = Math.Max(lidRange.Start, entryLid - MaxScanLids);
        var windowStart = entryLid;
        while (windowStart > scanFloor) {
            var from = Math.Max(scanFloor, windowStart - ScanWindow);
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
        while (windowEnd < lidRange.End) {
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

        var entries = run.ToList();
        var firstLid = entries[0].LocalId;
        var maxRunEntries = Settings.Coach.MaxRunEntries;
        if (entries.Count > maxRunEntries)
            entries = entries.Skip(entries.Count - maxRunEntries).ToList();
        return (firstLid, entries);
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
}
