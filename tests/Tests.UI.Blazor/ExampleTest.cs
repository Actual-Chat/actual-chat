using System.Threading.Tasks;
using ActualChat.Testing;
using ActualChat.Tests;
using ActualChat.Todos.UI.Blazor;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Fusion.Blazor;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.UI.Blazor
{
    public class ExampleTest : TestBase
    {
        public ExampleTest(ITestOutputHelper @out) : base(@out) { }

        [Fact]
        public async Task PlusButtonExists()
        {
            using var appHost = await TestHosts.NewAppHost();
            using var blazorTester = appHost.NewBlazorTester();
            blazorTester.JSInterop.Mode = JSRuntimeMode.Loose;
            var authStateProvider = blazorTester.ScopedAppServices.GetRequiredService<AuthStateProvider>();
            var cut = blazorTester.RenderComponent<TodoPage>(parameters => {
                parameters.AddCascadingValue(authStateProvider.GetAuthenticationStateAsync());
            });
            cut.MarkupMatches("class=\"fas fa-plus-square\"");
        }

        [Fact]
        public async Task SessionTest()
        {
            using var appHost = await TestHosts.NewAppHost();
            var services = appHost.Services;
            var sessionFactory = services.GetRequiredService<ISessionFactory>();
            var sessionA = sessionFactory.CreateSession();

            Assert.NotNull(sessionA);
            sessionA.ToString().Length.Should().BeGreaterOrEqualTo(16);
        }
    }
}
