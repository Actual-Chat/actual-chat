using ActualChat.Aot;
using ActualChat.App.Maui.IosShareExt.Components;
using ActualChat.App.Maui.IosShareExt.UI;
using NSContact = ActualChat.App.Maui.IosShareExt.UI.NSHasId<ActualChat.Contacts.Contact, ActualChat.ContactId>;
using NSPlace = ActualChat.App.Maui.IosShareExt.UI.NSHasId<ActualChat.Chat.Place, ActualChat.PlaceId>;

namespace ActualChat.App.Maui.IosShareExt.Module;

internal class IosShareExtAotSource : IAotSource
{
    public void KeepTypes()
    {
        if (CodeKeeper.AlwaysTrue)
            return;

#if IOS
        // iOS loads the extension principal class by its [Register] name from Info.plist.
        // The default ctor and overridden UIViewController methods must be preserved.
        CodeKeeper.Keep<ShareViewController>();
        CodeKeeper.Keep<ShareExtensionApplication>();

        // UIView subclasses used by the extension. iOS dispatches selectors (LayoutSubviews,
        // TouchesBegan, etc.) to managed overrides, so each concrete UIView-derived type
        // needs its metadata preserved.
        CodeKeeper.Keep<ContactIconView>();
        CodeKeeper.Keep<ContactListView>();
        CodeKeeper.Keep<ContactSelectionView>();
        CodeKeeper.Keep<ContactView>();
        CodeKeeper.Keep<CounterBadgeView>();
        CodeKeeper.Keep<ErrorContentView>();
        CodeKeeper.Keep<ErrorView>();
        CodeKeeper.Keep<PlaceListView>();
        CodeKeeper.Keep<PlaceView>();
        CodeKeeper.Keep<ShareView>();
        CodeKeeper.Keep<SignInView>();
        CodeKeeper.Keep<SuccessView>();
        CodeKeeper.Keep<UploadProgressView>();

        // Closed generic NSObject subclasses passed into UICollectionViewDiffableDataSource;
        // closed generic metadata must be kept for ObjC dispatch and diffing.
        CodeKeeper.Keep<NSContact>();
        CodeKeeper.Keep<NSPlace>();
#endif
    }

    public (Type, AotTypeKind)[] ListTypes() => [];
}
