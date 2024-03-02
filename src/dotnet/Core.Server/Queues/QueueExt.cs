namespace ActualChat;

public static class QueueExt
{
    public static Queue RequireValid(this Queue? queue)
    {
        if (queue == null)
            throw new ArgumentOutOfRangeException(nameof(queue), $"{nameof(Queue)} is null.");
        if (!queue.IsValid)
            throw new ArgumentOutOfRangeException(nameof(queue), $"Invalid {nameof(Queue)}: {queue}.");
        return queue;
    }
}
