using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui.Services;

public class MauiFileProviderImplFactory(IServiceProvider services) : IMauiFileProviderImplFactory
{
#if ANDROID
    private AndroidContentDownloader Downloader => field ??= services.GetRequiredService<AndroidContentDownloader>();
#endif

    public IMauiFileProviderImpl Create(FilePath fileRef)
    {
#if WINDOWS
        return new WindowsFileProviderImpl(fileRef);
#elif ANDROID
        return new AndroidFileProviderImpl(Downloader, fileRef);
#elif IOS
        return new AppleFileProviderImpl(services, fileRef);
#elif MACCATALYST
        return new MacFileProviderImpl(fileRef);
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
