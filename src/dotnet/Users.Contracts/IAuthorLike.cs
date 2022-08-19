using Stl.Versioning;

namespace ActualChat.Users;

/// <summary> The general author information which can be used between services and clients. </summary>
public interface IAuthorLike : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    /// <summary>Is used as @{Name}, e.g. @ivan </summary>
    string Name { get; }
    /// <summary>The url of the author's photo.</summary>
    string Picture { get; }
    /// <summary>Indicates whether the author is anonymous.</summary>
    bool IsAnonymous { get; }
}
