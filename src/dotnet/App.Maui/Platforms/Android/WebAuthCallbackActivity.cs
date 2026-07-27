using Android.App;
using Android.Content;
using Android.Content.PM;

namespace ActualChat.App.Maui;

[Activity(
    NoHistory = true,
    LaunchMode = LaunchMode.SingleTop,
    Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = MauiSettings.AppScheme,
    DataHost = MauiSettings.AuthCallbackHost)]
public class WebAuthCallbackActivity : WebAuthenticatorCallbackActivity;
