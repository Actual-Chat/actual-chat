using ActualChat.Db;
using OpenIddict.Abstractions;

namespace ActualChat.OAuth.Db;

public class OAuthDbInitializer(IServiceProvider services) : DbInitializer<OAuthDbContext>(services)
{
    public override async Task InitializeData(CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        if (await scopes.FindByNameAsync(OAuthConstants.McpScope, cancellationToken).ConfigureAwait(false) is not null)
            return;

        await scopes.CreateAsync(new OpenIddictScopeDescriptor {
            Name = OAuthConstants.McpScope,
            DisplayName = "Read and post in your chats via MCP",
            Resources = { OAuthConstants.McpResourcePath },
        }, cancellationToken).ConfigureAwait(false);
    }
}
