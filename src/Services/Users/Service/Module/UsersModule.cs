using System;
using System.Data;
using ActualChat.Hosting;
using ActualChat.Users.Db;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.CommandR;
using Stl.CommandR.Configuration;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Fusion.Operations.Internal;
using Stl.Fusion.Server;
using Stl.Plugins;

namespace ActualChat.Users.Module
{
    public class UsersHostModule : HostModule
    {
        public UsersHostModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public UsersHostModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
                return; // Server-side only module

            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            services.AddSettings<UsersSettings>();
            var settings = services.BuildServiceProvider().GetRequiredService<UsersSettings>();
            services.AddSingleton<IDbInitializer, UsersDbInitializer>();

            // ASP.NET Core authentication providers
            services.AddAuthentication(options => {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            }).AddCookie(options => {
                options.LoginPath = "/signIn";
                options.LogoutPath = "/signOut";
                if (isDevelopmentInstance)
                    options.Cookie.SecurePolicy = CookieSecurePolicy.None;
            }).AddMicrosoftAccount(options => {
                options.ClientId = settings.MicrosoftAccountClientId;
                options.ClientSecret = settings.MicrosoftAccountClientSecret;
                // That's for personal account authentication flow
                options.AuthorizationEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
                options.TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            }).AddGitHub(options => {
                options.ClientId = settings.GitHubClientId;
                options.ClientSecret = settings.GitHubClientSecret;
                options.Scope.Add("read:user");
                options.Scope.Add("user:email");
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            });

            // Fusion services
            var fusion = services.AddFusion();
            services.AddDbContextFactory<UsersDbContext>(builder => {
                builder.UseNpgsql(settings.Db);
                if (isDevelopmentInstance)
                    builder.EnableSensitiveDataLogging();
            });
            services.AddDbContextServices<UsersDbContext>(dbContext => {
                services.AddSingleton(new CompletionProducer.Options {
                    IsLoggingEnabled = true,
                });
                services.AddTransient(c => new DbOperationScope<UsersDbContext>(c) {
                    IsolationLevel = IsolationLevel.RepeatableRead,
                });
                dbContext.AddOperations((_, o) => {
                    o.UnconditionalWakeUpPeriod = TimeSpan.FromSeconds(isDevelopmentInstance ? 60 : 5);
                });
                dbContext.AddNpgsqlOperationLogChangeTracking();

                // Overriding / adding extra DbAuthentication services
                services.TryAddSingleton<IDbUserIdHandler<string>, DbUserIdHandler>();
                services.TryAddSingleton<DbUserByNameResolver>();
                dbContext.AddEntityResolver<string, DbUserIdentity<string>>();
                dbContext.AddEntityResolver<string, DbUserState>();

                // DB authentication services
                dbContext.AddAuthentication<DbSessionInfo, DbUser, string>((_, options) => {
                    options.MinUpdatePresencePeriod = TimeSpan.FromSeconds(55);
                });
            });
            services.AddCommander().AddHandlerFilter((handler, commandType) => {
                // 1. Check if this is DbOperationScopeProvider<UsersDbContext> handler
                if (handler is not InterfaceCommandHandler<ICommand> ich)
                    return true;
                if (ich.ServiceType != typeof(DbOperationScopeProvider<UsersDbContext>))
                    return true;
                // 2. Make sure it's intact only for Stl.Fusion.Authentication + local commands
                var commandAssembly = commandType.Assembly;
                if (commandAssembly == typeof(EditUserCommand).Assembly && commandType.Namespace == typeof(EditUserCommand).Namespace)
                    return true;
                if (commandAssembly == typeof(UserInfo).Assembly)
                    return true;
                return false;
            });

            // Auth services
            var fusionAuth = fusion.AddAuthentication();
            fusionAuth.AddServer(
                signInControllerOptionsBuilder: (_, options) => {
                    options.DefaultScheme = MicrosoftAccountDefaults.AuthenticationScheme;
                },
                authHelperOptionsBuilder: (_, options) => {
                    options.NameClaimKeys = Array.Empty<string>();
                });

            // Module's own services
            services.AddMvc().AddApplicationPart(GetType().Assembly);
            services.AddSingleton<IUserNameService, UserNameService>();
            fusion.AddComputeService<IUserInfoService, UserInfoService>();
            fusion.AddComputeService<IUserStateService, UserStateService>();
            var commander = services.AddCommander();
            commander.AddCommandService<AuthServiceCommandFilters>();

        }
    }
}
