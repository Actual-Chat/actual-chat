
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.Media;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Net.Http.Headers;
using ActualChat.MLSearch.Bot.Tools;

namespace ActualChat.MLSearch.Bot.External;


/**
    A handler for incoming messages from a user.
    This class responsibility is to take those requests and forward it to a bot.
    It should generate JWT tokens together with the forwarded requests to allow
    the bot use internal tools. It is also possible that this bot would reply
    in asyncronous mode. That means that it will use the JWT token provided to 
    send messages to a user through the internal API. 
**/
internal class ExternalChatBotConversationHandler(SigningCredentials signingCredentials, ICommander commander, UrlMapper urlMapper, IChatsBackend chats, ISearchEngine<ChatSlice> searchEngine)
    : IBotConversationHandler, IComputeService
{
    /*
    private SigningCredentials GetSigningCredentials()
    {
        var key = _configuration.GetSection("Jwt:KEY").Value;
        var secret = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));

        return new SigningCredentials(secret, SecurityAlgorithms.HmacSha256);
    }
    */

    private async Task<string> CreateToken(List<Claim> claims)
    {
        var token = GenerateTokenOptions(signingCredentials, claims);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private JwtSecurityToken GenerateTokenOptions(SigningCredentials signingCredentials, List<Claim> claims)
    {
        var lifetime = TimeSpan.FromSeconds(300);
        var expiration = DateTime.Now.Add(lifetime);

        var token = new JwtSecurityToken(
            issuer: "integrations.actual.chat",
            audience: "bot-tools.actual.chat",
            claims: claims,
            expires: expiration,
            signingCredentials: signingCredentials
        );

        return token;
    }
    public async Task ExecuteAsync(
        IReadOnlyCollection<ChatEntry>? updatedDocuments,
        IReadOnlyCollection<ChatEntryId>? deletedDocuments,
        CancellationToken cancellationToken = default)
    {
        if (updatedDocuments == null) {
            return;
        }

        var lastUpdatedDocument = updatedDocuments.LastOrDefault();
        if (lastUpdatedDocument == null)
            return;

        var chatId = lastUpdatedDocument.ChatId;
        AuthorId botId = new(chatId, Constants.User.MLSearchBot.AuthorLocalId, AssumeValid.Option);
        if (lastUpdatedDocument.AuthorId == botId)
            return;
        ///
        
        var claims = new List<Claim>();
        claims.Add(new Claim(BotConversationPolicy.ConversationClaimType, chatId));
        var token = await CreateToken(claims).ConfigureAwait(false);
        HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var url = "https://local.actual.chat/api/bot/conversation-tools/reply?text=foo";

        HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        
        //requestMessage.Content = new StringContent(content, Encoding.UTF8, "application/json");

        HttpResponseMessage response = client.SendAsync(requestMessage).GetAwaiter().GetResult();
        var resultContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
}
