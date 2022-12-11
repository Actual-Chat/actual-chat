using System.Diagnostics.CodeAnalysis;
using ActualChat.Commands;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Redis.Module;
using ActualChat.Users.Db;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Fusion.Server;
using Stl.Fusion.Server.Authentication;
using Stl.Plugins;

namespace ActualChat.Users.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class UsersServiceModule : HostModule<UsersSettings>
{
    public UsersServiceModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public UsersServiceModule(IPluginHost plugins) : base(plugins) { }

    public static HttpMessageHandler? GoogleBackchannelHttpHandler { get; set; }

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        // ASP.NET Core authentication providers
        services.AddAuthentication(options => {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        }).AddCookie(options => {
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
        }).AddGoogle(options => {
            options.ClientId = Settings.GoogleClientId;
            options.ClientSecret = Settings.GoogleClientSecret;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.BackchannelHttpHandler = GoogleBackchannelHttpHandler;
        }).AddMicrosoftAccount(options => {
            options.ClientId = Settings.MicrosoftAccountClientId;
            options.ClientSecret = Settings.MicrosoftAccountClientSecret;
            // That's for personal account authentication flow
            options.AuthorizationEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
            options.TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        });

        // Redis
        var redisModule = Plugins.GetPlugins<RedisModule>().Single();
        redisModule.AddRedisDb<UsersDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Plugins.GetPlugins<DbModule>().Single();
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
            db.AddEntityResolver<string, DbReadPosition>();

            // DB authentication services
            db.AddAuthentication<DbSessionInfo, DbUser, string>(auth => {
                auth.ConfigureAuthService(_ => new() {
                    MinUpdatePresencePeriod = Constants.Presence.SkipCheckInPeriod,
                });
            });
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
            if (commandAssembly == typeof(EditUserCommand).Assembly
                && OrdinalEquals(commandType.Namespace, typeof(EditUserCommand).Namespace))
                return true;
            if (commandAssembly == typeof(IAccounts).Assembly) // Users.Contracts assembly
                return true;
            return false;
        });
        var fusion = services.AddFusion();
        fusion.AddLocalCommandScheduler(Queues.Users);
        commander.AddEventHandlers();

        // Auth
        var fusionAuth = fusion.AddAuthentication();
        services.TryAddScoped<ServerAuthHelper, AppServerAuthHelper>(); // Replacing the default one w/ own
        fusionAuth.AddServer(
            signInControllerOptionsFactory: _ => new() {
                DefaultScheme = GoogleDefaults.AuthenticationScheme,
                SignInPropertiesBuilder = (_, properties) => {
                    properties.IsPersistent = true;
                },
            },
            serverAuthHelperOptionsFactory: _ => new() {
                NameClaimKeys = Array.Empty<string>(),
            });
        commander.AddCommandService<AuthCommandFilters>();
        services.AddSingleton<ClaimMapper>();
        services.Replace(ServiceDescriptor.Singleton<IDbUserRepo<UsersDbContext, DbUser, string>, DbUserRepo>());
        services.AddTransient(c => (DbUserRepo)c.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>());

        // Module's own services
        services.AddSingleton<UserNamer>();
        fusion.AddComputeService<IAccounts, Accounts>();
        fusion.AddComputeService<IAccountsBackend, AccountsBackend>();
        fusion.AddComputeService<IUserPresences, UserPresences>();
        fusion.AddComputeService<IAvatars, Avatars>();
        fusion.AddComputeService<IAvatarsBackend, AvatarsBackend>();
        fusion.AddComputeService<IReadPositions, ReadPositions>();
        fusion.AddComputeService<IReadPositionsBackend, ReadPositionsBackend>();
        fusion.AddComputeService<IServerKvas, ServerKvas>();
        fusion.AddComputeService<IServerKvasBackend, ServerKvasBackend>();
        commander.AddCommandService<IUsersUpgradeBackend, UsersUpgradeBackend>();

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
    }
}
