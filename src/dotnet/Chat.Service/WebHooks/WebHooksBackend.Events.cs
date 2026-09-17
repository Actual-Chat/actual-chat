namespace ActualChat.Chat;

public partial class WebHooksBackend
{
    // [EventHandler]
    public virtual Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
        => Task.CompletedTask;

    // [EventHandler]
    public virtual Task OnReactionChangedEvent(ReactionChangedEvent eventCommand, CancellationToken cancellationToken)
        => Task.CompletedTask;

    // [EventHandler]
    public virtual Task OnAuthorUpsertedEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken)
        => Task.CompletedTask;

    // [EventHandler]
    public virtual Task OnAuthorsRemovedEvent(AuthorsRemovedEvent eventCommand, CancellationToken cancellationToken)
        => Task.CompletedTask;

    // [EventHandler]
    public virtual Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
        => Task.CompletedTask;

    // [EventHandler]
    public virtual Task OnPlaceChangedEvent(PlaceChangedEvent eventCommand, CancellationToken cancellationToken)
        => Task.CompletedTask;

    // [EventHandler]
    public virtual Task OnPlaceMembershipChangedEvent(
        PlaceMembershipChangedEvent eventCommand,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    // [EventHandler]
    public virtual Task OnUserNotifiedEvent(UserNotifiedEvent eventCommand, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
