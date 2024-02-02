using System.Text;
using ActualChat.Hashing;

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
        _blazorScriptSuffix = "." + version.Hash().SHA1().AlphaNumeric().ToLowerInvariant();
        return _blazorScriptSuffix;
    }
}
