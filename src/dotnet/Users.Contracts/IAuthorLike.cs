using Stl.Versioning;

namespace ActualChat.Users;

/// <summary> The general author information which can be used between services and clients. </summary>
public interface IAuthorLike : IHasId<Symbol>, IHasVersion<long>
{
    /// <summary>Is used as @{Name}, e.g. @ivan </summary>
    string Name { get; }
    /// <summary> The url of the author avatar. </summary>
    string Picture { get; }
    /// <summary> Is user want to use anonymous author </summary>
    bool IsAnonymous { get; }
}
