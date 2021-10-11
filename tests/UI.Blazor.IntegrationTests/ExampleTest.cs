using System.Configuration;
using ActualChat.Host;
using ActualChat.Testing;
using ActualChat.Testing.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Generators.Internal;
using Xunit.Abstractions;

namespace ActualChat.UI.Blazor.IntegrationTests
{
    public class ExampleTest : AppHostTestBase
    {
        private readonly TestSettings _testSettings;
        public ExampleTest(ITestOutputHelper @out, TestSettings testSettings) : base(@out)
        {
            _testSettings = testSettings;
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
