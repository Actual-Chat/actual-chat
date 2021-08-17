using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ActualChat.Host;
using ActualChat.Hosting;
using FluentAssertions;
using FluentAssertions.Common;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stl.CommandR;
using Stl.Fusion.Authentication;
using Stl.Fusion.Authentication.Commands;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;


namespace Tests.UI.Blazor
{
    public class UiTest : TestBase
    {
        public UiTest(ITestOutputHelper @out) : base(@out)
        {
        }

        private IHost host;
        private IServiceProvider services;
        
        [Fact]
        public async Task SessionTest()
        {
            await RunHostAsync();
            var sessionFactory = services.GetRequiredService<ISessionFactory>();
            var sessionA = sessionFactory.CreateSession();
            Assert.NotNull(sessionA);
            sessionA.ToString().Length.Should().BeGreaterOrEqualTo(16);
        }
        
        [Fact]
        public async Task AuthTest()
        {
            await RunHostAsync();
            // await RunHostAsync();
            var sessionFactory = services.GetRequiredService<ISessionFactory>();
            var sessionA = sessionFactory.CreateSession();
            var authFactory = services.GetRequiredService<IServerSideAuthService>();

            var ivan = new User("", "Ivan").WithIdentity($"{sessionA}");
            var session = sessionA;

            var user = await authFactory.GetUser(session);
            user.Id.Should().Be(new User(session.Id).Id);
            user.Name.Should().Be(User.GuestName);
            user.IsAuthenticated.Should().BeFalse();

            user.Name.Should().Be(ivan.Name);
            long.TryParse(user.Id, out var _).Should().BeTrue();
        }

        public async Task RunHostAsync()
        {
            host = Host.CreateDefaultBuilder()
                .ConfigureHostConfiguration(builder => {
                    // Looks like there is no better way to set _default_ URL
                    builder.Sources.Insert(0, new MemoryConfigurationSource() {
                        InitialData = new Dictionary<string, string>() {
                            {WebHostDefaults.ServerUrlsKey, "http://localhost:7080"},
                            {"Host:IsTestServer", true.ToString()}
                        }
                    });
                })
                .ConfigureWebHostDefaults(builder => builder
                    .UseDefaultServiceProvider((ctx, options) => {
                        if (ctx.HostingEnvironment.IsDevelopment()) {
                            options.ValidateScopes = true;
                            options.ValidateOnBuild = true;
                        }
                    })
                    .UseStartup<Startup>())
                .Build();

            services = host.Services;

            var dbInitializers = services.GetServices<IDataInitializer>();
            var initTasks = dbInitializers.Select(i => i.Initialize(true)).ToArray();
            
            await Task.WhenAll(initTasks);
            
            host.RunAsync();
            await Task.Delay(500);
        }
    }
}
