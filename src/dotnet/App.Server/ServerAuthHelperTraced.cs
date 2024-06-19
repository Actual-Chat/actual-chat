using ActualChat.Security;
using Microsoft.AspNetCore.Http;
using ActualLab.Fusion.Server.Authentication;
using System.Security.Claims;

namespace ActualChat;

internal class ServerAuthHelperTraced(ServerAuthHelper.Options settings, IServiceProvider services): ServerAuthHelper(settings, services) {
    protected override bool IsSameUser(User? user, ClaimsPrincipal httpUser, string schema)
    { 
        return base.IsSameUser(user, httpUser, schema);
    }

}