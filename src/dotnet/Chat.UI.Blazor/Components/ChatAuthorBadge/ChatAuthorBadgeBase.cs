using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Components;

public abstract class ChatAuthorBadgeBase : ComputedStateComponent<ChatAuthorBadgeBase.Model>
{
    [Inject] private IChatAuthors ChatAuthors { get; init; } = null!;
    [Inject] private ChatActivity ChatActivity { get; init; } = null!;

    private string ChatId { get; set; } = "";
    private IChatRecordingActivity? ChatRecordingActivity { get; set; }

    [Parameter, EditorRequired] public string AuthorId { get; set; } = "";
    protected bool TrackRecording { get; set; }

    public override async ValueTask DisposeAsync() {
        await base.DisposeAsync().ConfigureAwait(true);
        ChatRecordingActivity?.Dispose();
    }

    protected override async Task OnParametersSetAsync() {
        if (!ChatAuthor.TryGetChatId(AuthorId, out var chatId))
            throw new InvalidOperationException("Invalid AuthorId");
        ChatId = chatId;
        ChatRecordingActivity?.Dispose();
        if (TrackRecording)
            ChatRecordingActivity = await ChatActivity.GetRecordingActivity(ChatId, CancellationToken.None).ConfigureAwait(false);
        _ = State.Recompute(); // ~ Same as what's in base.OnParametersSetAsync()
    }

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() { InitialValue = Model.None };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        if (ChatId.IsNullOrEmpty())
            return Model.None;

        var author = await ChatAuthors.GetAuthor(ChatId, AuthorId, true, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return Model.None;

        if (string.IsNullOrWhiteSpace(author.Picture)) {
            var picture = $"https://avatars.dicebear.com/api/avataaars/{author.Name}.svg";
            author = author with {
                Picture = picture,
            };
        }
        if (ChatRecordingActivity == null)
            return new(author, false);

        var isRecording = await ChatRecordingActivity.IsAuthorActive(author.Id, cancellationToken).ConfigureAwait(false);
        return new(author, isRecording);
    }

    public sealed record Model(Author Author, bool IsRecording) {
        public static Model None { get; } = new(new Author(), false);
    }
}
