using ActualChat.Users;

namespace ActualChat.Chat;

public static class Bots
{
    public static AuthorId GetWalleId(ChatId chatId)
        => new(chatId, Constants.User.Walle.AuthorLocalId, AssumeValid.Option);

    public static AuthorFull GetWalle(ChatId chatId)
        => new (GetWalleId(chatId)) {
            UserId = Constants.User.Walle.UserId,
            Avatar = new AvatarFull(Constants.User.Walle.UserId) {
                Name = Constants.User.Walle.Name,
                Picture = Constants.User.Walle.Picture,
            },
        };
}
