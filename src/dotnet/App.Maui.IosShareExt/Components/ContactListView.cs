using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Contacts;
using NSContact = ActualChat.App.Maui.IosShareExt.UI.NSHasId<ActualChat.Contacts.Contact, ActualChat.ContactId>;

namespace ActualChat.App.Maui.IosShareExt.Components;

public sealed class ContactListView : ComputedStateView<ContactListView.Model?>, IUICollectionViewDelegate
{
    private static readonly NSString SectionId = (NSString)"Main";
    private readonly UICollectionViewDiffableDataSource<NSString, NSContact> _dataSource;
    private readonly UICollectionView _collectionView;
    private ContactListSkeletonView? _skeletonView;

    private ShareUI ShareUI => Hub.ShareUI;

    // Built here, not in OnInitialRender: that runs once the contacts are in, too late for a skeleton
    public ContactListView(IosHub hub) : base(hub)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        BackgroundColor = UIColor.Clear;

        var configuration = new UICollectionLayoutListConfiguration(UICollectionLayoutListAppearance.Plain);
        configuration.ShowsSeparators = false;
        var layout = UICollectionViewCompositionalLayout.GetLayout(configuration);
        _collectionView = new UICollectionView(CGRect.Empty, layout) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = UIColor.Clear,
        };
        AddSubview(_collectionView);

        _skeletonView = new ContactListSkeletonView();
        AddSubview(_skeletonView);

        NSLayoutConstraint.ActivateConstraints([
            _collectionView.TopAnchor.ConstraintEqualTo(TopAnchor),
            _collectionView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            _collectionView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            _collectionView.BottomAnchor.ConstraintEqualTo(BottomAnchor),

            _skeletonView.TopAnchor.ConstraintEqualTo(TopAnchor),
            _skeletonView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            _skeletonView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            _skeletonView.BottomAnchor.ConstraintEqualTo(BottomAnchor),
        ]);

        var cellRegistration = UICollectionViewCellRegistrationExt.GetRegistration<UICollectionViewListCell, NSContact>(
            (cell, _, contact) => {
                foreach (var subview in cell.ContentView.Subviews) {
                    subview.RemoveFromSuperview();
                    subview.DisposeSilently();
                }

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
    }

    protected override void OnInitialRender(Model? model)
        => SetContacts(model?.Contacts);

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

    // Private methods

    private void SetContacts(IReadOnlyList<Contact>? contacts)
    {
        // Null is "no result yet"; an empty list is a real answer, and retires the skeleton
        if (contacts is not null)
            HideSkeleton();

        contacts ??= [];
        var snapshot = new NSDiffableDataSourceSnapshot<NSString, NSContact>();
        snapshot.AppendSections([SectionId]);
        var items = contacts.Select(c => new NSContact(c)).ToArray();
        snapshot.AppendItems(items, SectionId);
        _dataSource.ApplySnapshot(snapshot, true);
    }

    private void HideSkeleton()
    {
        if (_skeletonView is null)
            return;

        _skeletonView.RemoveFromSuperview();
        _skeletonView.DisposeSilently();
        _skeletonView = null;
    }

    // Nested types

    public sealed record Model(IReadOnlyList<Contact> Contacts);
}
