using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public static class EditPlaceMemberCommands
{
    public static async Task<EditPlaceMemberModel?> ComputeState(ChatUIHub hub, AuthorId authorId, CancellationToken cancellationToken)
    {
        var chatId = authorId.ChatId;
        if (!chatId.PlaceChatId.IsRoot)
            throw new ArgumentOutOfRangeException(nameof(authorId), "AuthorId should belong to place root chat");
        var placeId = chatId.PlaceChatId.PlaceId;
        var session = hub.Session();
        var author = await hub.Places.Get(session, placeId, authorId, cancellationToken);
        if (author == null || author.HasLeft)
            return null;
        var placeTask = hub.Places.Get(session, placeId, cancellationToken);
        var ownerIdsTask = hub.Places.ListOwnerIds(session, placeId, cancellationToken);
        var ownAuthorTask = hub.Places.GetOwn(session, placeId, cancellationToken);
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

    public static async Task OnRemoveFromPlaceClick(ChatUIHub hub, Author author)
    {
        var session = hub.Session();
        var result = await hub.UICommander().Run(new Places_Exclude(session, author.Id));
        if (result.HasError)
            return;
        var authorName = author.Avatar.Name;
        hub.ToastUI.Show($"{authorName} removed", "icon-minus-circle", Undo, "Undo", ToastDismissDelay.Long);

        void Undo() {
            var undoCommand = new Places_Restore(session, author.Id);
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
        var result = await hub.UICommander().Run(new Places_PromoteToOwner(hub.Session(), authorId));
        if (result.HasError)
            return;

        hub.ToastUI.Show($"{authorName} is promoted to Owner", "icon-star", ToastDismissDelay.Short);
    }
}
