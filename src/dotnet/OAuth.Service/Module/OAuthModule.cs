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
    public bool IsEnabled => !Settings.Route.IsNullOrEmpty() && HostInfo.HasRole(HostRole.Api) && HasCredentials;

    private bool _certificateLoadAttempted;
    private X509Certificate2? _certificate;

    private X509Certificate2? Certificate {
        get {
            if (_certificateLoadAttempted)
                return _certificate;

            _certificateLoadAttempted = true;
            try {
                _certificate = LoadCertificate(Settings);
            }
            catch (Exception e) {
                Log.LogError(e, "OAuth is disabled: the signing certificate could not be loaded");
            }
            return _certificate;
        }
    }

    private bool HasCredentials => Certificate is not null || HostInfo.IsDevelopmentInstance || HostInfo.IsTested;

    protected override void InjectServices(IServiceCollection services)
    {
        if (Settings.Route.IsNullOrEmpty() || !HostInfo.HasRole(HostRole.Api))
            return;
        if (!HasCredentials) {
            Log.LogError(
                "OAuth is disabled: no signing certificate configured " +
                "(OAuthSettings.SigningCertificateBase64 or SigningCertificatePath + SigningKeyPath).");
            return;
        }

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
                o.SetRefreshTokenReuseLeeway(Settings.RefreshTokenReuseLeeway);
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
                o.AddEventHandler(CimdClientResolver.Descriptor);
                o.AddEventHandler(RevocationSessionHandler.Descriptor);
            })
            .AddValidation(o => {
                o.UseLocalServer();
                o.UseAspNetCore();
            });
        services.AddSingleton<OAuthBearerAuthenticator>();
        services.AddSingleton<OAuthPruner>()
            .AddHostedService(c => c.GetRequiredService<OAuthPruner>());
        services.AddSingleton(c => new ServerMetadataExtender(c));
        services.AddScoped(c => new CimdClientResolver(c));
        services.AddScoped(c => new RevocationSessionHandler(c));
        services.AddHttpClient(CimdClientResolver.HttpClientName, c => {
                c.Timeout = TimeSpan.FromSeconds(5);
                c.MaxResponseContentBufferSize = CimdClientResolver.MaxDocumentLength;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddOptions<OpenIddictServerOptions>().Configure<UrlMapper>((o, urlMapper)
            => o.Resources.Add(new Uri(urlMapper.ToAbsolute(OAuthConstants.McpResourcePath))));
    }

    // Internal methods

    internal static X509Certificate2? LoadCertificate(OAuthSettings settings)
    {
        if (!settings.SigningCertificateBase64.IsNullOrEmpty())
            return X509CertificateLoader.LoadPkcs12(
                Convert.FromBase64String(settings.SigningCertificateBase64),
                settings.SigningCertificatePassword.NullIfEmpty());

        if (!settings.SigningCertificatePath.IsNullOrEmpty() && !settings.SigningKeyPath.IsNullOrEmpty())
            return X509Certificate2.CreateFromPemFile(settings.SigningCertificatePath, settings.SigningKeyPath);

        return null;
    }

    // Private methods

    private void AddCredentials(OpenIddictServerBuilder o)
    {
        var certificate = Certificate;
        if (certificate is null) {
            o.AddEphemeralSigningKey().AddEphemeralEncryptionKey();
            return;
        }

        o.AddSigningCertificate(certificate).AddEncryptionCertificate(certificate);
    }
}
