using ActualChat.Db;
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
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Fusion.Server;
using Stl.Fusion.Server.Authentication;
using Stl.Plugins;
using Stl.Redis;

namespace ActualChat.Users.Module;

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
            services.TryAddSingleton<IDbUserIdHandler<string>, DbUserIdHandler>();
            db.AddEntityResolver<string, DbUserIdentity<string>>();
            db.AddEntityResolver<string, DbAccount>();
            db.AddEntityResolver<string, DbUserPresence>();
            db.AddEntityResolver<string, DbUserAvatar>();
            db.AddEntityResolver<string, DbUserContact>();
            db.AddEntityResolver<string, DbChatReadPosition>();
            db.AddEntityResolver<string, DbKvasEntry>();
            db.AddShardLocalIdGenerator(dbContext => dbContext.UserAvatars,
                (e, shardKey) => e.UserId == shardKey, e => e.LocalId);

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
            if (commandAssembly == typeof(Account).Assembly)
                return true;
            return false;
        });
        var fusion = services.AddFusion();

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
        services.AddSingleton<IRandomNameGenerator, RandomNameGenerator>();
        services.AddSingleton<UserNamer>();
        services.AddSingleton<IUsersTempBackend, UsersTempBackend>();
        fusion.AddComputeService<IAccounts, Accounts>();
        fusion.AddComputeService<IAccountsBackend, AccountsBackend>();
        fusion.AddComputeService<IUserPresences, UserPresences>();
        fusion.AddComputeService<IUserAvatars, UserAvatars>();
        fusion.AddComputeService<IUserAvatarsBackend, UserAvatarsBackend>();
        fusion.AddComputeService<IUserContacts, UserContacts>();
        fusion.AddComputeService<IUserContactsBackend, UserContactsBackend>();
        fusion.AddComputeService<ISessionOptionsBackend, SessionOptionsBackend>();
        fusion.AddComputeService<IChatReadPositions, ChatReadPositions>();
        fusion.AddComputeService<IChatReadPositionsBackend, ChatReadPositionsBackend>();
        fusion.AddComputeService<IServerKvas, ServerKvas>();
        fusion.AddComputeService<IServerKvasBackend, ServerKvasBackend>();
        fusion.AddComputeService<IRecentEntries, RecentEntries>();
        fusion.AddComputeService<IRecentEntriesBackend, RecentEntriesBackend>();

        // ChatUserSettings
        services.AddSingleton(c => {
            var chatRedisDb = c.GetRequiredService<RedisDb<UsersDbContext>>();
            return chatRedisDb.GetSequenceSet<ChatUserSettings>("seq." + nameof(ChatUserSettings));
        });
        fusion.AddComputeService<ChatUserSettingsService>();
        services.AddSingleton<IChatUserSettings>(c => c.GetRequiredService<ChatUserSettingsService>());
        services.AddSingleton<IChatUserSettingsBackend>(c => c.GetRequiredService<ChatUserSettingsService>());

       // API controllers
        services.AddMvc().AddApplicationPart(GetType().Assembly);
    }
}
