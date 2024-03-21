using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public static class EditChatMemberCommands
{
    public static async Task<EditChatMemberModel?> ComputeState(ChatUIHub hub, AuthorId authorId, CancellationToken cancellationToken)
    {
        var chatId = authorId.ChatId;
        var session = hub.Session();
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
        var isOwner = ownerIds.Contains(author.Id);
        var isAnonymous = author.IsAnonymous;
        var ownIsAnonymous = ownAuthor.IsAnonymous;
        var canPromoteToOwner = !isOwner && ownIsOwner && !(isAnonymous ^ ownIsAnonymous);
        var canRemoveFromGroup = !isOwner && !isOwn;
        return new EditChatMemberModel(author, isOwner, isOwn, canPromoteToOwner, canRemoveFromGroup);
    }

    public static async Task OnRemoveFromGroupClick(ChatUIHub hub, Author author)
    {
        var result = await hub.UICommander().Run(new Authors_Exclude(hub.Session(), author.Id));
        if (result.HasError)
            return;
        var authorName = author.Avatar.Name;
        hub.ToastUI.Show($"{authorName} removed", "icon-minus-circle", Undo, "Undo", ToastDismissDelay.Long);

        void Undo() {
            var undoCommand = new Authors_Restore(hub.Session(), author.Id);
            _ = hub.UICommander().Run(undoCommand);
        }
    }

    public static async Task OnPromoteToOwnerClick(ChatUIHub hub, Author author)
    {
        var authorName = author.Avatar.Name;
        _ = await hub.ModalUI.Show(new ConfirmModal.Model(
            false,
            $"Are you sure you want to promote '{authorName}' to Owner? This action cannot be undone.",
            () => _ = OnPromoteToOwnerConfirmed(hub, author.Id, authorName)) {
            Title = "Promote to Owner"
        });
    }

    private static async Task OnPromoteToOwnerConfirmed(ChatUIHub hub, AuthorId authorId, string authorName)
    {
        var result = await hub.UICommander().Run(new Authors_PromoteToOwner(hub.Session(), authorId));
        if (result.HasError)
            return;

        hub.ToastUI.Show($"{authorName} is promoted to Owner", "icon-star", ToastDismissDelay.Short);
    }
}
