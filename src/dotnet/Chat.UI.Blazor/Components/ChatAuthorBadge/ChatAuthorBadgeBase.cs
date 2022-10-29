using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public abstract class ChatAuthorBadgeBase : ComputedStateComponent<ChatAuthorBadgeBase.Model>
{
    [Inject] protected IChatAuthors ChatAuthors { get; init; } = null!;
    [Inject] protected ChatActivity ChatActivity { get; init; } = null!;
    [Inject] protected IUserPresences UserPresences { get; init; } = null!;
    [Inject] protected Session Session { get; init; } = null!;

    protected ParsedAuthorId ParsedAuthorId { get; private set; }
    protected Symbol ChatId => ParsedAuthorId.ChatId;
    protected bool IsValid => ParsedAuthorId.IsValid;
    protected IChatRecordingActivity? ChatRecordingActivity { get; set; }

    [Parameter, EditorRequired] public string AuthorId { get; set; } = "";
    [Parameter] public bool ShowsPresence { get; set; }
    [Parameter] public bool ShowsRecording { get; set; }

    public override async ValueTask DisposeAsync() {
        await base.DisposeAsync();
        ChatRecordingActivity?.Dispose();
    }

    protected override async Task OnParametersSetAsync() {
        ParsedAuthorId = new ParsedAuthorId(AuthorId);
        ChatRecordingActivity?.Dispose();
        if (ShowsRecording)
            ChatRecordingActivity = await ChatActivity.GetRecordingActivity(ChatId, CancellationToken.None).ConfigureAwait(false);
        // Default scheduler is used from here
        _ = State.Recompute(); // ~ Same as what's in base.OnParametersSetAsync()
    }

    protected override ComputedState<Model>.Options GetStateOptions()
    {
        ParsedAuthorId = new ParsedAuthorId(AuthorId);
        if (!IsValid)
            return new () { InitialValue = Model.None };

        var model = Model.Loading;
        // Try to provide pre-filled initialValue for the first render when everything is cached
        var authorTask = GetChatAuthor(Session, ChatId, AuthorId, default);
#pragma warning disable VSTHRD002
        var author = authorTask.IsCompletedSuccessfully ? authorTask.Result : null;
#pragma warning restore VSTHRD002

        if (author != null) {
            model = new Model(author);
            var ownAuthorTask = ChatAuthors.GetOwn(Session, ChatId, default);
#pragma warning disable VSTHRD002
            var ownAuthor = ownAuthorTask.IsCompletedSuccessfully ? ownAuthorTask.Result : null;
#pragma warning restore VSTHRD002
            var isOwn = ownAuthor != null && ownAuthor.Id == author.Id;
            if (isOwn)
                model = model with { IsOwn = true };
        }

        return new () { InitialValue = model };
    }

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        if (!IsValid)
            return Model.None;

        var author = await GetChatAuthor(Session, ChatId, AuthorId, cancellationToken);
        if (author == null)
            return Model.None;

        var presence = await GetPresence(Session, ChatId, AuthorId, cancellationToken);
        var ownAuthor = await ChatAuthors.GetOwn(Session, ChatId, cancellationToken);
        var isOwn = ownAuthor != null && author.Id == ownAuthor.Id;
        return new(author, presence, isOwn);
    }

    private async ValueTask<ChatAuthor?> GetChatAuthor(
        Session session,
        string chatId,
        string authorId,
        CancellationToken cancellationToken)
    {
        if (!IsValid)
            return null;

        var author = await ChatAuthors.Get(session, chatId, authorId, cancellationToken);
        if (author == null)
            return null;
        return author;
    }

    private async ValueTask<Presence> GetPresence(
        Session session,
        string chatId,
        string authorId,
        CancellationToken cancellationToken)
    {
        if (!IsValid)
            return Presence.Offline;

        var presence = Presence.Unknown;
        if (ShowsPresence)
            presence = await ChatAuthors.GetAuthorPresence(session, chatId, authorId, cancellationToken);
        if (ChatRecordingActivity != null) {
            var isRecording = await ChatRecordingActivity.IsAuthorActive(authorId, cancellationToken);
            if (isRecording)
                presence = Presence.Recording;
        }
        return presence;
    }

    public sealed record Model(
        ChatAuthor ChatAuthor,
        Presence Presence = Presence.Unknown,
        bool IsOwn = false)
    {
        public static Model None { get; } = new(ChatAuthor.None);
        public static Model Loading { get; } = new(ChatAuthor.Loading); // Should differ by ref. from None
    }
}
