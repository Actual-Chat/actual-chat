using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Services;

public static class OAuthScopeExt
{
    extension(IStringLocalizer l)
    {
        public string OAuthScopeText(string scope)
            => scope switch {
                "mcp" => l.OAuthConsent_Scope_Mcp,
                "offline_access" => l.OAuthConsent_Scope_OfflineAccess,
                _ => scope,
            };
    }
}
