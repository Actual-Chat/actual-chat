using ActualChat.Users;

namespace ActualChat.Chat;

public static class Bots
{
    public static bool IsBot(AuthorId authorId)
        => authorId.LocalId < 0;

    public static AuthorId GetWalleId(ChatId chatId)
        => new(chatId, Constants.User.Walle.AuthorLocalId, AssumeValid.Option);

    public static AuthorFull GetWalle(ChatId chatId)
        => new (GetWalleId(chatId)) {
            UserId = Constants.User.Walle.UserId,
            Avatar = new AvatarFull(Constants.User.Walle.UserId) {
                Name = Constants.User.Walle.Name,
                PictureUrl = Constants.User.Walle.Picture,
            },
        };

    public static AuthorId GetSherlockId(ChatId chatId)
        => new(chatId, Constants.User.Sherlock.AuthorLocalId, AssumeValid.Option);

    public static AuthorFull GetSherlock(ChatId chatId)
        => new (GetSherlockId(chatId)) {
            UserId = Constants.User.Sherlock.UserId,
            Avatar = new AvatarFull(Constants.User.Sherlock.UserId) {
                Name = Constants.User.Sherlock.Name,
                PictureUrl = Constants.User.Sherlock.Picture,
            },
        };
}
