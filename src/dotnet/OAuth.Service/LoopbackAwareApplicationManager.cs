using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;

namespace ActualChat.OAuth;

// Not sealed: OpenIddict resolves it as OpenIddictApplicationManager<T>, which stays open for derivation.

/// <summary>
/// RFC 8252 §7.3: a registered loopback redirect (http://localhost/…, http://127.0.0.1/…, http://[::1]/…)
/// matches the same URI on any port — Claude Code and Cursor pick an ephemeral port per session.
/// </summary>
public class LoopbackAwareApplicationManager(
    IOpenIddictApplicationCache<OpenIddictEntityFrameworkCoreApplication> cache,
    ILogger<LoopbackAwareApplicationManager> logger,
    IOptionsMonitor<OpenIddictCoreOptions> options,
    IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication> store)
    : OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication>(cache, logger, options, store)
{
    public override async ValueTask<bool> ValidateRedirectUriAsync(
        OpenIddictEntityFrameworkCoreApplication application, string uri, CancellationToken cancellationToken = default)
    {
        if (await base.ValidateRedirectUriAsync(application, uri, cancellationToken).ConfigureAwait(false))
            return true;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var requested) || !OAuthApplications.IsLoopback(requested))
            return false;

        var registeredUris = await GetRedirectUrisAsync(application, cancellationToken).ConfigureAwait(false);
        foreach (var registered in registeredUris) {
            if (!Uri.TryCreate(registered, UriKind.Absolute, out var candidate))
                continue;
            if (!OAuthApplications.IsLoopback(candidate))
                continue;
            if (string.Equals(candidate.Host, requested.Host, StringComparison.OrdinalIgnoreCase)
                && candidate.AbsolutePath == requested.AbsolutePath
                && candidate.Query == requested.Query)
                return true;
        }

        return false;
    }
}
