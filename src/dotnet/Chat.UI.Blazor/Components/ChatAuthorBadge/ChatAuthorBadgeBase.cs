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
        => new() { InitialValue = Model.None };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        if (ChatId.IsNullOrEmpty())
            return Model.None;

        var author = await ChatAuthors.GetAuthor(Session, ChatId, AuthorId, true, cancellationToken).ConfigureAwait(true);
        if (author == null)
            return Model.None;

        if (string.IsNullOrWhiteSpace(author.Picture)) {
            var picture = $"https://avatars.dicebear.com/api/avataaars/{author.Name}.svg";
            author = author with {
                Picture = picture,
            };
        }

        var presence = Presence.Unknown;
        if (ShowsPresence)
            presence = await ChatAuthors.GetAuthorPresence(Session, ChatId, AuthorId, cancellationToken).ConfigureAwait(true);
        if (ChatRecordingActivity != null) {
            var isRecording = await ChatRecordingActivity.IsAuthorActive(author.Id, cancellationToken).ConfigureAwait(false);
            if (isRecording)
                presence = Presence.Recording;
        }

        return new(author, presence);
    }

    public sealed record Model(Author Author, Presence Presence = Presence.Unknown) {
        public static Model None { get; } = new(new Author());
    }
}
