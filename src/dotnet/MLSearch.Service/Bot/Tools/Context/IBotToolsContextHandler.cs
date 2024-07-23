using Microsoft.AspNetCore.Http;

namespace ActualChat.MLSearch.Bot.Tools.Context;

public interface IBotToolsContextHandler 
{
    IBotToolsContext GetContext(HttpRequest request);
    void SetContext(HttpRequestMessage request, string conversationId);
}