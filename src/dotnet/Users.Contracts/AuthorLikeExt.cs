using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Users;

public static class AuthorLikeExt
{
    [return: NotNullIfNotNull("source")]
    public static Author? ToAuthor(this IAuthorLike? source)
    {
        if (source == null)
            return null;
        return new() {
            Id = source.Id,
            Version = source.Version,
            Name = source.Name,
            Picture = source.Picture,
            IsAnonymous = source.IsAnonymous,
        };
    }

    [return: NotNullIfNotNull("source")]
    public static TAuthor? InheritFrom<TAuthor>(this TAuthor? source, UserInfo? @base)
        where TAuthor : Author
    {
        if (source == null)
            return null;
        if (source.IsAnonymous || @base == null)
            return source;
        if (source.Name.IsNullOrEmpty() && !@base.Name.IsNullOrEmpty())
            source = source with { Name = @base.Name };
        return source;
    }

    [return: NotNullIfNotNull("source")]
    public static TAuthor? InheritFrom<TAuthor>(this TAuthor? source, IAuthorLike? @base)
        where TAuthor : Author
    {
        if (source == null)
            return null;
        if (source.IsAnonymous || @base == null)
            return source;
        if (source.Name.IsNullOrEmpty() && !@base.Name.IsNullOrEmpty())
            source = source with { Name = @base.Name };
        if (source.Picture.IsNullOrEmpty() && !@base.Picture.IsNullOrEmpty())
            source = source with { Picture = @base.Picture };
        return source;
    }

    [return: NotNullIfNotNull("source")]
    public static TAuthor? InheritFrom<TAuthor>(this TAuthor? source, UserAvatar? avatar)
        where TAuthor : Author
    {
        if (source == null)
            return null;
        if (avatar == null)
            return source;
        return source with { Name = source.Name, Picture = avatar.Picture };
    }
}
