using Android.App;
using Android.Content;

namespace ActualChat.App.Maui;

[IntentFilter(
    new [] { Intent.ActionSend },
    DataMimeTypes = new [] { System.Net.Mime.MediaTypeNames.Text.Plain },
    Categories = new [] { Intent.CategoryDefault, Intent.CategoryBrowsable })]
/* Temporarily disabled files sharing, only plain text is supported
[IntentFilter(
    new [] { Intent.ActionSend },
    DataMimeTypes = new [] { "image/*" },
    Categories = new [] { Intent.CategoryDefault, Intent.CategoryBrowsable })]
[IntentFilter(
    new [] { Intent.ActionSendMultiple },
    DataMimeTypes = new [] { "image/*" },
    Categories = new [] { Intent.CategoryDefault, Intent.CategoryBrowsable })]
    */
public partial class MainActivity
{
}
