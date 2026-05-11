using Android.App;
using Android.Content;

namespace ActualChat.App.Maui;

[MetaData("android.app.shortcuts", Resource = "@xml/share_targets")]
[IntentFilter(
    new [] { Intent.ActionSend },
    DataMimeTypes = new [] { System.Net.Mime.MediaTypeNames.Text.Plain },
    Categories = new [] { Intent.CategoryDefault, Intent.CategoryBrowsable })]
[IntentFilter(
    new [] { Intent.ActionSend },
    DataMimeTypes = new [] { "*/*" },
    Categories = new [] { Intent.CategoryDefault, Intent.CategoryBrowsable })]
[IntentFilter(
    new [] { Intent.ActionSendMultiple },
    DataMimeTypes = new [] { "*/*" },
    Categories = new [] { Intent.CategoryDefault, Intent.CategoryBrowsable })]
public partial class MainActivity
{ }
