using ActualLab.Generators;

namespace ActualChat.App.Server.Module;

/// <summary>
/// Builds the <c>Content-Security-Policy</c> header value for app pages.
/// The per-request nonce it embeds is shared with the page via <see cref="HttpContext.Items"/>,
/// so the header and the page agree no matter which of them is produced first.
/// </summary>
public sealed class ContentSecurityPolicy
{
    private const string NonceItemKey = "ActualChat.CspNonce";
    private const int NonceLength = 12;

    private const string Voxt = Constants.Hosts.Voxt;
    private const string DevVoxt = Constants.Hosts.DevVoxt;
    private const string LocalVoxt = Constants.Hosts.LocalVoxt;
    private const string AppHosts =
        $"https://{Voxt} https://*.{Voxt} https://*.{DevVoxt} https://*.{LocalVoxt}";
    private const string AppSubdomains =
        $"https://*.{Voxt} https://*.{DevVoxt} https://*.{LocalVoxt}";

    private readonly string _unsafeEval;
    private readonly string _extraConnectSrc;

    public ContentSecurityPolicy(HostInfo hostInfo)
    {
        var isLocal = hostInfo.BaseUrlKind == BaseUrlKind.Local;
        // unsafe-eval is required for 'eval-source-map' to debug during local development
        _unsafeEval = isLocal ? "'unsafe-eval'" : "'wasm-unsafe-eval'";
        _extraConnectSrc = isLocal
            ? " ws://localhost:* wss://localhost:* https://raw.githubusercontent.com"
            : "";
    }

    public static string GetNonce(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(NonceItemKey, out var item) && item is string existingNonce)
            return existingNonce;

        var nonce = RandomStringGenerator.Default.Next(NonceLength);
        httpContext.Items[NonceItemKey] = nonce;
        return nonce;
    }

    public string Get(string nonce)
        => string.Join("; ", [
            "default-src 'self'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'",
            "object-src 'none'",
            "worker-src 'self' blob:",
            $"img-src 'self' {AppHosts} https://*.dicebear.com https://i.ytimg.com "
                + "https://www.googletagmanager.com https://static.klipy.com blob: data:",
            $"media-src 'self' {AppSubdomains} blob: data:",
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com/css",
            "font-src 'self' https://fonts.gstatic.com/s/ubuntu/",
            "frame-src https://www.youtube.com/ https://www.google.com/recaptcha/",
            $"script-src 'self' {_unsafeEval} 'nonce-{nonce}' "
                + "'sha256-IWFoCSRBx0i01sF31ty8kSeDtD8PRi/efpTSg/fumWo=' "
                + "'sha256-zU2GIxfpzdHVMmYxjdLyOs4r4XWtc9oFoEFrwbxzdQg=' "
                + "https://www.youtube.com/iframe_api https://www.youtube.com/s/player/ "
                + "https://www.googletagmanager.com/gtag/js https://www.gstatic.com/recaptcha/releases/",
            $"connect-src 'self'{_extraConnectSrc} {AppHosts} wss://{Voxt} wss://*.{Voxt} "
                + "https://*.dicebear.com https://*.ingest.sentry.io "
                + "https://firebaseinstallations.googleapis.com https://fcmregistrations.googleapis.com "
                + "https://firebase.googleapis.com https://*.google-analytics.com/g/collect "
                + "https://*.analytics.google.com/g/collect https://www.google.com/recaptcha/enterprise/clr",
            "upgrade-insecure-requests",
        ]);
}
