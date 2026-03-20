using ActualChat.Testing.Host;
using DotNetEnv.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Core.Server.IntegrationTests;

public class TestAppHostConfigurationTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(TestAppHostConfigurationTest)}", TestAppHostOptions.None, @out)
{
    [Fact]
    public async Task ShouldNotContainEnvConfigurationSource()
    {
        await using var host = await NewAppHost();
        var configRoot = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();
        var envSources = configRoot.Providers
            .Where(p => p is EnvConfigurationProvider)
            .ToList();
        envSources.Should().BeEmpty("TestAppHost must not have EnvConfigurationSource");
    }

    [Fact]
    public async Task ShouldNotResolveToHardcodedPort()
    {
        await using var host = await NewAppHost();
        var config = host.Services.GetRequiredService<IConfiguration>();

        var urls = config["urls"];
        var aspnetUrls = config["ASPNETCORE_URLS"];
        var serverUrls = host.ServerUrls;

        Out.WriteLine($"ServerUrls: {serverUrls}");
        Out.WriteLine($"urls: {urls}");
        Out.WriteLine($"ASPNETCORE_URLS: {aspnetUrls}");

        // Dump all configuration providers and their values for urls/ASPNETCORE_URLS
        var configRoot = (IConfigurationRoot)config;
        foreach (var provider in configRoot.Providers) {
            provider.TryGet("urls", out var u);
            provider.TryGet("ASPNETCORE_URLS", out var au);
            provider.TryGet(WebHostDefaults.ServerUrlsKey, out var su);
            if (u != null || au != null || su != null)
                Out.WriteLine($"  {provider.GetType().Name}: urls={u}, ASPNETCORE_URLS={au}, ServerUrlsKey={su}");
        }

        serverUrls.Should().NotContain("7080", "ServerUrls must use a random port, not the .env default");
        urls?.Should().NotContain("7080", "urls config must not resolve to the .env default port");
        aspnetUrls?.Should().NotContain("7080", "ASPNETCORE_URLS must not resolve to the .env default port");
    }

    [Fact]
    public async Task BaseUriShouldMatchServerUrls()
    {
        await using var host = await NewAppHost();
        var urlMapper = host.Services.UrlMapper();

        Out.WriteLine($"ServerUrls: {host.ServerUrls}");
        Out.WriteLine($"BaseUri: {urlMapper.BaseUri}");

        urlMapper.BaseUri.ToString().TrimEnd('/')
            .Should().Be(host.ServerUrls.TrimEnd('/'),
                "BaseUri must match ServerUrls in tests");
    }
}
