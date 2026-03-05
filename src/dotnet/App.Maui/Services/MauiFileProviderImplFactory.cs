using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui.Services;

public class MauiFileProviderImplFactory(IServiceProvider services) : IMauiFileProviderImplFactory
{
#if ANDROID
    private AndroidContentDownloader Downloader => field ??= services.GetRequiredService<AndroidContentDownloader>();
#endif

    public IMauiFileProviderImpl Create(FilePath fileRef, FilePath transientFileRef)
    {
#if WINDOWS
        return new WindowsFileProviderImpl(fileRef);
#elif ANDROID
        return new AndroidFileProviderImpl(Downloader, fileRef);
#elif IOS
        return new IosFileProviderImpl(services, fileRef, transientFileRef);
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
