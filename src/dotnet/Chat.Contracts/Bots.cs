
namespace ActualChat.Chat;

/// <summary>
/// Provides utilities for working with bot authors (Walle AI assistant).
/// </summary>
public static class Bots
{
    public static bool IsBot(AuthorId authorId)
        => authorId.LocalId < 0;

    public static bool IsHookBot(AuthorId authorId)
        // Wall-E (-1) and Sherlock (-2) are system authors; hook bots take the ids below them
        => authorId.LocalId < Constants.User.Sherlock.AuthorLocalId;

    public static AuthorId GetWalleId(ChatId chatId)
        => Constants.User.Walle.GetWalleAuthorId(chatId);

    public static AuthorFull GetWalle(ChatId chatId)
        => new (Constants.User.Walle.UserId, GetWalleId(chatId)) {
            Avatar = new AvatarFull(Constants.User.Walle.UserId) {
                Name = Constants.User.Walle.Name,
                PictureUrl = Constants.User.Walle.Picture,
            },
        };
}
