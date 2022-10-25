using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebView;
using WebKit;

namespace ActualChat.App.Maui;

public partial class MainPage
{
    /// <summary>
    /// Gets the <see cref="WKWebView"/> instance that was initialized.
    /// the default values to allow further configuring additional options.
    /// </summary>
    public WKWebView? PlatformWebView { get; private set; }

    // To manage iOS permissions, update Info.plist to include the necessary keys to access
    // the APIs required by your app. You may have to perform additional configuration to enable
    // use of those APIs from the WebView, as is done below.

    private partial void BlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
    {
        e.Configuration.AllowsInlineMediaPlayback = true;
        e.Configuration.MediaTypesRequiringUserActionForPlayback = WebKit.WKAudiovisualMediaTypes.None;
        e.Configuration.MediaPlaybackRequiresUserAction = false;
        e.Configuration.RequiresUserActionForMediaPlayback = false;
        e.Configuration.UpgradeKnownHostsToHttps = true;
    }

    private partial void BlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
        => PlatformWebView = e.WebView;
}
