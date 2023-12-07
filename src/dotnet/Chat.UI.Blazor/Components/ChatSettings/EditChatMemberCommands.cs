using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public static class EditChatMemberCommands
{
    public static async Task<EditChatMemberModel?> ComputeState(ChatHub chatHub, AuthorId authorId, CancellationToken cancellationToken)
    {
        var chatId = authorId.ChatId;
        var session = chatHub.Session;
        var author = await chatHub.Authors.Get(session, chatId, authorId, cancellationToken);
        if (author == null || author.HasLeft)
            return null;
        var chat = await chatHub.Chats.Get(session, chatId, cancellationToken);
        var ownerIds = await chatHub.Roles.ListOwnerIds(session, chatId, cancellationToken);
        var ownAuthor = await chatHub.Authors.GetOwn(session, chatId, cancellationToken);
        var isOwn = ownAuthor != null && ownAuthor.Id == author.Id;
        var isOwner = ownerIds.Contains(author.Id);
        var canPromoteToOwner = !isOwner && chat != null && chat.Rules.IsOwner();
        var canRemoveFromGroup = !isOwner && !isOwn;
        return new EditChatMemberModel(author, isOwner, isOwn, canPromoteToOwner, canRemoveFromGroup);
    }

    public static async Task OnRemoveFromGroupClick(ChatHub chatHub, Author author)
    {
        var result = await chatHub.UICommander().Run(new Authors_Exclude(chatHub.Session, author.Id));
        if (result.HasError)
            return;
        var authorName = author.Avatar.Name;
        chatHub.ToastUI.Show($"{authorName} removed", "icon-minus-circle", Undo, "Undo", ToastDismissDelay.Long);

        void Undo() {
            var undoCommand = new Authors_Restore(chatHub.Session, author.Id);
            _ = chatHub.UICommander().Run(undoCommand);
        }
    }

    public static async Task OnPromoteToOwnerClick(ChatHub chatHub, Author author)
    {
        var authorName = author.Avatar.Name;
        _ = await chatHub.ModalUI.Show(new ConfirmModal.Model(
            false,
            $"Are you sure you want to promote '{authorName}' to Owner? This action cannot be undone.",
            () => _ = OnPromoteToOwnerConfirmed(chatHub, author.Id, authorName)) {
            Title = "Promote to Owner"
        });
    }

    private static async Task OnPromoteToOwnerConfirmed(ChatHub chatHub, AuthorId authorId, string authorName)
    {
        var result = await chatHub.UICommander().Run(new Authors_PromoteToOwner(chatHub.Session, authorId));
        if (result.HasError)
            return;

        chatHub.ToastUI.Show($"{authorName} is promoted to Owner", "icon-star", ToastDismissDelay.Short);
    }
}
