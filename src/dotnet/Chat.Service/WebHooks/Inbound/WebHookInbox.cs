using ActualChat.Resilience;
using ActualChat.WebHooks;

namespace ActualChat.Chat;

public sealed record InboundResult(
    int StatusCode,
    string? Error = null,
    long? EntryLocalId = null,
    TimeSpan? RetryAfter = null)
{
    public bool IsOk => StatusCode == 200;
}

/// <summary>
/// Everything behind <c>POST /hooks/in/{token}</c> except HTTP itself, so tests and the UI's test post share it.
/// </summary>
public sealed class WebHookInbox(IServiceProvider services)
{
    private const string TestMessageFormat = "Test message from {0}";
    // Disabled, archived, deleted and retired all read the same from the outside
    private const string GoneError = "This web hook is no longer available.";
    // Also used by the endpoint when something escapes this class entirely
    internal const string UnavailableError = "Temporarily unavailable, retry later.";

    private IServiceProvider Services { get; } = services;
    private IWebHooksBackend WebHooksBackend { get; } = services.GetRequiredService<IWebHooksBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private ICommander Commander { get; } = services.Commander();
    private RateLimitPolicy RateLimitPolicy => field ??= Services.GetRequiredService<RateLimitPolicy>();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<InboundResult> Post(
        string token,
        string contentType,
        string body,
        CancellationToken cancellationToken)
    {
        var startedAt = CpuTimestamp.Now;
        // LooksValid keeps arbitrary request strings out of the GetByTokenHash compute method's cache.
        // A token that never resolved to a hook is logged nowhere: this route is public, so scanners
        // would otherwise own the log.
        if (!WebHookTokens.LooksValid(token))
            return new InboundResult(404);

        var hook = await WebHooksBackend
            .GetByTokenHash(WebHookTokens.Hash(token), cancellationToken)
            .ConfigureAwait(false);
        if (hook is null || hook.Kind != WebHookKind.Incoming)
            return new InboundResult(404);

        var result = await PostResolved(hook, contentType, body, cancellationToken).ConfigureAwait(false);
        var elapsedMs = (int)startedAt.Elapsed.TotalMilliseconds;
        if (result.IsOk)
            Log.LogDebug("Inbound post: hook {HookId} -> entry {EntryLocalId}, {StatusCode} in {ElapsedMs}ms",
                hook.Id, result.EntryLocalId, result.StatusCode, elapsedMs);
        else
            Log.LogWarning("Inbound post rejected: hook {HookId} -> {StatusCode} '{Error}' in {ElapsedMs}ms",
                hook.Id, result.StatusCode, result.Error, elapsedMs);

        return result;
    }

    public Task<InboundResult> PostTest(WebHook hook, string sentBy, CancellationToken cancellationToken)
        => PostPayload(hook, new InboundPayload(string.Format(TestMessageFormat, sentBy), null, []), cancellationToken);

    // Private methods

    private async Task<InboundResult> PostResolved(
        WebHook hook,
        string contentType,
        string body,
        CancellationToken cancellationToken)
    {
        if (!hook.IsEnabled)
            return new InboundResult(410, GoneError);

        try {
            // Rejected posts are charged too, so a flood of garbage bodies is throttled as well
            var identities = new RateLimitIdentity[] {
                new(RateLimitIdentityKind.Target, hook.Id.Value),
                new(RateLimitIdentityKind.UserId, hook.Id.ToBotUserId().Value),
            };
            await RateLimitPolicy
                .Check(nameof(WebHookInbox), RateLimitClass.WebHookInbound, identities, cancellationToken)
                .ConfigureAwait(false);

            var payload = InboundPayloadParser.Parse(contentType, body);
            if (payload is null)
                return new InboundResult(400, "The body must be JSON, or a form with a JSON 'payload' field.");

            return await PostPayload(hook, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (RateLimitExceededException e) {
            return new InboundResult(429, "Too many posts. Please retry later.", RetryAfter: e.RetryDelay);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Inbound post failed: hook {HookId}", hook.Id);
            return new InboundResult(503, UnavailableError);
        }
    }

    private async Task<InboundResult> PostPayload(
        WebHook hook,
        InboundPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.IsEmpty)
            return new InboundResult(400, "Nothing to post: 'text' and 'attachments' are both empty.");

        if (!ChatId.TryParse(hook.ScopeId, out var chatId))
            return new InboundResult(410, GoneError);

        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null || chat.IsArchived)
            return new InboundResult(410, GoneError);

        var (markup, imageUrls) = SlackCardFolder.Fold(payload);
        if (markup.Length > Constants.Chat.MaxEntryTextLength)
            return new InboundResult(400, $"The message is longer than {Constants.Chat.MaxEntryTextLength} characters.");

        if (payload.ReplyTo is { } replyTo) {
            var repliedEntry = await ChatsBackend
                .GetEntry(ChatEntryId.New(chatId, replyTo), cancellationToken)
                .ConfigureAwait(false);
            if (repliedEntry is null || repliedEntry.IsRemoved)
                return new InboundResult(400, "'replyTo' does not name a message in this chat.");
        }

        var author = await AuthorsBackend
            .GetByUserId(chatId, hook.Id.ToBotUserId(), RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (author is null || author.HasLeft)
            return new InboundResult(410, GoneError);

        var attachments = new List<ChatEntryAttachment>();
        foreach (var imageUrl in imageUrls) {
            var mediaId = await Commander
                .Call(new MediaBackend_GrabImage(imageUrl), true, cancellationToken)
                .ConfigureAwait(false);
            if (mediaId is null)
                continue;

            attachments.Add(new ChatEntryAttachment { MediaId = mediaId });
        }

        if (markup.Length == 0 && attachments.Count == 0)
            return new InboundResult(400, "No image could be fetched and there is no text.");

        var command = new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chatId, 0),
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author.Id,
                Content = markup,
                RepliedEntryLid = payload.ReplyTo is { } lid ? Option.Some<long?>(lid) : default,
                Attachments = attachments.Count == 0 ? null : attachments.ToArray(),
            }));
        var entry = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        try {
            await Commander
                .Call(new WebHooksBackend_RecordPost(hook.Id, hook.ScopeId), true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            // The message is already posted, so failing here would report a failure the sender
            // would retry into a duplicate. LastActivityAt is cosmetic; the entry is not.
            Log.LogWarning(e, "RecordPost failed: hook {HookId}", hook.Id);
        }

        return new InboundResult(200, EntryLocalId: entry.LocalId);
    }
}
