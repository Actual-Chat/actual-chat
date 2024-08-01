namespace ActualChat.UI.Blazor;

public class AfterRenderQueue
{
    private int _renderBatch;
    private int _onAfterRenderBatch;
    private ImmutableList<QueueItem> _queue = ImmutableList<QueueItem>.Empty;

    public void OnRenderStarted()
        => _renderBatch++;

    public void Enqueue(Action<object> action, object state)
        => _queue = _queue.Add(new QueueItem(_renderBatch, action, state));

    public void OnAfterRender()
    {
        _onAfterRenderBatch++;

        var queue = _queue;
        if (queue.Count == 0)
            return;

        foreach (var item in queue) {
            if (item.RenderBatch > _onAfterRenderBatch)
                break;
            _queue = _queue.Remove(item);
            item.Action(item.State);
        }
    }

    // Nested types
    private record QueueItem(int RenderBatch, Action<object> Action, object State);
}
