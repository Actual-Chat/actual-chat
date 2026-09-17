using ActualChat.OAuth.Module;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace ActualChat.OAuth.Handlers;

/// <summary>
/// Adds what MCP clients look for and OpenIddict doesn't advertise on its own:
/// the DCR endpoint, CIMD support and the public-client auth method.
/// </summary>
public sealed class ServerMetadataExtender(IServiceProvider services)
    : IOpenIddictServerHandler<HandleConfigurationRequestContext>
{
    public static readonly OpenIddictServerHandlerDescriptor Descriptor
        = OpenIddictServerHandlerDescriptor.CreateBuilder<HandleConfigurationRequestContext>()
            .UseSingletonHandler<ServerMetadataExtender>()
            .SetOrder(int.MaxValue - 100_000)
            .Build();

    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private OAuthSettings Settings { get; } = services.GetRequiredService<OAuthSettings>();

    public ValueTask HandleAsync(HandleConfigurationRequestContext context)
    {
        var route = Settings.Route.TrimEnd('/');
        context.Metadata["registration_endpoint"] = UrlMapper.ToAbsolute($"{route}/{OAuthConstants.RegisterRoute}");
        context.Metadata["client_id_metadata_document_supported"] = true;
        context.TokenEndpointAuthenticationMethods.Add(ClientAuthenticationMethods.None);
        context.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
        return default;
    }
}
