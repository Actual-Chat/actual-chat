using System.Collections.Generic;
using System.Threading.Tasks;
using ActualChat.Host;
using ActualChat.Todos;
using ActualChat.Todos.UI.Blazor;
using Bunit;
using FluentAssertions;
using Grpc.Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stl.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Fusion.UI;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;


namespace Tests.UI.Blazor
{
    public class UITest : TestBase
    {
        private IHost MainTestHost { get; set; }
        public UITest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public async Task PlusButtonExists()
        {
            // await RunHostAsync();
            await TestHost.CreateBasicHost();
            MainTestHost = TestHost.MainTestHost;

            using var context = new TestContext();
            context.Services.AddFallbackServiceProvider(MainTestHost.Services);
            var cut = context.RenderComponent<TodoPage>();
            cut.MarkupMatches("class=\"fas fa-plus-square\"");
        }
        
        [Fact]
        public async Task SessionTest()
        {
            await TestHost.CreateBasicHost();
            MainTestHost = TestHost.MainTestHost;
            var services = MainTestHost.Services;

            var sessionFactory = services.GetRequiredService<ISessionFactory>();
            var sessionA = sessionFactory.CreateSession();
            
            Assert.NotNull(sessionA);
            sessionA.ToString().Length.Should().BeGreaterOrEqualTo(16);
        }
        
        [Fact]
        public async Task AuthTest()
        {
            await TestHost.CreateBasicHost();
            var MainTestHost = TestHost.MainTestHost;
            var services = MainTestHost.Services;
            // await Host.RunAsync();
            // await Task.Delay(500);
            
            var sessionFactory = services.GetRequiredService<ISessionFactory>();
            var sessionA = sessionFactory.CreateSession();
            var authFactory = services.GetRequiredService<IServerSideAuthService>();

            var ivan = new User("", "Ivan").WithIdentity($"{sessionA}");
            var session = sessionA;

            var user = await authFactory.GetUser(sessionA);
            user.Id.Should().Be(new User(session.Id).Id);
            user.IsAuthenticated.Should().BeFalse();
        }
        
        // public async Task RunHostAsync()
        // {
        //     var host = Host.CreateDefaultBuilder()
        //         .ConfigureHostConfiguration(builder => {
        //             // Looks like there is no better way to set _default_ URL
        //             builder.Sources.Insert(0, new MemoryConfigurationSource() {
        //                 InitialData = new Dictionary<string, string>() {
        //                     {WebHostDefaults.ServerUrlsKey, "http://localhost:7080"},
        //                     {"Host:IsTestServer", true.ToString()}
        //                 }
        //             });
        //         })
        //         .ConfigureWebHostDefaults(builder => builder
        //             .UseDefaultServiceProvider((ctx, options) => {
        //                 if (ctx.HostingEnvironment.IsDevelopment()) {
        //                     options.ValidateScopes = true;
        //                     options.ValidateOnBuild = true;
        //                 }
        //             })
        //             .UseStartup<Startup>())
        //         .Build();
        //
        //     services = host.Services;
        //
        //     var dbInitializers = services.GetServices<IDataInitializer>();
        //     var initTasks = dbInitializers.Select(i => i.Initialize(true)).ToArray();
        //     
        //     await Task.WhenAll(initTasks);
        //     
        //     host.RunAsync();
        //     await Task.Delay(500);
        // }
        

    }
}
