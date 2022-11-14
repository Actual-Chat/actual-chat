using ActualChat.Chat.Db;
using ActualChat.Users;

namespace ActualChat.Chat;

public static class AuthorExt
{
    public static Symbol GetWalleId(Symbol chatId)
        => DbAuthor.ComposeId(chatId, Constants.Chat.WalleAuthorLocalId);

    public static AuthorFull GetWalle(Symbol chatId)
        => new () {
            Id = GetWalleId(chatId),
            UserId = UserConstants.Walle.UserId,
            Avatar = new AvatarFull {
                Name = UserConstants.Walle.Name,
                Picture = UserConstants.Walle.Picture,
            },
        };
}
