using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public abstract class ChatAuthorBadgeBase : ComputedStateComponent<ChatAuthorBadgeBase.Model>
{
    [Inject] private IChatAuthors ChatAuthors { get; init; } = null!;
    [Inject] private ChatActivity ChatActivity { get; init; } = null!;
    [Inject] private IUserPresences UserPresences { get; init; } = null!;
    [Inject] private Session Session { get; init; } = null!;

    private string ChatId { get; set; } = "";
    private IChatRecordingActivity? ChatRecordingActivity { get; set; }

    [Parameter, EditorRequired] public string AuthorId { get; set; } = "";
    [Parameter] public bool ShowsPresence { get; set; }
    [Parameter] public bool ShowsRecording { get; set; }
    [Parameter] public bool EvalInitialValue { get; set; }

    public override async ValueTask DisposeAsync() {
        await base.DisposeAsync().ConfigureAwait(true);
        ChatRecordingActivity?.Dispose();
    }

    protected override async Task OnParametersSetAsync() {
        var parsedChatAuthorId = new ParsedChatAuthorId(AuthorId).AssertValid();
        ChatId = parsedChatAuthorId.ChatId.Id;
        ChatRecordingActivity?.Dispose();
        if (ShowsRecording)
            ChatRecordingActivity = await ChatActivity.GetRecordingActivity(ChatId, CancellationToken.None).ConfigureAwait(false);
        _ = State.Recompute(); // ~ Same as what's in base.OnParametersSetAsync()
    }

    protected override ComputedState<Model>.Options GetStateOptions()
    {
        var initialValue = Model.None;
        if (EvalInitialValue) {
            var parsedChatAuthorId = new ParsedChatAuthorId(AuthorId).AssertValid();
            var chatId = parsedChatAuthorId.ChatId.Id;
            if (!chatId.IsEmpty) {
                // Try to initialize with cached value
                var authorTask = GetChatAuthor(Session, parsedChatAuthorId.ChatId, AuthorId, default);
                if (authorTask.IsCompletedSuccessfully) {
 #pragma warning disable VSTHRD002
                    var author = authorTask.Result;
 #pragma warning restore VSTHRD002
                    if (author != null)
                        initialValue = new Model(author);
                }
            }
        }
        return new () { InitialValue = initialValue };
    }

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        if (ChatId.IsNullOrEmpty())
            return Model.None;
        var author = await GetChatAuthor(Session, ChatId, AuthorId, cancellationToken).ConfigureAwait(true);
        if (author == null)
            return Model.None;
        var presence = await GetPresence(Session, ChatId, AuthorId, cancellationToken).ConfigureAwait(true);
        return new(author, presence);
    }

    private async Task<Author?> GetChatAuthor(
        Session session,
        string chatId,
        string authorId,
        CancellationToken cancellationToken)
    {
        var author = await ChatAuthors.GetAuthor(session, chatId, authorId, true, cancellationToken).ConfigureAwait(true);
        if (author == null)
            return null;
        if (string.IsNullOrWhiteSpace(author.Picture)) {
            var picture = $"https://avatars.dicebear.com/api/avataaars/{author.Name}.svg";
            author = author with {
                Picture = picture,
            };
        }
        return author;
    }

    private async Task<Presence> GetPresence(
        Session session,
        string chatId,
        string authorId,
        CancellationToken cancellationToken)
    {
        var presence = Presence.Unknown;
        if (ShowsPresence)
            presence = await ChatAuthors.GetAuthorPresence(session, chatId, authorId, cancellationToken).ConfigureAwait(true);
        if (ChatRecordingActivity != null) {
            var isRecording = await ChatRecordingActivity.IsAuthorActive(authorId, cancellationToken).ConfigureAwait(true);
            if (isRecording)
                presence = Presence.Recording;
        }
        return presence;
    }

    public sealed record Model(Author Author, Presence Presence = Presence.Unknown) {
        public static Model None { get; } = new(Author.None);
    }
}
