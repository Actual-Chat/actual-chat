using ActualChat.UI.Blazor.Authorization;
using ActualChat.Users.UI.Blazor.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace ActualChat.UI.Blazor.Host.Authorization;

public static class AuthorizationOptionsExt
{
    public static void AddAppPolicies(this AuthorizationOptions options)
        => options.AddPolicy(KnownPolicies.IsUserActive,
            builder => {
                builder.AddRequirements(new IsUserActiveRequirement());
            });
}
