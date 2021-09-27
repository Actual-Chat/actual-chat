using System.Threading.Tasks;
using ActualChat.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.UI.Blazor
{
    public class ExampleTest : AppHostTestBase
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
    }
}
