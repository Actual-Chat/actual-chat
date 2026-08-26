using System.Security.Claims;
using ActualChat.Authentication;
using ActualChat.Db.Module;
using ActualChat.Kvas;
using ActualChat.Redis.Module;
using ActualChat.Security;
using ActualChat.Users.Db;
using ActualChat.Users.Email;
using ActualChat.Users.Flows;
using ActualChat.Users.Internal;
using ActualChat.Users.Models;
using ActualChat.Users.Phone;
using ActualChat.Users.Phone.Internal;
using ActualLab.Fusion.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
                // A handshake token, not a login: its only consumer is AuthHelper.UpdateAuthState on
                // the close flow, so it just has to outlive the callback -> /fusion/close hop.
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                options.SlidingExpiration = false;
            });
            authentication.AddGoogle(options => {
                options.ClientId = Settings.GoogleClientId;
                options.ClientSecret = Settings.GoogleClientSecret;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                // Force Google's account chooser on every sign-in. Without this Google
                // silently re-authenticates the last-used account, so a user who signed
                // out of the app can't pick a different account on the next sign-in.
                options.AdditionalAuthorizationParameters["prompt"] = "select_account";
                // Pin the scopes explicitly — the package's defaults already include
                // these, but locking them in protects us from a future package update
                // silently dropping any of them.
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                // GoogleOptions defaults don't map "picture" into a claim — add it
                // so the user's profile picture URL is captured for downstream use
                // (e.g., seeding the avatar on first sign-in). Provider-scoped key
                // ("google/picture") so future providers can carry their own.
                options.ClaimActions.MapJsonKey(Constants.User.Claims.GooglePicture, "picture");
                options.ClaimActions.MapJsonKey(
                    AuthSchema.EmailVerifiedClaim, "email_verified", ClaimValueTypes.Boolean);
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

        // The recipient's UI language, for any server-side text: notifications now, email next.
        services.AddSingleton<UserLocalizers>();

        if (rpcHost.IsApiHost) {
            services.AddSingleton<AuthHelper>(); // Used by ApiHost-s
            services.AddSingleton<ClaimMapper>(); // Used by ServerAuth
        }

        // Sessions
        rpcHost.AddBackend<ISessionsBackend, SessionsBackend>();
        var usesSessionsBackendImpl = rpcHost.HostInfo.Roles.GetBackendServiceMode<ISessionsBackend>().UsesImplementation();
        if (usesSessionsBackendImpl) {
            // The services below are used only by SessionsBackend
            services.AddSingleton(_ => new DbSessionTrimmer.Options());
            services.AddSingleton<DbSessionTrimmer>()
                .AddHostedService(c => c.GetRequiredService<DbSessionTrimmer>());
        }

        // SessionTemporals
        rpcHost.AddApi<ISessionTemporals, SessionTemporals>();
        rpcHost.AddBackend<ISessionTemporalsBackend, SessionTemporalsBackend>();

        // Accounts
        rpcHost.AddLocalApi<IAccounts, Accounts>(); // Used by Chats, etc.
        rpcHost.AddBackend<IAccountsBackend, AccountsBackend>();
        var usesAccountsBackendImpl = rpcHost.HostInfo.Roles.GetBackendServiceMode<IAccountsBackend>().UsesImplementation();
        if (usesAccountsBackendImpl)
            services.AddSingleton<AccountNameValidator>(); // Used by AccountsBackend
        // UserPresences
        rpcHost.AddLocalApi<IUserPresences, UserPresences>(); // Used by Authors -> Chats, etc.
        rpcHost.AddBackend<IUserPresencesBackend, UserPresencesBackend>();

        // Avatars
        rpcHost.AddLocalApi<IAvatars, Avatars>(); // Used by Authors -> Chats, etc.
        rpcHost.AddBackend<IAvatarsBackend, AvatarsBackend>();
        services.AddSingleton<AvatarPictures>(); // Used by AvatarPicturesController for caching

        // ChatPositions
        rpcHost.AddApi<IChatPositions, ChatPositions>();
        rpcHost.AddBackend<IChatPositionsBackend, ChatPositionsBackend>();

        // ChatUsages
        rpcHost.AddApi<IChatUsages, ChatUsages>();
        rpcHost.AddBackend<IChatUsagesBackend, ChatUsagesBackend>();

        // UserSettings, ServerSettings and ServerKvas
        rpcHost.AddLocalApi<IUserSettings, UserSettings>();
        rpcHost.AddLocalApi<IServerKvas, ServerKvas>(); // Used by Authors, Avatars -> Chats, etc.
        rpcHost.AddBackend<IServerKvasBackend, ServerKvasBackend>();

        // PhoneAuth
        rpcHost.AddApi<IPhoneAuth, PhoneAuth>(); // Requires Redis & IVerificationCodeSender

        // EmailAuth
        rpcHost.AddApi<IEmailAuth, EmailAuth>(); // Requires Redis & IEmailSender

        // NativeAuth (iOS/Android OAuth)
        if (rpcHost.IsApiHost)
            rpcHost.AddApi<INativeAuth, NativeAuth>(); // Requires ASP.NET auth options

        // Emails
        rpcHost.AddApi<IEmails, Emails>();
        rpcHost.AddBackend<IEmailsBackend, EmailsBackend>();

        // Phones
        rpcHost.AddApi<IPhones, Phones>();

        // TimeZones
        rpcHost.AddApi<ITimeZones, TimeZones>();

        // Mobile authentication
        rpcHost.AddApi<IMobileSessions, MobileSessions>();

        // reCAPTCHA
        rpcHost.AddLocalApi<ICaptcha, Captcha>();
        services.AddSingleton<CaptchaProofValidator>(); // Used by IPhoneAuth & IEmailAuth (API)

        // NOTE(AY): We don't have a clear separation between the backend and the front-end
        // due to IAuth, ISessionsBackend & IAccountsBackend, so these services are always local, and thus
        // they drag the DB, Redis & everything they depend on.
        // That's why we can't just exit here if we're operating as a backend client.

        if (!isBackendClient) {
            services.AddSingleton<ContactGreeter>()
                .AddHostedService(c => c.GetRequiredService<ContactGreeter>());
            services.AddHttpClient(UserSignInFlow.HttpClientName);

            services.AddFlows()
                .Add<UserSignInFlow>()
                .Add<DigestFlow>()
                .Add<AccountMigrationFlow>();
        }

        // TOTP codes - used by IPhoneAuth & IEmailAuth (API)
        services.AddSingleton<TotpCodes>(); // Requires Redis

        // Email sender - used by IEmailAuth (API) & Emails
        services.AddSingleton<IEmailSender, EmailSender>();

        // Verification code channels: each available one is registered under its own key,
        // the composite picks among them at send time
        var isTelegramEnabled = Settings.IsTelegramGatewayEnabled;
        var isTwilioEnabled = Settings.IsTwilioEnabled;
        var isSmsToEnabled = Settings.IsSMSToEnabled;

        if (!isTelegramEnabled && !isTwilioEnabled && !isSmsToEnabled)
            services.AddSingleton<IVerificationCodeSender, LogOnlyVerificationCodeSender>();
        else {
            if (isTelegramEnabled)
                services.AddKeyedSingleton<IVerificationCodeSender>(
                    "Telegram", (c, _) => new TelegramGatewayCodeSender(c));
            if (isSmsToEnabled)
                services.AddKeyedSingleton<IVerificationCodeSender>(
                    "SMSTo", (c, _) => new SMSToVerificationCodeSender(c));
            if (isTwilioEnabled) {
                services.AddSingleton<ITwilioRestClient>(_ => {
                    TwilioClient.Init(Settings.TwilioApiKey, Settings.TwilioApiSecret, Settings.TwilioAccountSid);
                    return TwilioClient.GetRestClient();
                });
                services.AddKeyedSingleton<IVerificationCodeSender>(
                    "Twilio", (c, _) => new TwilioVerificationCodeSender(c));
            }
            // Off development this stays unregistered on purpose: it reports every code as delivered by SMS,
            // so with Telegram alone it would turn "the number has no Telegram" into a silent success
            if (!isTwilioEnabled && !isSmsToEnabled && HostInfo.IsDevelopmentInstance)
                services.AddKeyedSingleton<IVerificationCodeSender>(
                    "LogOnly", (c, _) => new LogOnlyVerificationCodeSender(c));
            services.AddSingleton<IVerificationCodeSender, CompositeVerificationCodeSender>();
        }
        services.AddHttpClient(nameof(TelegramGatewayCodeSender))
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient(nameof(SMSToVerificationCodeSender))
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<UsersDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, UsersDbInitializer>();
        dbModule.AddDbContextServices<UsersDbContext>(services, db => {
            // Auth-related services
            db.AddEntityResolver<string, DbSession>();
            // Other services
            db.AddEntityResolver<string, DbKvasEntry>();
            db.AddEntityResolver<string, DbAccount>(_ => new() {
                QueryTransformer = query => query.Include(a => a.Identities),
            });
            db.AddEntityResolver<string, DbAccountIdentity>();
            db.AddEntityResolver<string, DbAvatar>();
            db.AddEntityResolver<string, DbUserPresence>();
            db.AddEntityResolver<string, DbChatPosition>();
        });
    }
}
