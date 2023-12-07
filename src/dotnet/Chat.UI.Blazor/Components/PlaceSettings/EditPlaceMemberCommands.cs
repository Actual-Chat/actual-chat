using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public static class EditPlaceMemberCommands
{
    public static async Task<EditPlaceMemberModel?> ComputeState(ChatHub chatHub, AuthorId authorId, CancellationToken cancellationToken)
    {
        var chatId = authorId.ChatId;
        if (!chatId.PlaceChatId.IsRoot)
            throw new ArgumentOutOfRangeException(nameof(authorId), "AuthorId should belong to place root chat");
        var placeId = chatId.PlaceChatId.PlaceId;
        var session = chatHub.Session;
        var author = await chatHub.Places.Get(session, placeId, authorId, cancellationToken);
        if (author == null || author.HasLeft)
            return null;
        var placeTask = chatHub.Places.Get(session, placeId, cancellationToken);
        var ownerIdsTask = chatHub.Places.ListOwnerIds(session, placeId, cancellationToken);
        var ownAuthorTask = chatHub.Places.GetOwn(session, placeId, cancellationToken);
        await Task.WhenAll(placeTask, ownerIdsTask, ownerIdsTask);
        var place = await placeTask;
        var ownerIds = await ownerIdsTask;
        var ownAuthor = await ownAuthorTask;

        var isOwn = ownAuthor != null && ownAuthor.Id == author.Id;
        var isOwner = ownerIds.Contains(author.Id);
        var canPromoteToOwner = !isOwner && place != null && place.Rules.IsOwner();
        var canRemoveFromGroup = !isOwner && !isOwn;
        return new EditPlaceMemberModel(author, isOwner, isOwn, canPromoteToOwner, canRemoveFromGroup);
    }

    public static async Task OnRemoveFromPlaceClick(ChatHub chatHub, Author author)
    {
        var result = await chatHub.UICommander().Run(new Places_Exclude(chatHub.Session, author.Id));
        if (result.HasError)
            return;
        var authorName = author.Avatar.Name;
        chatHub.ToastUI.Show($"{authorName} removed", "icon-minus-circle", Undo, "Undo", ToastDismissDelay.Long);

        void Undo() {
            var undoCommand = new Places_Restore(chatHub.Session, author.Id);
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
        var result = await chatHub.UICommander().Run(new Places_PromoteToOwner(chatHub.Session, authorId));
        if (result.HasError)
            return;

        chatHub.ToastUI.Show($"{authorName} is promoted to Owner", "icon-star", ToastDismissDelay.Short);
    }
}
