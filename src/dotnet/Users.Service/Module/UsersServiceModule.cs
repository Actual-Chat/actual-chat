using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Redis.Module;
using ActualChat.Security;
using ActualChat.Users.Db;
using ActualChat.Users.Email;
using ActualChat.Users.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders.Physical;
using Newtonsoft.Json;
using ActualLab.Fusion.Authentication.Services;
using ActualLab.Fusion.EntityFramework.Operations;
using ActualLab.Fusion.Server;
using ActualLab.Fusion.Server.Authentication;
using Twilio;
using Twilio.Clients;

namespace ActualChat.Users.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class UsersServiceModule(IServiceProvider moduleServices)
    : HostModule<UsersSettings>(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IAccountsBackend>().IsClient();
        var rpc = rpcHost.Rpc;
        var commander = rpcHost.Commander;
        var fusion = rpcHost.Fusion;
        var fusionWebServer = fusion.AddWebServer();

        if (rpcHost.IsApiHost) {
            services.AddMvcCore().AddApplicationPart(GetType().Assembly);

            // ASP.NET Core authentication providers
            var authentication = services.AddAuthentication(options => {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            });
            authentication.AddCookie(options => {
                options.LoginPath = "/signIn";
                options.LogoutPath = "/signOut";
                if (HostInfo.IsDevelopmentInstance)
                    options.Cookie.SecurePolicy = CookieSecurePolicy.None;
                // This controls the expiration time stored in the cookie itself
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.SlidingExpiration = true;
                // And this controls when the browser forgets the cookie
                options.Events.OnSigningIn = ctx => {
                    ctx.CookieOptions.Expires = DateTimeOffset.UtcNow.AddDays(28);
                    return Task.CompletedTask;
                };
            });
            authentication.AddGoogle(options => {
                options.ClientId = Settings.GoogleClientId;
                options.ClientSecret = Settings.GoogleClientSecret;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            });
            authentication.AddApple(options => {
                options.Events.OnCreatingTicket = context => {
                    if (context.Identity == null)
                        return Task.CompletedTask;

                    if (!context.HttpContext.Request.Form.TryGetValue("user", out var userValue))
                        return Task.CompletedTask;

                    var userInfo = JsonConvert.DeserializeObject<AppleUser>(userValue.ToString());
                    if (userInfo?.Name == null)
                        return Task.CompletedTask;

                    if (!userInfo.Name.FirstName.IsNullOrEmpty())
                        context.Identity.AddClaim(new Claim(ClaimTypes.GivenName, userInfo.Name.FirstName));

                    if (!userInfo.Name.LastName.IsNullOrEmpty())
                        context.Identity.AddClaim(new Claim(ClaimTypes.Surname, userInfo.Name.LastName));

                    return Task.CompletedTask;
                };
                options.ClientId = Settings.AppleClientId;
                options.KeyId = Settings.AppleKeyId;
                options.TeamId = Settings.AppleTeamId;
                options.GenerateClientSecret = true;
                options.UsePrivateKey(_ => new PhysicalFileInfo(new FileInfo(Settings.ApplePrivateKeyPath)));
            });
            authentication.AddScheme<PhoneAuthOptions, PhoneAuthHandler>(Constants.Auth.Phone.SchemeName,
                options => options.CallbackPath = Constants.Auth.Phone.CallbackPath);
            /*
            authentication.AddMicrosoftAccount(options => {
                options.ClientId = Settings.MicrosoftAccountClientId;
                options.ClientSecret = Settings.MicrosoftAccountClientSecret;
                // That's for personal account authentication flow
                options.AuthorizationEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
                options.TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            });
            */

            fusionWebServer.ConfigureAuthEndpoint(_ => new() {
                DefaultSignInScheme = GoogleDefaults.AuthenticationScheme,
                SignInPropertiesBuilder = (_, properties) => {
                    properties.IsPersistent = true;
                },
            });
        }

        // System properties
        rpcHost.AddApi<ISystemProperties, SystemProperties>();

        // Secure tokens
        rpcHost.AddApi<ISecureTokens, SecureTokens>();
        services.AddSingleton<ISecureTokensBackend, SecureTokensBackend>(); // Used by HttpSessionExt, server-side logic in AppBase, etc.

        // IAuth
        if (rpcHost.IsApiHost)
            rpc.AddServer<IAuth>(); // IAuth is registered below

        // Accounts
        rpcHost.AddApiOrLocal<IAccounts, Accounts>(); // Used by Chats, etc.
        rpcHost.AddBackend<IAccountsBackend, AccountsBackend>();
        rpcHost.AddBackend<IUsersUpgradeBackend, UsersUpgradeBackend>();

        // UserPresences
        rpcHost.AddApiOrLocal<IUserPresences, UserPresences>(); // Used by Authors -> Chats, etc.
        rpcHost.AddBackend<IUserPresencesBackend, UserPresencesBackend>();

        // Avatars
        rpcHost.AddApiOrLocal<IAvatars, Avatars>(); // Used by Authors -> Chats, etc.
        rpcHost.AddBackend<IAvatarsBackend, AvatarsBackend>();

        // ChatPositions
        rpcHost.AddApi<IChatPositions, ChatPositions>();
        rpcHost.AddBackend<IChatPositionsBackend, ChatPositionsBackend>();

        // ServerKvas
        rpcHost.AddApiOrLocal<IServerKvas, ServerKvas>(); // Used by Authors, Avatars -> Chats, etc.
        rpcHost.AddBackend<IServerKvasBackend, ServerKvasBackend>();

        // PhoneAuth
        rpcHost.AddApi<IPhoneAuth, PhoneAuth>(); // Requires Redis & ITextMessageSender

        // Emails
        rpcHost.AddApi<IEmails, Emails>();

        // Phones
        rpcHost.AddApi<IPhones, Phones>();

        // TimeZones
        rpcHost.AddApi<ITimeZones, TimeZones>();

        // Mobile authentication
        rpcHost.AddApi<IMobileSessions, MobileSessions>();
#pragma warning disable CS0618
        if (rpcHost.IsApiHost)
            rpc.AddServer<IMobileAuth, IMobileSessions>(); // ~ Alias of IMobileSessions
#pragma warning restore CS0618

        // NOTE(AY): We don't have a clear separation between the backend and the front-end
        // due to IAuth & IAuthBackend, so these services are always local, and thus
        // they drag the DB, Redis & everything they depend on.
        // That's why we can't just exit here if we're operating as a backend client.

        if (!isBackendClient) {
            services.AddSingleton<ContactGreeter>()
                .AddHostedService(c => c.GetRequiredService<ContactGreeter>());
        }

        // TOTP codes - used by IPhoneAuth (API)
        services.AddSingleton<TotpCodes>();
        services.AddSingleton<TotpSecrets>(); // Requires Redis

        // Email sender - used by IEmails (API)
        services.AddSingleton<IEmailSender, EmailSender>();

        // Text message sender / Twilio - used by IPhoneAuth (API)
        if (Settings.IsTwilioEnabled) {
            services.AddSingleton<ITwilioRestClient>(_ => {
                TwilioClient.Init(Settings.TwilioApiKey, Settings.TwilioApiSecret, Settings.TwilioAccountSid);
                return TwilioClient.GetRestClient();
            });
            services.AddSingleton<ITextMessageSender, TwilioTextMessageSender>();
        }
        else
            services.AddSingleton<ITextMessageSender, LogOnlyTextMessageSender>();

        // IAuth & IAuthBackend
        fusion.AddDbAuthService<UsersDbContext, DbSessionInfo, DbUser, string>(auth => {
            auth.ConfigureAuthService(_ => new() {
                MinUpdatePresencePeriod = Constants.Session.MinUpdatePresencePeriod,
            });
            auth.ConfigureSessionInfoTrimmer(_ => new DbSessionInfoTrimmer<UsersDbContext>.Options {
                MaxSessionAge = TimeSpan.FromDays(180),
            });
        });
        services.AddSingleton<UserNamer>();
        services.AddSingleton<ClaimMapper>();
        commander.AddService<AuthCommandFilters>();
        commander.AddService<AuthBackendCommandFilters>();

        // DbSessionInfoRepo replacement
        services.AddSingleton<DbSessionInfoRepo>();
        services.AddAlias<IDbSessionInfoRepo<UsersDbContext, DbSessionInfo, string>, DbSessionInfoRepo>();
        // DbUserRepo replacement
        services.AddSingleton<DbUserRepo>();
        services.AddAlias<IDbUserRepo<UsersDbContext, DbUser, string>, DbUserRepo>();

        // ServerAuthHelper replacement
        services.AddScoped<ServerAuthHelper, AppServerAuthHelper>(); // Replacing the default one w/ own
        fusionWebServer.ConfigureServerAuthHelper(_ => new() {
            NameClaimKeys = [],
            SessionInfoUpdatePeriod = Constants.Session.SessionInfoUpdatePeriod,
            AllowSignIn = HostInfo.IsDevelopmentInstance
                ? ServerAuthHelper.Options.AllowAnywhere
                : ServerAuthHelper.Options.AllowOnCloseWindowRequest,
            AllowChange = ServerAuthHelper.Options.AllowOnCloseWindowRequest,
            AllowSignOut = ServerAuthHelper.Options.AllowOnCloseWindowRequest,
        });

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<UsersDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, UsersDbInitializer>();
        dbModule.AddDbContextServices<UsersDbContext>(services, db => {
            // Overriding / adding extra DbAuthentication services
            services.AddSingleton<IDbUserIdHandler<string>, DbUserIdHandler>();
            db.AddEntityConverter<DbSessionInfo, SessionInfo, DbSessionInfoConverter>();
            db.AddEntityResolver<string, DbUserIdentity<string>>();
            db.AddEntityResolver<string, DbKvasEntry>();
            db.AddEntityResolver<string, DbAccount>();
            db.AddEntityResolver<string, DbAvatar>();
            db.AddEntityResolver<string, DbUserPresence>();
            db.AddEntityResolver<string, DbChatPosition>();
        });
    }
}
