namespace ActualChat.Core.Server.UnitTests.Priming;

internal static class LockingComputeMethodPrimerTestExt
{
    public static int GetReservationCount<TKey, TValue>(this LockingComputeMethodPrimer<TKey, TValue> primer)
        where TKey : notnull
        => ((ICollection)typeof(LockingComputeMethodPrimer<TKey, TValue>)
            .GetField("_reservations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(primer)!).Count;
}
