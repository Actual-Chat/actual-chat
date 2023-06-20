using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Redis.Module;
using ActualChat.Users.Db;
using ActualChat.Users.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders.Physical;
using Newtonsoft.Json;
using Stl.Fusion.Authentication.Services;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Fusion.Server;
using Stl.Fusion.Server.Authentication;

namespace ActualChat.Users.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class UsersServiceModule : HostModule<UsersSettings>
{
    public UsersServiceModule(IServiceProvider services) : base(services) { }

    public static HttpMessageHandler? GoogleBackchannelHttpHandler { get; set; }

    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        // ASP.NET Core authentication providers
        var authentication = services.AddAuthentication(options => {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        });
        authentication.AddCookie(options => {
            options.LoginPath = "/signIn";
            options.LogoutPath = "/signOut";
            if (IsDevelopmentInstance)
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
            options.BackchannelHttpHandler = GoogleBackchannelHttpHandler;
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

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<UsersDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, UsersDbInitializer>();
        dbModule.AddDbContextServices<UsersDbContext>(services, Settings.Db, db => {
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

        // Commander & Fusion
        var commander = services.AddCommander();
        commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<UsersDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<UsersDbContext>))
                return true;
            // 2. Make sure it's intact only for Stl.Fusion.Authentication + local commands
            var commandAssembly = commandType.Assembly;
            if (commandAssembly == typeof(Auth_EditUser).Assembly
                && OrdinalEquals(commandType.Namespace, typeof(Auth_EditUser).Namespace))
                return true;
            if (commandAssembly == typeof(IAccounts).Assembly) // Users.Contracts assembly
                return true;
            return false;
        });
        var fusion = services.AddFusion();

        // Auth
        fusion.AddDbAuthService<UsersDbContext, DbSessionInfo, DbUser, string>(auth => {
            auth.ConfigureAuthService(_ => new() {
                MinUpdatePresencePeriod = Constants.Session.MinUpdatePresencePeriod,
            });
        });
        services.AddScoped<ServerAuthHelper, AppServerAuthHelper>(); // Replacing the default one w/ own
        var fusionWebAuth = fusion.AddWebServer().AddAuthentication();
        fusionWebAuth.ConfigureSignInController(_ => new() {
            DefaultScheme = GoogleDefaults.AuthenticationScheme,
            SignInPropertiesBuilder = (_, properties) => {
                properties.IsPersistent = true;
            },
        });
        fusionWebAuth.ConfigureServerAuthHelper(_ => new() {
            NameClaimKeys = Array.Empty<string>(),
        });
        commander.AddCommandService<AuthCommandFilters>();
        services.AddSingleton<ClaimMapper>();
        services.Replace(ServiceDescriptor.Singleton<IDbUserRepo<UsersDbContext, DbUser, string>, DbUserRepo>());
        services.AddTransient(c => (DbUserRepo)c.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>());

        // Module's own services
        services.AddSingleton<UserNamer>();
        fusion.AddService<ISystemProperties, SystemProperties>();
        fusion.AddService<IAccounts, Accounts>();
        fusion.AddService<IAccountsBackend, AccountsBackend>();
        fusion.AddService<IUserPresences, UserPresences>();
        fusion.AddService<IUserPresencesBackend, UserPresencesBackend>();
        fusion.AddService<IAvatars, Avatars>();
        fusion.AddService<IAvatarsBackend, AvatarsBackend>();
        fusion.AddService<IChatPositions, ChatPositions>();
        fusion.AddService<IChatPositionsBackend, ChatPositionsBackend>();
        fusion.AddService<IServerKvas, ServerKvas>();
        fusion.AddService<IServerKvasBackend, ServerKvasBackend>();
        commander.AddCommandService<IUsersUpgradeBackend, UsersUpgradeBackend>();

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
    }
}
