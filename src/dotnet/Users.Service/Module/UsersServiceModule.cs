using System.Security.Claims;
using ActualChat.Db.Module;
using Microsoft.EntityFrameworkCore;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Redis.Module;
using ActualChat.Roulette;
using ActualChat.Security;
using ActualChat.Users.Db;
using ActualChat.Users.Email;
using ActualChat.Users.Flows;
using ActualChat.Users.Internal;
using ActualChat.Users.Models;
using ActualChat.Users.Phone;
using ActualChat.Users.Phone.Internal;
using ActualChat.Authentication;
using ActualLab.Fusion.Server;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders.Physical;
using Newtonsoft.Json;
using Twilio;
using Twilio.Clients;

namespace ActualChat.Users.Module;

public sealed class UsersServiceModule(IServiceProvider moduleServices)
    : HostModule<UsersSettings>(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IAccountsBackend>() is ServiceMode.Client;
        var rpc = rpcHost.Rpc;
        var commander = rpcHost.Commander;
        var fusion = rpcHost.Fusion;
        var fusionWebServer = fusion.AddWebServer();

        if (rpcHost.IsApiHost) {
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
            authentication.AddScheme<PhoneAuthOptions, PhoneAuthHandler>(
                AuthSchema.Phone,
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

            fusionWebServer.AddAuthEndpoints();
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

        // Legacy IAuth for old clients
#pragma warning disable CS0618 // Obsolete
        rpcHost.AddLocalApi<ILegacyAuth, LegacyAuth>("IAuth");
#pragma warning restore CS0618
        if (rpcHost.IsApiHost) {
            services.AddSingleton<ServerAuth>(); // Used by ApiHost-s
            services.AddSingleton<ClaimMapper>(); // Used by ServerAuth
        }

        // Sessions backend
        rpcHost.AddBackend<ISessionsBackend, SessionsBackend>();
        var usesSessionsBackendImpl = rpcHost.HostInfo.Roles.GetBackendServiceMode<ISessionsBackend>().UsesImplementation();
        if (usesSessionsBackendImpl) {
            // The services below are used only by SessionsBackend
            services.AddSingleton(_ => new SessionsBackend.Options {
                MinUpdatePresencePeriod = Constants.Session.MinUpdatePresencePeriod,
            });
            services.AddSingleton(_ => new DbSessionInfoTrimmer.Options {
                MaxSessionAge = TimeSpan.FromDays(180),
            });
            services.AddSingleton<DbSessionInfoTrimmer>()
                .AddHostedService(c => c.GetRequiredService<DbSessionInfoTrimmer>());
        }

        // Accounts
        rpcHost.AddLocalApi<IAccounts, Accounts>(); // Used by Chats, etc.
        rpcHost.AddBackend<IAccountsBackend, AccountsBackend>();
        var usesAccountsBackendImpl = rpcHost.HostInfo.Roles.GetBackendServiceMode<IAccountsBackend>().UsesImplementation();
        if (usesAccountsBackendImpl)
            services.AddSingleton<UserNamer>(); // Used by AccountsBackend
        rpcHost.AddBackend<IUsersUpgradeBackend, UsersUpgradeBackend>();

        // UserPresences
        rpcHost.AddLocalApi<IUserPresences, UserPresences>(); // Used by Authors -> Chats, etc.
        rpcHost.AddBackend<IUserPresencesBackend, UserPresencesBackend>();

        // Avatars
        rpcHost.AddLocalApi<IAvatars, Avatars>(); // Used by Authors -> Chats, etc.
        rpcHost.AddBackend<IAvatarsBackend, AvatarsBackend>();

        // ChatPositions
        rpcHost.AddApi<IChatPositions, ChatPositions>();
        rpcHost.AddBackend<IChatPositionsBackend, ChatPositionsBackend>();

        // ChatUsages
        rpcHost.AddApi<IChatUsages, ChatUsages>();
        rpcHost.AddBackend<IChatUsagesBackend, ChatUsagesBackend>();

        // ServerKvas
        rpcHost.AddLocalApi<IServerKvas, ServerKvas>(); // Used by Authors, Avatars -> Chats, etc.
        rpcHost.AddLocalApi<IServerSettings, ServerSettings>();
        rpcHost.AddBackend<IServerKvasBackend, ServerKvasBackend>();

        // PhoneAuth
        rpcHost.AddApi<IPhoneAuth, PhoneAuth>(); // Requires Redis & ITextMessageSender

        // EmailAuth
        rpcHost.AddApi<IEmailAuth, EmailAuth>(); // Requires Redis & IEmailSender

        // Emails
        rpcHost.AddApi<IEmails, Emails>();
        rpcHost.AddBackend<IEmailsBackend, EmailsBackend>();

        // Phones
        rpcHost.AddApi<IPhones, Phones>();

        // TimeZones
        rpcHost.AddApi<ITimeZones, TimeZones>();

        // RouletteProfiles
        rpcHost.AddLocalApi<IRouletteProfiles, RouletteProfiles>();
        rpcHost.AddBackend<IRouletteProfilesBackend, RouletteProfilesBackend>();

        // Mobile authentication
        rpcHost.AddApi<IMobileSessions, MobileSessions>();

        // reCAPTCHA
        rpcHost.AddLocalApi<ICaptcha, Captcha>();

        // NOTE(AY): We don't have a clear separation between the backend and the front-end
        // due to IAuth, ISessionsBackend & IAccountsBackend, so these services are always local, and thus
        // they drag the DB, Redis & everything they depend on.
        // That's why we can't just exit here if we're operating as a backend client.

        if (!isBackendClient) {
            services.AddSingleton<ContactGreeter>()
                .AddHostedService(c => c.GetRequiredService<ContactGreeter>());

            services.AddFlows()
                .Add<MasterFlow>()
                .Add<DigestFlow>();
        }

        // TOTP codes - used by IPhoneAuth (API)
        services.AddSingleton<TotpCodes>();
        services.AddSingleton<TotpSecrets>(); // Requires Redis

        // Email sender - used by IEmailAuth (API) & Emails
        services.AddSingleton<IEmailSender, EmailSender>();

        // Text message sender registration - covers all combinations of Twilio / SMS.to availability
        var isTwilioEnabled = Settings.IsTwilioEnabled;
        var isSmsToEnabled = Settings.IsSMSToEnabled;

        if (!isTwilioEnabled && !isSmsToEnabled)
            // Neither enabled -> log-only sender
            services.AddSingleton<ITextMessageSender, LogOnlyTextMessageSender>();
        else if (isTwilioEnabled && isSmsToEnabled) {
            // Both enabled -> use Composite to route +7 through SMS.to, everything else through Twilio
            // Key "SMSTo" is used for numbers starting with +7
            services.AddKeyedSingleton<ITextMessageSender>("SMSTo", (c, _) => new SMSToTextMessageSender(c));

            services.AddSingleton<ITwilioRestClient>(_ => {
                TwilioClient.Init(Settings.TwilioApiKey, Settings.TwilioApiSecret, Settings.TwilioAccountSid);
                return TwilioClient.GetRestClient();
            });
            services.AddKeyedSingleton<ITextMessageSender>("Default", (c, _) => new TwilioTextMessageSender(c));
            services.AddSingleton<ITextMessageSender, CompositeTextMessageSender>();
        }
        else if (isTwilioEnabled) {
            // Only Twilio enabled -> use Twilio directly
            services.AddSingleton<ITwilioRestClient>(_ => {
                TwilioClient.Init(Settings.TwilioApiKey, Settings.TwilioApiSecret, Settings.TwilioAccountSid);
                return TwilioClient.GetRestClient();
            });
            services.AddSingleton<ITextMessageSender, TwilioTextMessageSender>();
        }
        else {
            // Only SMS.to enabled -> use SMS.to directly (also register keyed instance for consistency)
            services.AddKeyedSingleton<ITextMessageSender>("SMSTo", (c, _) => new SMSToTextMessageSender(c));
            services.AddSingleton<ITextMessageSender, SMSToTextMessageSender>();
        }

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<UsersDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, UsersDbInitializer>();
        dbModule.AddDbContextServices<UsersDbContext>(services, db => {
            // Auth-related services
            db.AddEntityResolver<string, DbSessionInfo>();
            db.AddEntityResolver<string, DbUser>(_ => new() {
                QueryTransformer = query => query.Include(u => u.Identities),
            });
            db.AddEntityResolver<string, DbUserIdentity<string>>();

            // Other services
            db.AddEntityResolver<string, DbKvasEntry>();
            db.AddEntityResolver<string, DbAccount>();
            db.AddEntityResolver<string, DbAvatar>();
            db.AddEntityResolver<string, DbUserPresence>();
            db.AddEntityResolver<string, DbChatPosition>();
            db.AddEntityResolver<string, DbRouletteProfilePrefs>();
            db.AddEntityResolver<string, DbRouletteUserSettings>();
        });
    }
}
