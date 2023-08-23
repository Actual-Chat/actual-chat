using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public abstract class AuthorBadgeBase : ComputedStateComponent<AuthorBadgeBase.Model>
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IAuthors Authors { get; init; } = null!;
    [Inject] protected ChatActivity ChatActivity { get; init; } = null!;
    [Inject] protected IUserPresences UserPresences { get; init; } = null!;
    [Inject] protected AuthorUI AuthorUI { get; init; } = null!;

    protected AuthorId AuthorId { get; private set; }
    protected ChatId ChatId => AuthorId.ChatId;
    protected IChatRecordingActivity? ChatRecordingActivity { get; set; }

    [Parameter, EditorRequired] public string AuthorSid { get; set; } = "";
    [Parameter] public bool ShowPresence { get; set; }
    [Parameter] public bool ShowRecording { get; set; }

    public override async ValueTask DisposeAsync() {
        await base.DisposeAsync();
        ChatRecordingActivity?.Dispose();
        ChatRecordingActivity = null;
    }

    protected override void OnParametersSet()
    {
        AuthorId = new AuthorId(AuthorSid);
        ChatRecordingActivity?.Dispose();
        ChatRecordingActivity = null;
    }

    protected override async Task OnParametersSetAsync() {
        if (ShowRecording && !ChatId.IsNone)
            ChatRecordingActivity = await ChatActivity.GetRecordingActivity(ChatId, CancellationToken.None);
        await base.OnParametersSetAsync();
    }

    protected override ComputedState<Model>.Options GetStateOptions()
    {
        var model = Model.Loading;
        if (AuthorId.IsNone)
            return new () {
                InitialValue = model,
                Category = GetStateCategory(),
            };

        // Try to provide pre-filled initialValue for the first render when everything is cached
        var authorTask = GetAuthor(AuthorId, default);
#pragma warning disable VSTHRD002
        var author = authorTask.IsCompletedSuccessfully ? authorTask.Result : null;
#pragma warning restore VSTHRD002

        if (author != null) {
            model = new Model(author);
            var ownAuthorTask = Authors.GetOwn(Session, ChatId, default);
#pragma warning disable VSTHRD002
            var ownAuthor = ownAuthorTask.IsCompletedSuccessfully ? ownAuthorTask.Result : null;
#pragma warning restore VSTHRD002
            var isOwn = ownAuthor != null && ownAuthor.Id == author.Id;
            if (isOwn)
                model = model with { IsOwn = true };
        }

        return new () {
            InitialValue = model,
            Category = GetStateCategory(),
        };
    }

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        if (AuthorId.IsNone)
            return Model.None;

        var author = await GetAuthor(AuthorId, cancellationToken);
        if (author == null)
            return Model.None;

        var getPresenceTask = GetPresence(AuthorId, cancellationToken);
        var getOwnAuthorTask = Authors.GetOwn(Session, ChatId, cancellationToken);

        var presence = await getPresenceTask;
        var ownAuthor = await getOwnAuthorTask;
        var isOwn = ownAuthor != null && author.Id == ownAuthor.Id;
        return new(author, presence, isOwn);
    }

    private async ValueTask<Author?> GetAuthor(AuthorId authorId, CancellationToken cancellationToken)
    {
        if (authorId.IsNone)
            return null;

        var author = await Authors.Get(Session, authorId.ChatId, authorId, cancellationToken);
        return author;
    }

    private async ValueTask<Presence> GetPresence(AuthorId authorId, CancellationToken cancellationToken)
    {
        if (authorId.IsNone)
            return Presence.Offline;

        var presence = Presence.Unknown;
        if (ShowPresence)
            presence = await Authors.GetPresence(Session, authorId.ChatId, authorId, cancellationToken);
        if (ShowRecording && ChatRecordingActivity != null) {
            var isRecording = await ChatRecordingActivity.IsAuthorActive(authorId, cancellationToken);
            if (isRecording)
                presence = Presence.Recording;
        }
        return presence;
    }

    public sealed record Model(
        Author Author,
        Presence Presence = Presence.Unknown,
        bool IsOwn = false)
    {
        public static Model None { get; } = new(Author.None);
        public static Model Loading { get; } = new(Author.Loading); // Should differ by ref. from None
    }
}
