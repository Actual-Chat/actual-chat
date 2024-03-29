namespace ActualChat;

public static class CancellationTokenExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CancellationTokenSource CreateDelayedTokenSource(
        this CancellationToken cancellationToken,
        TimeSpan cancellationDelay)
        => new DelayedCancellationTokenSource(cancellationToken, cancellationDelay);

    // Nested types

    private class DelayedCancellationTokenSource : CancellationTokenSource
    {
        private readonly TimeSpan _cancellationDelay;
        private readonly CancellationTokenRegistration _registration;

 #pragma warning disable CA1068
        public DelayedCancellationTokenSource(CancellationToken cancellationToken, TimeSpan cancellationDelay)
 #pragma warning restore CA1068
        {
            _cancellationDelay = cancellationDelay;
            _registration = cancellationToken.Register(static state => {
                var self = (DelayedCancellationTokenSource)state!;
                self.CancelAfter(self._cancellationDelay);
            }, this);
        }

        protected override void Dispose(bool disposing)
        {
            _registration.Dispose();
            base.Dispose(disposing);
        }
    }
}
