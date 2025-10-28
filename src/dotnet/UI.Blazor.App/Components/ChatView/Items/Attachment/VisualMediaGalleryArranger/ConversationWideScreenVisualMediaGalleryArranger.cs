namespace ActualChat.UI.Blazor.App.Components;

public class ConversationWideScreenVisualMediaGalleryArranger : IVisualMediaGalleryArranger
{
    public VisualMediaGalleryLine<TItem>[] Arrange<TItem>(IReadOnlyCollection<TItem> items, Func<TItem, (int width, int height)> getSize)
    {
        var resultBuilder = new VisualMediaGalleryArrangeResultBuilder<TItem>();
        var index = 0;
        foreach (var item in items) {
            var (width, height) = getSize(item);
            var tile = new VisualMediaGalleryTile<TItem>(item, width, height);
            var isLast = index == items.Count - 1;
            var lineIsFull = resultBuilder.Line.Count >= 4;
            var tileIsWide = tile.Proportions is VisualMediaProportions.Wide or VisualMediaProportions.ExtraWide;
            var lineHasManyWide = resultBuilder.Line.Count(t => t.Proportions is VisualMediaProportions.Wide or VisualMediaProportions.ExtraWide) > 2;

            if (items.Count == 3
                && index == 1
                && resultBuilder.Line is [{ Proportions: VisualMediaProportions.Wide or VisualMediaProportions.ExtraWide }]
                && tileIsWide) {
                resultBuilder.CompleteLine();
            }

            if ((!isLast && lineIsFull)
                || (isLast && resultBuilder.Line.Count == 4 && tileIsWide && lineHasManyWide)) {
                resultBuilder.CompleteLine();
            }

            resultBuilder.AddTile(tile);
            index++;
        }
        resultBuilder.CompleteLine();
        return resultBuilder.GetLines();
    }
}
