namespace ActualChat;

public static partial class Constants
{
    // Custom URL schemes the MAUI apps register. Both flavors are listed on every
    // server: MauiPreferences.HostOverride lets a prod-flavor app sign in against dev.
    public static class AppSchemes
    {
        public const string Prod = "voxt";
        public const string Dev = $"{Prod}-dev";
        public static readonly IReadOnlySet<string> All
            = new HashSet<string>([Prod, Dev], StringComparer.OrdinalIgnoreCase);
    }
}
