using ActualChat.Maui;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

public class MauiFileProviderImplFactory(IServiceProvider services) : IMauiFileProviderImplFactory
{
#if ANDROID
    private AndroidContentDownloader Downloader => field ??= services.GetRequiredService<AndroidContentDownloader>();
#endif
#if IOS
    private IosVideoThumbnails VideoThumbnails => field ??= services.GetRequiredService<IosVideoThumbnails>();
#endif

    public IMauiFileProviderImpl Create(string fileRef)
    {
#if WINDOWS
        return new WindowsFileProviderImpl(fileRef);
#elif ANDROID
        return new AndroidFileProviderImpl(Downloader, fileRef);
#elif IOS
        return new IosFileProviderImpl(VideoThumbnails, fileRef);
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
