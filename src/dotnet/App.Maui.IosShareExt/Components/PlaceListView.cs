using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Contacts;
using NSPlace = ActualChat.App.Maui.IosShareExt.UI.NSHasId<ActualChat.Chat.Place?, ActualChat.PlaceId>;

namespace ActualChat.App.Maui.IosShareExt.Components;

public sealed class PlaceListView(IosHub hub) : ComputedStateView<PlaceListView.Model?>(hub), IUICollectionViewDelegate
{
    private static readonly NSString SectionId = (NSString)"Main";
    private const float ItemWidth = 40;
    private const float ItemHeight = 47; // 40 + 4 spacing + 3 underline
    private const float ListHeight = 52;

    private UICollectionView _collectionView = null!;
    private UICollectionViewDiffableDataSource<NSString, NSPlace> _dataSource = null!;

    private IPlaces Places => Hub.Places;
    private IContacts Contacts => Hub.Contacts;
    private ShareUI ShareUI => Hub.ShareUI;

    protected override void OnInitialRender(Model? model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        BackgroundColor = UIColor.Clear;

        var layout = new UICollectionViewFlowLayout {
            ScrollDirection = UICollectionViewScrollDirection.Horizontal,
            ItemSize = new CGSize(ItemWidth, ItemHeight),
            MinimumInteritemSpacing = 8,
            MinimumLineSpacing = 8,
            SectionInset = new UIEdgeInsets(0, 16, 0, 16),
        };

        _collectionView = new UICollectionView(CGRect.Empty, layout)
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = UIColor.Clear,
            ShowsHorizontalScrollIndicator = false,
        };

        AddSubview(_collectionView);

        NSLayoutConstraint.ActivateConstraints([
            _collectionView.TopAnchor.ConstraintEqualTo(TopAnchor),
            _collectionView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            _collectionView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            _collectionView.BottomAnchor.ConstraintEqualTo(BottomAnchor),
            // workaround for changing item height
            _collectionView.HeightAnchor.ConstraintEqualTo(ListHeight),
        ]);

        // Register cell
        var cellRegistration =
            UICollectionViewCellRegistrationExt.GetRegistration<UICollectionViewListCell, NSPlace>((cell, _, place) => {
                foreach (var subview in cell.ContentView.Subviews) {
                    subview.RemoveFromSuperview();
                    subview.DisposeSilently();
                }

                var placeCellView = new PlaceView(place.Value, Hub);
                placeCellView.TranslatesAutoresizingMaskIntoConstraints = false;
                cell.AddSubview(placeCellView);
                cell.BackgroundConfiguration = UIBackgroundConfiguration.ClearConfiguration;

                NSLayoutConstraint.ActivateConstraints([
                    placeCellView.TopAnchor.ConstraintEqualTo(cell.ContentView.TopAnchor),
                    placeCellView.LeadingAnchor.ConstraintEqualTo(cell.ContentView.LeadingAnchor),
                    placeCellView.TrailingAnchor.ConstraintEqualTo(cell.ContentView.TrailingAnchor),
                    placeCellView.BottomAnchor.ConstraintEqualTo(cell.ContentView.BottomAnchor),
                    placeCellView.WidthAnchor.ConstraintEqualTo(ItemWidth),
                    placeCellView.HeightAnchor.ConstraintEqualTo(ItemHeight),
                ]);
            });

        // Configure diffable data source
        _collectionView.Delegate = this;
        _dataSource = new UICollectionViewDiffableDataSource<NSString, NSPlace>(_collectionView,
            (collectionView, indexPath, item) =>
                collectionView.DequeueConfiguredReusableCell(cellRegistration, indexPath, item));

        SetPlaces(model);
    }

    protected override void OnStateChanged(Model? model)
        => SetPlaces(model);

    private void SetPlaces(Model? model)
    {
        var snapshot = new NSDiffableDataSourceSnapshot<NSString, NSPlace>();
        snapshot.AppendSections([SectionId]);

        var items = (model?.Places ?? []).Select(p => new NSPlace(p)).Prepend(new NSPlace(null)).ToArray();
        snapshot.AppendItems(items, SectionId);

        _dataSource.ApplySnapshot(snapshot, true);
    }

    protected override async Task<Model?> ComputeState(CancellationToken cancellationToken)
    {
        var placeIds = await Contacts.ListPlaceIds(Session, cancellationToken).ConfigureAwait(false);
        var places = await placeIds.Select(x => Places.Get(Session, x, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return new Model(places.SkipNullItems().ToList());
    }

    protected override ComputedState<Model?>.Options GetStateOptions()
        => GetStateOptions(GetType(),
            static t => new ComputedState<Model?>.Options {
                InitialValue = null,
                Category = GetStateCategory(t),
                UpdateDelayer = FixedDelayer.MinDelay,
            });

    void IUICollectionViewDelegate.ItemSelected(UICollectionView collectionView, NSIndexPath indexPath)
    {
        var place = _dataSource.GetItemIdentifier(indexPath);
        ShareUI.SelectedPlaceId.Value = place?.Id;
    }

    // Nested types

    public sealed record Model(IReadOnlyList<Place> Places);
}
