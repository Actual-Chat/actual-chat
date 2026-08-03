using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public static class EditChatMemberCommands
{
    public static async Task<EditChatMemberModel?> ComputeState(AppUIHub hub, AuthorId authorId, CancellationToken cancellationToken)
    {
        var chatId = authorId.ChatId;
        var session = hub.Session;
        var author = await hub.Authors.Get(session, chatId, authorId, cancellationToken);
        if (author == null || author.HasLeft)
            return null;

        var chat = await hub.Chats.Get(session, chatId, cancellationToken);
        if (chat == null)
            return null;

        var ownAuthor = chat.Rules.Author;
        if (ownAuthor == null || !chat.Rules.CanEditMembers())
            return null;

        var ownIsOwner = chat.Rules.IsOwner();
        var isOwn = ownAuthor.Id == author.Id;
        var ownerIds = await hub.Roles.ListOwnerIds(session, chatId, cancellationToken);
        var moderatorIds = await hub.Roles.ListModeratorIds(session, chatId, cancellationToken);
        var isOwner = ownerIds.Contains(author.Id);
        var isModerator = moderatorIds.Contains(author.Id);
        var isAnonymous = author.IsAnonymous;
        var ownIsAnonymous = ownAuthor.IsAnonymous;
        var canPromoteToOwner = !isOwner && ownIsOwner && !(isAnonymous ^ ownIsAnonymous);
        var canSetModerator = !isOwner && ownIsOwner;
        var canRemoveFromGroup = !isOwner && !isOwn;
        return new EditChatMemberModel(
            author, isOwner, isModerator, isOwn,
            canPromoteToOwner, canSetModerator, canRemoveFromGroup);
    }

    public static async Task OnRemoveFromGroupClick(AppUIHub hub, Author author)
    {
        var result = await hub.UICommander.Run(new Authors_Exclude(hub.Session, author.Id));
        if (result.HasError)
            return;
        var authorName = author.Avatar.Name;
        hub.ToastUI.Show($"{authorName} removed", "icon-minus-circle", Undo, "Undo", ToastDismissDelay.Long);

        void Undo() {
            var undoCommand = new Authors_Restore(hub.Session, author.Id);
            _ = hub.UICommander.Run(undoCommand);
        }
    }

    public static async Task OnPromoteToOwnerClick(AppUIHub hub, Author author)
    {
        var authorName = author.Avatar.Name;
        _ = await hub.ModalUI.Show(new ConfirmModal.Model(
            false,
            $"Are you sure you want to promote '{authorName}' to Owner? This action cannot be undone.",
            () => _ = OnPromoteToOwnerConfirmed(hub, author.Id, authorName)) {
            Title = "Promote to Owner"
        });
    }

    public static async Task OnSetModeratorClick(AppUIHub hub, Author author, bool isModerator)
    {
        var authorName = author.Avatar.Name;
        var command = new Authors_ChangeRole(hub.Session, author.Id, SystemRole.Moderator, isModerator);
        var result = await hub.UICommander.Run(command);
        if (result.HasError)
            return;

        var text = isModerator
            ? $"{authorName} is now a Moderator"
            : $"{authorName} is no longer a Moderator";
        hub.ToastUI.Show(text, "icon-star-2", ToastDismissDelay.Short);
    }

    // Private methods

    private static async Task OnPromoteToOwnerConfirmed(AppUIHub hub, AuthorId authorId, string authorName)
    {
        var command = new Authors_ChangeRole(hub.Session, authorId, SystemRole.Owner, true);
        var result = await hub.UICommander.Run(command);
        if (result.HasError)
            return;

        hub.ToastUI.Show($"{authorName} is promoted to Owner", "icon-star", ToastDismissDelay.Short);
    }
}
