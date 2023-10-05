namespace ActualChat.App.Server.Pages;

public static class HostHelper
{
    private static string? _blazorScriptSuffix;

    public static string GetBlazorScriptSuffix()
    {
        if (_blazorScriptSuffix != null)
            return _blazorScriptSuffix;

        var version = typeof(ComponentsWebAssemblyApplicationBuilderExtensions)
            .Assembly
            .GetInformationalVersion()
            .RequireNonEmpty("Blazor extensions assembly version");
        _blazorScriptSuffix = "." + version.GetSHA1HashCode(HashEncoding.AlphaNumeric).ToLowerInvariant();
        return _blazorScriptSuffix;
    }
}
