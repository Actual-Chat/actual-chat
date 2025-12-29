using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Contacts;
using NSContact = ActualChat.App.Maui.IosShareExt.UI.NSHasId<ActualChat.Contacts.Contact, ActualChat.ContactId>;

namespace ActualChat.App.Maui.IosShareExt.Components;

public sealed class ContactListView(IosHub hub) : ComputedStateView<ContactListView.Model?>(hub), IUICollectionViewDelegate
{
    private static readonly NSString SectionId = (NSString)"Main";
    private UICollectionViewDiffableDataSource<NSString, NSContact> _dataSource = null!;
    private UICollectionView _collectionView = null!;

    private ShareUI ShareUI => Hub.ShareUI;

    protected override void OnInitialRender(Model? model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        BackgroundColor = UIColor.Clear;

        var configuration = new UICollectionLayoutListConfiguration(UICollectionLayoutListAppearance.Plain);
        configuration.ShowsSeparators = false;
        var layout = UICollectionViewCompositionalLayout.GetLayout(configuration);
        _collectionView = new UICollectionView(CGRect.Empty, layout)
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = UIColor.Clear,
            // ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Never,
        };

        AddSubview(_collectionView);

        NSLayoutConstraint.ActivateConstraints([
            _collectionView.TopAnchor.ConstraintEqualTo(TopAnchor),
            _collectionView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            _collectionView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            _collectionView.BottomAnchor.ConstraintEqualTo(BottomAnchor)
        ]);

        var cellRegistration = UICollectionViewCellRegistrationExt.GetRegistration<UICollectionViewListCell, NSContact>(
            (cell, _, contact) =>
            {
                foreach (var subview in cell.ContentView.Subviews)
                    subview.RemoveFromSuperview();

                var contactItemView = new ContactView(contact, Hub);
                contactItemView.TranslatesAutoresizingMaskIntoConstraints = false;
                cell.ContentView.AddSubview(contactItemView);
                cell.BackgroundConfiguration = UIBackgroundConfiguration.ClearConfiguration;

                NSLayoutConstraint.ActivateConstraints([
                    contactItemView.TopAnchor.ConstraintEqualTo(cell.ContentView.TopAnchor),
                    contactItemView.LeadingAnchor.ConstraintEqualTo(cell.ContentView.LeadingAnchor),
                    contactItemView.TrailingAnchor.ConstraintEqualTo(cell.ContentView.TrailingAnchor),
                    contactItemView.BottomAnchor.ConstraintEqualTo(cell.ContentView.BottomAnchor),
                    contactItemView.HeightAnchor.ConstraintEqualTo(ContactView.Height),
                ]);
            });

        _collectionView.Delegate = this;
        _dataSource = new UICollectionViewDiffableDataSource<NSString, NSContact>(
            _collectionView,
            (collectionView1, indexPath, itemIdentifier) =>
                collectionView1.DequeueConfiguredReusableCell(cellRegistration, indexPath, itemIdentifier));

        SetContacts(model?.Contacts);
    }

    private void SetContacts(IReadOnlyList<Contact>? contacts)
    {
        contacts ??= [];
        var snapshot = new NSDiffableDataSourceSnapshot<NSString, NSContact>();
        snapshot.AppendSections([SectionId]);
        var items = contacts.Select(c => new NSContact(c)).ToArray();
        snapshot.AppendItems(items, SectionId);
        _dataSource.ApplySnapshot(snapshot, true);
    }

    protected override void OnStateChanged(Model? model)
        => SetContacts(model?.Contacts);

    protected override ComputedState<Model?>.Options GetStateOptions()
        => GetStateOptions(GetType(),
            static t => new ComputedState<Model?>.Options {
                InitialValue = null,
                Category = GetStateCategory(t),
                UpdateDelayer = FixedDelayer.MinDelay,
            });

    protected override async Task<Model?> ComputeState(CancellationToken cancellationToken)
    {
        var contacts = await ShareUI.ListContacts(cancellationToken).ConfigureAwait(false);
        return new Model(contacts);
    }

    void IUICollectionViewDelegate.ItemSelected(UICollectionView collectionView, NSIndexPath indexPath)
    {
        var contact = _dataSource.GetItemIdentifier(indexPath);
        if (contact == null)
            return;

        ShareUI.ToggleSelection(contact.Id!);
    }

    // Nested types

    public sealed record Model(IReadOnlyList<Contact> Contacts);
}
