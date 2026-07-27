using System.Net;
using ActualChat.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ActualChat.Core.Server.UnitTests.AspNetCore;

public class ForwardedHeadersOptionsExtTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string Networks = "127.0.0.0/8;::1/128;10.0.0.0/8";
    [Fact]
    public async Task KeepsRemoteAddressOfUnlistedPeer()
    {
        // arrange
        var context = NewContext("203.0.113.9", "198.51.100.7");

        // act
        await Run(context);

        // assert
        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("203.0.113.9"));
    }

    [Fact]
    public async Task AppliesForwardedAddressOfListedPeer()
    {
        // arrange
        var context = NewContext("10.1.2.3", "198.51.100.7");

        // act
        await Run(context);

        // assert
        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("198.51.100.7"));
    }

    [Fact]
    public async Task StopsAtTheOutermostUnlistedEntry()
    {
        // arrange
        var context = NewContext("10.1.2.3", "198.51.100.7, 10.9.9.9, 10.8.8.8");

        // act
        await Run(context);

        // assert
        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("198.51.100.7"));
    }

    [Fact]
    public void RemoteAddressOfSingleEntryForwardedForHeader()
    {
        // arrange
        var context = NewContext("198.51.100.99", "203.0.113.9");

        // act
        var address = context.GetRemoteIPAddress();

        Out.WriteLine($"Single entry -> {address}");

        // assert
        address.Should().Be(IPAddress.Parse("203.0.113.9"));
    }

    [Fact]
    public void RemoteAddressOfCommaJoinedForwardedForHeader()
    {
        // arrange
        var context = NewContext("198.51.100.99", "203.0.113.9, 198.51.100.7");

        // act
        var address = context.GetRemoteIPAddress();

        Out.WriteLine($"Comma-joined entries -> {address}");

        // assert
        address.Should().Be(IPAddress.Parse("198.51.100.99"));
    }

    [Fact]
    public void RemoteAddressOfRepeatedForwardedForHeader()
    {
        // arrange
        var context = NewContext("198.51.100.99", new StringValues(["203.0.113.9", "198.51.100.7"]));

        // act
        var address = context.GetRemoteIPAddress();

        Out.WriteLine($"Repeated header -> {address}");

        // assert
        address.Should().Be(IPAddress.Parse("203.0.113.9"));
    }

    // Private methods

    private static DefaultHttpContext NewContext(string peer, StringValues forwardedFor)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        return context;
    }

    private static Task Run(HttpContext context)
    {
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.All }
            .SetKnownProxies(Networks);
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(options));
        return middleware.Invoke(context);
    }
}
