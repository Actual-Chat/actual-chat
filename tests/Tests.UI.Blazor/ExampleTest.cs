using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Chat.UI.Blazor;
using ActualChat.Testing;
using Blazorise;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Authentication;
using Stl.Fusion.Blazor;
using Stl.Fusion.Client;
using Stl.Fusion.UI;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.UI.Blazor
{
    public class ExampleTest : TestBase
    {
        public ExampleTest(ITestOutputHelper @out) : base(@out) { }

        [Fact]
        public async Task SessionTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var sessionFactory = services.GetRequiredService<ISessionFactory>();
            var sessionA = sessionFactory.CreateSession();

            Assert.NotNull(sessionA);
            sessionA.ToString().Length.Should().BeGreaterOrEqualTo(16);
        }

        [Fact]
        public async Task ChatPageTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            using var blazorTester = appHost.NewBlazorTester();
            var user = await blazorTester.SignIn(new User("", "Bob"));
            user.Id.Value.Should().NotBeNullOrEmpty();
            user.Name.Should().Be("user-Bob");
            var page = blazorTester.RenderComponent<ChatPage>(parameters 
                => parameters.AddAuthenticationState(blazorTester));
            page.RenderCount.Should().Be(1);
            var badges = page.FindComponents<Badge>();
            badges.Count.Should().Be(1);
        }
    }
}
