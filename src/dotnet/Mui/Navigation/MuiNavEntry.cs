namespace ActualChat.Mui;

public sealed record MuiNavEntry(string Title, string Href, string Icon, int Order = 10, string? Group = null);

public sealed record MuiNavGroup(string Title, string Icon, int Order);

public static class MuiNavGroups
{
    public const string Dashboards = "Dashboards";
    public const string System = "System";
}

public static class MuiNavEntryExt
{
    public static IServiceCollection AddMuiNavEntry(this IServiceCollection services, MuiNavEntry entry)
        => services.AddSingleton(entry);

    public static IServiceCollection AddMuiNavGroup(this IServiceCollection services, MuiNavGroup group)
        => services.AddSingleton(group);
}
