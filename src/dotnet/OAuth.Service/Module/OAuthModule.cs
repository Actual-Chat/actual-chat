using System.Security.Cryptography.X509Certificates;
using ActualChat.Db.Module;
using ActualChat.OAuth.Db;
using ActualChat.OAuth.Handlers;
using ActualChat.Redis.Module;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth.Module;

public sealed class OAuthModule(IServiceProvider moduleServices)
    : HostModule<OAuthSettings>(moduleServices), IServerModule
{
    public bool IsEnabled => !Settings.Route.IsNullOrEmpty() && HostInfo.HasRole(HostRole.Api);

    protected override void InjectServices(IServiceCollection services)
    {
        if (!IsEnabled)
            return;

        var rpcHost = services.AddRpcHost(HostInfo);
        rpcHost.AddApi<IOAuthGrants, OAuthGrants>();

        var route = Settings.Route.TrimEnd('/');
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<OAuthDbContext>(services);
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, OAuthDbInitializer>();
        dbModule.AddDbContextServices<OAuthDbContext>(services);
        // OpenIddict's EF stores take the DbContext itself; DbModule registers only the pooled factory
        services.AddScoped(c => c.GetRequiredService<IDbContextFactory<OAuthDbContext>>().CreateDbContext());

        services.AddOpenIddict()
            .AddCore(o => {
                o.UseEntityFrameworkCore().UseDbContext<OAuthDbContext>();
                o.ReplaceApplicationManager<
                    OpenIddictEntityFrameworkCoreApplication, LoopbackAwareApplicationManager>();
            })
            .AddServer(o => {
                o.SetAuthorizationEndpointUris($"{route}/{OAuthConstants.AuthorizeRoute}")
                    .SetTokenEndpointUris($"{route}/{OAuthConstants.TokenRoute}")
                    .SetRevocationEndpointUris($"{route}/{OAuthConstants.RevokeRoute}");
                o.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
                o.RequireProofKeyForCodeExchange();
                o.RegisterScopes(OAuthConstants.McpScope, Scopes.OfflineAccess);
                o.SetAuthorizationCodeLifetime(Settings.AuthorizationCodeLifetime)
                    .SetAccessTokenLifetime(Settings.AccessTokenLifetime)
                    .SetRefreshTokenLifetime(Settings.RefreshTokenLifetime);
                o.DisableAccessTokenEncryption();
                o.UseReferenceRefreshTokens();
                o.SetRefreshTokenReuseLeeway(TimeSpan.Zero);
                // The MCP endpoint is the only resource (registered below, once UrlMapper is resolvable),
                // and every client gets it, so per-client rsrc: permissions would only duplicate that
                o.IgnoreResourcePermissions();
                AddCredentials(o);
                var aspNetCore = o.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
                // Tests and plain-http dev hosts have no TLS; prod sees https via forwarded headers + UseBaseUrl
                if (HostInfo.IsDevelopmentInstance || HostInfo.IsTested)
                    aspNetCore.DisableTransportSecurityRequirement();
                o.AddEventHandler(ServerMetadataExtender.Descriptor);
            })
            .AddValidation(o => {
                o.UseLocalServer();
                o.UseAspNetCore();
            });
        services.AddSingleton(c => new ServerMetadataExtender(c));
        services.AddOptions<OpenIddictServerOptions>().Configure<UrlMapper>((o, urlMapper)
            => o.Resources.Add(new Uri(urlMapper.ToAbsolute(OAuthConstants.McpResourcePath))));
    }

    // Private methods

    private void AddCredentials(OpenIddictServerBuilder o)
    {
        if (Settings.SigningCertificateBase64.IsNullOrEmpty()) {
            if (!HostInfo.IsDevelopmentInstance && !HostInfo.IsTested)
                throw StandardError.Configuration(
                    "OAuthSettings.SigningCertificateBase64 is required outside development.");

            o.AddEphemeralSigningKey().AddEphemeralEncryptionKey();
            return;
        }

        var certificate = X509CertificateLoader.LoadPkcs12(
            Convert.FromBase64String(Settings.SigningCertificateBase64),
            Settings.SigningCertificatePassword.NullIfEmpty());
        o.AddSigningCertificate(certificate).AddEncryptionCertificate(certificate);
    }
}
