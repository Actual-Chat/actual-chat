using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Resources;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public static class EditPlaceMemberCommands
{
    public static async Task<EditPlaceMemberModel?> ComputeState(AppUIHub hub, AuthorId authorId, CancellationToken cancellationToken)
    {
        var chatId = authorId.ChatId;
        if (chatId is not PlaceChatId { IsRoot: true } placeChatId)
            throw new ArgumentOutOfRangeException(nameof(authorId), "AuthorId should belong to place root chat");

        var placeId = placeChatId.PlaceId;
        var session = hub.Session;
        var author = await hub.Places.Get(session, placeId, authorId, cancellationToken);
        if (author == null || author.HasLeft)
            return null;
        var placeTask = hub.Places.Get(session, placeId, cancellationToken);
        var ownerIdsTask = hub.Places.ListOwnerIds(session, placeId, cancellationToken);
        var moderatorIdsTask = hub.Places.ListModeratorIds(session, placeId, cancellationToken);
        var ownAuthorTask = hub.Places.GetOwn(session, placeId, cancellationToken);
        await Task.WhenAll(placeTask, ownerIdsTask, moderatorIdsTask, ownAuthorTask);
        var place = await placeTask;
        var ownerIds = await ownerIdsTask;
        var moderatorIds = await moderatorIdsTask;
        var ownAuthor = await ownAuthorTask;

        var isOwn = ownAuthor != null && ownAuthor.Id == author.Id;
        var isOwner = ownerIds.Contains(author.Id);
        var isModerator = moderatorIds.Contains(author.Id);
        var ownIsOwner = place != null && place.Rules.IsOwner();
        var canPromoteToOwner = !isOwner && ownIsOwner;
        var canSetModerator = !isOwner && ownIsOwner;
        var canRemoveFromGroup = !isOwner && !isOwn;
        return new EditPlaceMemberModel(
            author, isOwner, isModerator, isOwn,
            canPromoteToOwner, canSetModerator, canRemoveFromGroup);
    }

    public static async Task OnRemoveFromPlaceClick(AppUIHub hub, Author author)
    {
        var session = hub.Session;
        var result = await hub.UICommander.Run(new Places_Exclude(session, author.Id));
        if (result.HasError)
            return;
        var authorName = author.Avatar.Name;
        hub.ToastUI.Show($"{authorName} removed", "icon-minus-circle", Undo, "Undo", ToastDismissDelay.Long);

        void Undo() {
            var undoCommand = new Places_Restore(session, author.Id);
            _ = hub.UICommander.Run(undoCommand);
        }
    }

    public static async Task OnPromoteToOwnerClick(AppUIHub hub, Author author)
    {
        var l = hub.StringLocalizer;
        var authorName = author.Avatar.Name;
        _ = await hub.ModalUI.Show(new ConfirmModal.Model(
            false,
            l.Members_PromoteConfirm_Format(authorName),
            () => _ = OnPromoteToOwnerConfirmed(hub, author.Id, authorName)) {
            Title = l.Account_PromoteToOwner
        });
    }

    public static async Task OnSetModeratorClick(AppUIHub hub, Author author, bool isModerator)
    {
        var authorName = author.Avatar.Name;
        var command = new Places_ChangeRole(hub.Session, author.Id, SystemRole.Moderator, isModerator);
        var result = await hub.UICommander.Run(command);
        if (result.HasError)
            return;

        var l = hub.StringLocalizer;
        var text = isModerator
            ? l.Members_NowModerator_Format(authorName)
            : l.Members_NoLongerModerator_Format(authorName);
        hub.ToastUI.Show(text, "icon-shield", ToastDismissDelay.Short);
    }

    // Private methods

    private static async Task OnPromoteToOwnerConfirmed(AppUIHub hub, AuthorId authorId, string authorName)
    {
        var command = new Places_ChangeRole(hub.Session, authorId, SystemRole.Owner, true);
        var result = await hub.UICommander.Run(command);
        if (result.HasError)
            return;

        hub.ToastUI.Show(
            hub.StringLocalizer.Members_PromotedToOwner_Format(authorName),
            "icon-crown",
            ToastDismissDelay.Short);
    }
}
