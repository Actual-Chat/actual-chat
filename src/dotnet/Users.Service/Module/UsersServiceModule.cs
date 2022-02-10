using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using ActualChat.Users.Db;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Fusion.Server;
using Stl.Fusion.Server.Controllers;
using Stl.Plugins;
using Stl.Redis;

namespace ActualChat.Users.Module;

public class UsersServiceModule : HostModule<UsersSettings>
{
    public UsersServiceModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public UsersServiceModule(IPluginHost plugins) : base(plugins) { }

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
        services.RegisterLocalIdGenerator<UsersDbContext, DbUserAvatar>();

        // DB
        var dbModule = Plugins.GetPlugins<DbModule>().Single();
        dbModule.AddDbContextServices<UsersDbContext>(services, Settings.Db);
        services.AddSingleton<IDbInitializer, UsersDbInitializer>();
        services.AddDbContextServices<UsersDbContext>(dbContext => {
            // Overriding / adding extra DbAuthentication services
            services.AddSingleton(_ => new DbAuthService<UsersDbContext>.Options() {
                MinUpdatePresencePeriod = TimeSpan.FromSeconds(45),
            });
            services.TryAddSingleton<IDbUserIdHandler<string>, DbUserIdHandler>();
            services.TryAddSingleton<DbUserByNameResolver>();
            dbContext.AddEntityResolver<string, DbUserIdentity<string>>();
            dbContext.AddEntityResolver<string, DbUserState>();
            dbContext.AddEntityResolver<string, DbUserAuthor>();
            dbContext.AddEntityResolver<string, DbUserAvatar>();

            // DB authentication services
            dbContext.AddAuthentication<DbSessionInfo, DbUser, string>((_, options) => {
                options.MinUpdatePresencePeriod = TimeSpan.FromSeconds(55);
            });
        });

        // Fusion services
        var fusion = services.AddFusion();
        services.AddCommander().AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<UsersDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<UsersDbContext>))
                return true;
            // 2. Make sure it's intact only for Stl.Fusion.Authentication + local commands
            var commandAssembly = commandType.Assembly;
            if (commandAssembly == typeof(EditUserCommand).Assembly
                && string.Equals(commandType.Namespace, typeof(EditUserCommand).Namespace, StringComparison.Ordinal))
                return true;
            if (commandAssembly == typeof(UserInfo).Assembly)
                return true;
            return false;
        });


        // Auth services
        var fusionAuth = fusion.AddAuthentication();
        services.TryAddScoped<ServerAuthHelper>(); // Replacing the default one w/ own
        fusionAuth.AddServer(
            signInControllerSettingsFactory: _ => SignInController.DefaultSettings with {
                DefaultScheme = GoogleDefaults.AuthenticationScheme,
                SignInPropertiesBuilder = (_, properties) => {
                    properties.IsPersistent = true;
                },
            },
            serverAuthHelperSettingsFactory: _ => new() {
                NameClaimKeys = Array.Empty<string>(),
            });

        // Module's own services
        services.AddMvc().AddApplicationPart(GetType().Assembly);
        services.AddSingleton<IRandomNameGenerator, RandomNameGenerator>();
        services.AddSingleton<UserNamer>();
        fusion.AddComputeService<IUserInfos, UserInfos>();
        fusion.AddComputeService<IUserStates, UserStates>();
        fusion.AddComputeService<IUserAuthors, UserAuthors>();
        fusion.AddComputeService<IUserAuthorsBackend, UserAuthorsBackend>();
        fusion.AddComputeService<IUserAvatars, UserAvatars>();
        fusion.AddComputeService<IUserAvatarsBackend, UserAvatarsBackend>();
        fusion.AddComputeService<ISessionOptionsBackend, SessionOptionsBackend>();
        services.AddCommander()
            .AddCommandService<AuthServiceCommandFilters>();
        services.AddSingleton<ClaimMapper>();
        services.Replace(ServiceDescriptor.Singleton<IDbUserRepo<UsersDbContext, DbUser, string>, DbUserRepository>());

        // ChatUserSettings
        services.AddSingleton(c => {
            var chatRedisDb = c.GetRequiredService<RedisDb<UsersDbContext>>();
            return chatRedisDb.GetSequenceSet<ChatUserSettings>("seq." + nameof(ChatUserSettings));
        });
        fusion.AddComputeService<ChatUserSettingsService>();
        services.AddSingleton<IChatUserSettings>(c => c.GetRequiredService<ChatUserSettingsService>());
        services.AddSingleton<IChatUserSettingsBackend>(c => c.GetRequiredService<ChatUserSettingsService>());
    }
}
