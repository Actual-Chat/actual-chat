namespace ActualChat.Users;

/// <summary> The general author information which can be used between services and clients. </summary>
public interface IAuthorInfo
{
    /// <summary> The url of the author avatar. </summary>
    string? Picture { get; }

    /// <summary>Is used as @{Nickame}, e.g. @ivan </summary>
    string? Nickname { get; }

    /// <summary> e.g. Ivan Ivanov </summary>
    string? Name { get; }

    /// <summary> Is user want to use anonymous author </summary>
    bool IsAnonymous { get; }
}
