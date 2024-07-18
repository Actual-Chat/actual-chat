using System.Text;
using ActualChat.Hashing;
using ActualChat.Security;
using ActualChat.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Documents;
using ActualChat.Chat;

namespace ActualChat.MLSearch.Bot.Tools;

internal static class BotToolsPolicy {
    public const string Name = "BotTool";
}


[BotTools]
[ApiController, Route("api/bot/search")]
//[Authorize(Policy = BotToolsPolicy.Name)]
internal sealed class SearchToolsController(UrlMapper urlMapper, IChatsBackend chats, ISearchEngine<ChatSlice> searchEngine) : ControllerBase
{
    [HttpPost("public-chat-text")]
    public async Task<ActionResult<List<RankedDocument<ChatSlice>>>> PublicChatsText([FromBody]string text, CancellationToken cancellationToken)
    {
        // TODO: constrain with permissions
        // NOTE: Do not allow this code into prod without permission constrains added.
        var query = new SearchQuery() {
            Filters = [
                new SemanticFilter<ChatSlice>(text),
                new KeywordFilter<ChatSlice>(text.Split())
            ],
        };
        var searchResult = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        var documents = searchResult.Documents;
        // TODO: Error handling
        
        return documents.Select(e=>e).ToList();
    }

    [HttpPost("private-chat-text")]
    public async Task<ActionResult<string>> PrivateChatsText([FromBody]string text, CancellationToken cancellationToken)
    {
        var user = this.HttpContext.User;
        return JsonSerializer.Serialize(user);
    }
}