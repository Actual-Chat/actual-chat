using ActualChat.Users;

namespace ActualChat.Chat;

/// <summary>
/// View object that can be transfered to the client. <br />
/// This type doesn't contain UserId and other privacy-related fields.
/// </summary>
public record class AuthorInfo : IAuthorInfo
{
    /// <inheritdoc />
    public string? Picture { get; init; }
    /// <inheritdoc />
    public string? Nickname { get; init; }
    /// <inheritdoc />
    public string? Name { get; init; }
    /// <inheritdoc />
    public bool IsAnonymous { get; init; }
    /// <summary>
    /// Status from the users (micro) service.
    /// </summary>
    // ToDo: add user statuses to the users service
    public bool IsOnline { get; init; }

    public AuthorInfo() { }
    public AuthorInfo(IAuthorInfo info)
    {
        Picture = info.Picture;
        Nickname = info.Nickname;
        Name = info.Name;
        IsAnonymous = info.IsAnonymous;
    }
}
