using System.Text;
using ActualChat.Hashing;
using ActualChat.Security;
using ActualChat.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Documents;
using ActualChat.Chat;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ActualChat.MLSearch.Bot.Tools;

internal static class BotConversationPolicy {
    public const string Name = "BotConversation";
    public const string ConversationClaimType = "ConversationId";

    public static AuthorizationPolicyBuilder Configure(AuthorizationPolicyBuilder policy) => 
        policy
            .RequireClaim(ConversationClaimType)
            .RequireAuthenticatedUser();


    public static string? GetConversationId(this ClaimsPrincipal claims){
        return claims.FindFirstValue(ConversationClaimType);
    }
}

// Note: 
// This class uses BotAuthSchemeHandler directly only and only for one reason. 
// The Authentication in the current state is a complete mess.
// 1. Authentication Handler can not be added for a single controller. 
//    It adds to all external call - no matter what controller is being used.
// 2. Authentication middleware can not be inserted in a separate service. 
//    So far only the AppService being first adds middleware. Meaning that
//    other services (like MLServiceModulde) can't add it's own authentication.
//    Example:
//    System.InvalidOperationException: Endpoint ActualChat.MLSearch.Bot.Tools.ConversationToolsController.Reply
//    (ActualChat.MLSearch.Service) contains authorization metadata, but a middleware 
//    was not found that supports authorization.
//    Configure your application startup by adding app.UseAuthorization() 
//    in the application startup code. If there are calls to app.UseRouting() 
//    and app.UseEndpoints(...), the call to app.UseAuthorization() must go between them.
[BotTools]
[ApiController]
[Route("api/bot/conversation-tools")]
//[Authorize(Policy = BotConversationPolicy.Name)]
//, 
public sealed class ConversationToolsController(ICommander commander, BotAuthSchemeHandler auth): ControllerBase
{
    [HttpGet("reply")]
    public async Task Reply(string text, CancellationToken cancellationToken) {
    /*
    }
    [HttpPost("reply")]
    public async Task Reply([FromBody]string text, CancellationToken cancellationToken)
    {
    */
        var claims = auth.GetValidatedClaims(this.HttpContext.Request);
        if (claims == null) {
            throw new UnauthorizedAccessException();
        }
        //var botAuthorId = this.HttpContext.User.Identity;
        string? conversationId = claims.GetConversationId();
        if (conversationId.IsNullOrEmpty()){
            throw new UnauthorizedAccessException();
        }
        var chatId = ChatId.Parse(conversationId);
        // TODO: Get bot identity from the http context.
        AuthorId botId = new(chatId, Constants.User.MLSearchBot.AuthorLocalId, AssumeValid.Option);
        var textEntryId = new TextEntryId(chatId, 0, AssumeValid.Option);
        var upsertCommand = new ChatsBackend_ChangeEntry(
            textEntryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = botId,
                Content = text,
            }));
        await commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        return;
    }

}