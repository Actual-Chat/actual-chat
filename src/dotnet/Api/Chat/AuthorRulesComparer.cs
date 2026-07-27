namespace ActualChat.Chat;

/// <summary>
/// Compares <see cref="AuthorRules"/> for compute methods that rebuild one on every recompute -
/// <see cref="AuthorRules"/> itself compares by reference.
/// </summary>
public sealed class AuthorRulesComparer : IEqualityComparer<AuthorRules>
{
    public bool Equals(AuthorRules? x, AuthorRules? y)
    {
        // Author and Account are compared by reference on purpose: they come straight out of
        // AuthorsBackend/AccountsBackend, so identical references mean identical content, while
        // comparing them by Id or Version would hide changes their own fields carry - an avatar
        // edit, for one, doesn't bump AuthorFull.Version.
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null)
            return false;

        return x.ChatId == y.ChatId
            && x.Permissions == y.Permissions
            && ReferenceEquals(x.Author, y.Author)
            && ReferenceEquals(x.Account, y.Account);
    }

    public int GetHashCode(AuthorRules obj)
        => HashCode.Combine(obj.ChatId, obj.Permissions, obj.Author, obj.Account);
}
