using System;
using System.Linq;
using System.Threading.Tasks;
using ActualChat.Testing;
using ActualChat.Todos;
using ActualChat.Todos.UI.Blazor;
using Blazorise;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
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
        public async Task TodoSummaryBadgeTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            using var blazorTester = appHost.NewBlazorTester();
            var session = blazorTester.Session;
            var uiCommandRunner = blazorTester.ScopedAppServices.GetRequiredService<UICommandRunner>();

            // Create user & sign in
            var user = await blazorTester.SignIn(new User("", "Alex"));
            user.Id.Value.Should().NotBeNullOrEmpty();
            user.Name.Should().Be("Alex");
            user.Identities.Count.Should().Be(1);

            // Render summary badge
            var badge = blazorTester.RenderComponent<TodoSummaryBadge>(
                parameters => parameters.AddAuthenticationState(blazorTester));
            badge.RenderCount.Should().Be(1);
            var badges = badge.FindComponents<Badge>();
            badges.Count.Should().Be(2);
            badges.First().MarkupMatches("<span class=\"badge badge-success\"><b>0</b> done</span>");
            badges.Last().MarkupMatches("<span class=\"badge badge-primary\"><b>0</b> total</span>");

            // Add item
            var (todo1, _) = await uiCommandRunner.Run(
                new AddOrUpdateTodoCommand(session, new Todo() { Title = "1" }));
            todo1.Id.Should().NotBeNullOrEmpty();
            await Task.Delay(100); // Must be enough for user-induced re-render to complete

            // Check content again
            badge.RenderCount.Should().BeGreaterThan(1);
            badges = badge.FindComponents<Badge>();
            badges.Count.Should().Be(2);
            badges.First().MarkupMatches("<span class=\"badge badge-success\"><b>0</b> done</span>");
            badges.Last().MarkupMatches("<span class=\"badge badge-primary\"><b>1</b> total</span>");
        }

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
    }
}
