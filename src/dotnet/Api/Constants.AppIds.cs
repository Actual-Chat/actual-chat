namespace ActualChat;

public static partial class Constants
{
    // Android package ids of the two MAUI flavors - App.Maui.csproj picks one via IsDevMaui.
    // Both are listed on every server, in .well-known/assetlinks.json and in the web manifest,
    // so a client has to match the id it finds against its own instance.
    public static class AppIds
    {
        public const string Prod = "chat.actual.app";
        public const string Dev = "chat.actual.dev.app";
    }
}
