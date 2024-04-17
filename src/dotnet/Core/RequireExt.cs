using System.Diagnostics.CodeAnalysis;

namespace ActualChat;

public static class RequireExt
{
    public static T? RequireNull<T>(this T? source)
        where T : class
        => source.RequireNull(typeof(T).GetName());
    public static T? RequireNull<T>(this T? source, string name)
        where T : class
        => source == null ? source : throw StandardError.Constraint($"This {name} already exists.");
    public static async Task<T?> RequireNull<T>(this Task<T?> task)
        where T : class
    {
        var source = await task.ConfigureAwait(false);
        return source.RequireNull();
    }
    public static async Task<T?> RequireNull<T>(this Task<T?> task, string name)
        where T : class
    {
        var source = await task.ConfigureAwait(false);
        return source.RequireNull(name);
    }

    public static IEnumerable<T> Require<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this IEnumerable<T?> source,
        Requirement<T>? requirement = null)
        where T : class, IRequirementTarget
        => source.Select(x => x.Require(requirement));
}
