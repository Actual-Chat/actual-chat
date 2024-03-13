using ActualChat.Users;

namespace ActualChat.Chat;

public static class Bots
{
    public static AuthorId GetWalleId(ChatId chatId)
        => new(chatId, Constants.User.Walle.AuthorLocalId, AssumeValid.Option);

    public static AuthorId GetMLSearchBotId(ChatId chatId)
        => new(chatId, Constants.User.MLSearchBot.AuthorLocalId, AssumeValid.Option);

    public static AuthorFull GetMLSearchBot(ChatId chatId)
        => new (GetMLSearchBotId(chatId)) {
            UserId = Constants.User.MLSearchBot.UserId,
            Avatar = new AvatarFull(Constants.User.MLSearchBot.UserId) {
                Name = Constants.User.MLSearchBot.Name,
                PictureUrl = Constants.User.MLSearchBot.Picture,
            },
        };

    public static AuthorFull GetWalle(ChatId chatId)
        => new (GetWalleId(chatId)) {
            UserId = Constants.User.Walle.UserId,
            Avatar = new AvatarFull(Constants.User.Walle.UserId) {
                Name = Constants.User.Walle.Name,
                PictureUrl = Constants.User.Walle.Picture,
            },
        };
}
