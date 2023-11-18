namespace ActualChat.Core.UnitTests;

public class UlidTest
{
    [Fact]
    public void StringOrderIsTheSame()
    {
#pragma warning disable CA1305
        var ulid1 = Ulid.Parse("01GE2BCW3WACV5S0CAB3K0243E");
        var ulid2 = Ulid.Parse("01GE2BCW3WMSR3FAN5VJ41HR7Z");
        var ulid3 = Ulid.Parse("01GE2BCW3X452MQ18FF4BVCB30");
#pragma warning restore CA1305
    }
}
