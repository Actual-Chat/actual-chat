using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Host;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi.Models;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Blazor;
using Stl.Fusion.Bridge;
using Stl.Fusion.Client;
using Stl.Fusion.Server;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Collections;
using Stl.Plugins;

namespace ActualChat.Host
{
    public class Startup
    {
        private IConfiguration Cfg { get; }
        private IWebHostEnvironment Env { get; }
        private IPluginHost Plugins { get; set; } = null!;
        private HostSettings HostSettings => Plugins.GetRequiredService<HostSettings>();
        private ILogger Log => Plugins?.GetService<ILogger<Startup>>() ?? NullLogger<Startup>.Instance;

        public Startup(IConfiguration cfg, IWebHostEnvironment environment)
        {
            Cfg = cfg;
            Env = environment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // Logging
            services.AddLogging(logging => {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning);
                if (Env.IsDevelopment()) {
                    logging.AddFilter(typeof(App).Namespace, LogLevel.Information);
                    logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Information);
                    logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
                    logging.AddFilter("Stl.Fusion.Operations", LogLevel.Information);
                }
            });

            // Other services shared together with plugins
            services.AddSingleton(new HostInfo() {
                HostKind = HostKind.WebServer,
                ServiceScope = ServiceScope.Server,
                Environment = Env.EnvironmentName,
                Configuration = Cfg,
            });
            services.AddSettings<HostSettings>();

            // Creating plugins
            Plugins = new PluginHostBuilder(new ServiceCollection().Add(services)).Build();
            services.AddSingleton(Plugins);

            // Fusion services
            services.AddSingleton(new Publisher.Options() { Id = HostSettings.PublisherId });
            var fusion = services.AddFusion();
            var fusionServer = fusion.AddWebServer();
            var fusionClient = fusion.AddRestEaseClient();
            var fusionAuth = fusion.AddAuthentication().AddServer(
                signInControllerOptionsBuilder: (_, options) => {
                    options.DefaultScheme = MicrosoftAccountDefaults.AuthenticationScheme;
                },
                authHelperOptionsBuilder: (_, options) => {
                    options.NameClaimKeys = Array.Empty<string>();
                });

            // ASP.NET Core authentication providers
            services.AddAuthentication(options => {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            }).AddCookie(options => {
                options.LoginPath = "/signIn";
                options.LogoutPath = "/signOut";
                if (Env.IsDevelopment())
                    options.Cookie.SecurePolicy = CookieSecurePolicy.None;
            }).AddMicrosoftAccount(options => {
                options.ClientId = HostSettings.MicrosoftAccountClientId;
                options.ClientSecret = HostSettings.MicrosoftAccountClientSecret;
                // That's for personal account authentication flow
                options.AuthorizationEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
                options.TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            }).AddGitHub(options => {
                options.ClientId = HostSettings.GitHubClientId;
                options.ClientSecret = HostSettings.GitHubClientSecret;
                options.Scope.Add("read:user");
                options.Scope.Add("user:email");
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            });

            // Web
            services.AddRouting();
            services.AddMvc().AddApplicationPart(Assembly.GetExecutingAssembly());
            services.AddServerSideBlazor(o => o.DetailedErrors = true);
            fusionAuth.AddBlazor(o => { }); // Must follow services.AddServerSideBlazor()!

            // Swagger & debug tools
            services.AddSwaggerGen(c => {
                c.SwaggerDoc("v1", new OpenApiInfo {
                    Title = "ActualChat API", Version = "v1"
                });
            });

            // Injecting plugin services
            Plugins.GetPlugins<HostModule>().Apply(m => m.InjectServices(services));

            // Blazor Server services
            Program.ConfigureBlazorServerServices(services, Plugins);
        }

        public void Configure(IApplicationBuilder app)
        {
            // This server serves static content from Blazor Client,
            // and since we don't copy it to local wwwroot,
            // we need to find Client's wwwroot in bin/(Debug/Release) folder
            // and set it as this server's content root.
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            var binCfgPart = Regex.Match(baseDir, @"[\\/]bin[\\/]\w+[\\/]").Value;
            var wwwRootPath = Path.Combine(baseDir, "wwwroot");
            if (!Directory.Exists(Path.Combine(wwwRootPath, "_framework")))
                // This is a regular build, not a build produced w/ "publish",
                // so we remap wwwroot to the client's wwwroot folder
                wwwRootPath = Path.GetFullPath(Path.Combine(baseDir,
                    $"../../../../UI.Blazor.Host/{binCfgPart}/net5.0/wwwroot"));
            Env.WebRootPath = wwwRootPath;
            Env.WebRootFileProvider = new PhysicalFileProvider(Env.WebRootPath);
            StaticWebAssetsLoader.UseStaticWebAssets(Env, Cfg);

            if (Env.IsDevelopment()) {
                app.UseDeveloperExceptionPage();
                app.UseWebAssemblyDebugging();
            }
            else {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }
            app.UseHttpsRedirection();

            app.UseWebSockets(new WebSocketOptions() {
                KeepAliveInterval = TimeSpan.FromSeconds(30),
            });
            app.UseFusionSession();

            // Static + Swagger
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();
            app.UseSwagger();
            app.UseSwaggerUI(c => {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
            });

            // API controllers
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints => {
                endpoints.MapBlazorHub();
                endpoints.MapFusionWebSocketServer();
                endpoints.MapControllers();
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}
