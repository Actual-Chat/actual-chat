namespace ActualChat.UI.Blazor;

public class CollectionParameterComparer : ParameterComparer
{
    public override bool AreEqual(object? oldValue, object? newValue)
    {
        if (oldValue == null && newValue == null)
            return true;

        if (oldValue is not IEnumerable oldCollection || newValue is not IEnumerable newCollection)
            return false;

        return oldCollection.OfType<object?>().ToHashSet().SetEquals(newCollection.OfType<object?>());
    }
}
