
namespace ActualChat.Chat;

/// <summary>
/// Provides utilities for working with bot authors (Walle AI assistant).
/// </summary>
public static class Bots
{
    public static bool IsBot(AuthorId authorId)
        => authorId.LocalId < 0;

    public static AuthorId GetWalleId(ChatId chatId)
        => AuthorId.New(chatId, Constants.User.Walle.AuthorLocalId);

    public static AuthorFull GetWalle(ChatId chatId)
        => new (Constants.User.Walle.UserId, GetWalleId(chatId)) {
            Avatar = new AvatarFull(Constants.User.Walle.UserId) {
                Name = Constants.User.Walle.Name,
                PictureUrl = Constants.User.Walle.Picture,
            },
        };
}
