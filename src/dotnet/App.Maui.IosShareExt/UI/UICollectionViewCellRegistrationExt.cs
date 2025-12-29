namespace ActualChat.App.Maui.IosShareExt.UI;

public static class UICollectionViewCellRegistrationExt
{
    public static UICollectionViewCellRegistration GetRegistration<TCell, TIdentifier>(
        UICollectionViewCellRegistrationConfigurationHandler<TCell, TIdentifier> handler)
        where TCell : UICollectionViewCell
        where TIdentifier : NSObject
        => UICollectionViewCellRegistration.GetRegistration(typeof(TCell),
            (cell, path, item) => handler((TCell)cell, path, (TIdentifier)item));
}

public delegate void UICollectionViewCellRegistrationConfigurationHandler<in TCell, in TIdentifier>(
    TCell cell,
    NSIndexPath indexPath,
    TIdentifier itemId)
    where TCell : UICollectionViewCell
    where TIdentifier : NSObject;

